using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        // Container-spawned items (grocery-bag "Spawn all"/"Spawn one") exist in
        // neither save, so they cannot be discovered by stable scene path. The peer
        // whose player physically opens the bag lets the game spill naturally and
        // captures the clones; the host mints a net id per clone and broadcasts an
        // ItemSpawn manifest; every OTHER peer materializes matching objects from
        // its own scene (adopt / steal / instantiate from a template) and binds
        // them. Nobody drives a remote bag FSM: the bag's "Confirm" state bounces
        // straight back to "Wait player" without a live player interaction, and the
        // spiller's bag-consumption despawn destroys the replica bags on the other
        // peers before a manifest could use them (both observed in-game, v29-v31).
        //
        // COVERAGE-ROADMAP 7.2 — Spawner/* completeness audit (14 subroots):
        //   COVERED (bag "Spawn one/all" capture flow above):
        //     BagContentsStore, BagContentsFleetari, CreateBagStore, CreateBagFleetari,
        //     CreateItems, CreateItemsSeparate.
        //   COVERED (native factory identity, ItemWorldSync.Factories.cs, v106):
        //     CreateTrophiesAmateur/Icerace/Junior/RallyAMA/RallyJR (15 factories).
        //   EXCLUDED — SPAWNITEM-event spawners (CreateMooseMeat, CreatePartsPackages,
        //     CreateSprayCans).
        //     These fire a SPAWNITEM action on a game event (chop moose, buy part, win race)
        //     rather than pouring near an opened container, so the bag capture window does
        //     not apply. Item pose snapshots cannot create missing bodies, including
        //     for late joiners. These factories still need native identity, contents /
        //     condition and save/deletion adapters, plus their gameplay authority paths.
        //     Tracked as the residual of 7.1 (moose meat) / 5.2 (rally parts).

        /// <summary>How far from the container's transform a fresh clone may appear.</summary>
        private const float SpawnCaptureRadius = 6f;

        /// <summary>Give up gathering a spawn's clones this long after the trigger.</summary>
        private const float SpawnCaptureHardSeconds = 2f;

        /// <summary>Finalize a capture once the clone count holds steady this long.</summary>
        private const float SpawnCaptureStableSeconds = 0.3f;

        /// <summary>Guest: give up waiting for the host's manifest after an offer.</summary>
        private const float SpawnOfferManifestSeconds = 6f;

        private const int GuestSpawnMaxItems = SpawnIntent.MaxItems;
        private const int GuestSpawnMaxTemplateNameLength = 128;
        private const float GuestSpawnPoseMaxAgeSeconds = 2f;
        private const float GuestSpawnMaxDistance = 12f;

        private sealed class PendingSpawn
        {
            public uint ContainerId;
            public ushort Epoch;
            public string StateName = string.Empty;
            public bool IsHost;
            public bool IsOffer;            // guest: local spill captured, offered to the host
            public bool AwaitingManifest;   // offer sent; parked until the manifest binds it
            public ushort OfferSeq;         // SpawnIntent.Sequence — the manifest echoes it back
            public byte HostOwnerId = WorldSyncIds.NoOwner;
            public List<ItemSpawn.Entry>? Descriptors;
            public bool IsReplay;           // join-snapshot replay — stale-clone rules apply

            public Vector3 Near;
            public bool HasNear;
            public float HardDeadline;
            public int LastCount;
            public float StableSince;
            public readonly List<Rigidbody> Captured = new List<Rigidbody>();
        }

        /// <summary>How far from its manifest pose an existing local clone may rest and still be adopted.</summary>
        private const float SpawnAdoptRadius = 3f;

        private readonly List<PendingSpawn> _pendingSpawns = new List<PendingSpawn>();
        // Host: manifests it minted this session, re-sent (live-refreshed) with the join
        // snapshot so late joiners get already-spilled contents. See BuildSpawnReplayManifests.
        private readonly Dictionary<long, ItemSpawn> _hostSpawnManifests = new Dictionary<long, ItemSpawn>();
        // Guest: every id the host has ever named in an item snapshot. An item tracked
        // locally but absent here is host-unknown — i.e. a stale clone our scanner
        // grabbed under a per-peer ordinal id — and safe to rebind to a manifest id.
        private readonly HashSet<uint> _snapshotSeenIds = new HashSet<uint>();
        // Host: next manifest epoch per container id. Lives here (not on the FSM
        // registration) because guest-offered spills have no host-side registration —
        // one counter must serve both paths or their (container, epoch) dedup keys
        // would collide.
        private readonly Dictionary<uint, ushort> _spawnEpochs = new Dictionary<uint, ushort>();
        private readonly Dictionary<byte, ushort> _lastGuestSpawnSequences = new Dictionary<byte, ushort>();
        private ushort _outSpawnSequence;

        /// <summary>Host: a player (re)joined — its spawn counter restarted; drop the stale latch.</summary>
        public void ForgetPlayerSpawnSequence(byte playerId) => _lastGuestSpawnSequences.Remove(playerId);

        private static long SpawnKey(uint containerId, ushort epoch) => ((long)containerId << 16) | epoch;

        private ushort MintSpawnEpoch(uint containerId)
        {
            _spawnEpochs.TryGetValue(containerId, out ushort last);
            ushort next = (ushort)(last + 1);
            _spawnEpochs[containerId] = next;
            return next;
        }

        /// <summary>Host: the local bag just spilled; gather its clones, then publish the manifest.</summary>
        internal void StartHostSpawnCapture(uint containerId, string stateName, Vector3 near)
        {
            ushort epoch = MintSpawnEpoch(containerId);
            _pendingSpawns.Add(new PendingSpawn
            {
                ContainerId = containerId,
                Epoch = epoch,
                StateName = stateName,
                IsHost = true,
                Near = near,
                HasNear = true,
                HardDeadline = Time.unscaledTime + SpawnCaptureHardSeconds,
                StableSince = Time.unscaledTime,
            });
        }

        /// <summary>
        /// Guest: the local bag just spilled naturally; gather the clones, then offer
        /// them to the host, which mints ids and answers with the binding manifest.
        /// </summary>
        internal void StartGuestSpawnOffer(uint containerId, string stateName, Vector3 near)
        {
            _pendingSpawns.Add(new PendingSpawn
            {
                ContainerId = containerId,
                StateName = stateName,
                IsOffer = true,
                Near = near,
                HasNear = true,
                HardDeadline = Time.unscaledTime + SpawnCaptureHardSeconds,
                StableSince = Time.unscaledTime,
            });
        }

        /// <summary>
        /// Host gate for a guest's naturally-spilled bag capture. Runtime bags have
        /// intentionally peer-local ids, so their contents cannot be re-derived on
        /// the host; nevertheless an offer must be fresh, bounded, replay-safe, and
        /// physically close to the player before it can mint shared item identities.
        /// </summary>
        internal bool TryAcceptGuestSpawnIntent(SpawnIntent intent, byte playerId)
        {
            if (intent.PlayerId != playerId || intent.Items.Count == 0 || intent.Items.Count > GuestSpawnMaxItems)
                return false;

            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return false;

            Vector3 playerPosition = Vector3.zero;
            bool foundPlayer = false;
            float now = Time.unscaledTime;
            foreach (var player in session.Players)
            {
                if (player.PlayerId != playerId) continue;
                if (player.LastTransformTime <= 0f
                    || now - player.LastTransformTime > GuestSpawnPoseMaxAgeSeconds)
                    return false;
                playerPosition = player.Position;
                foundPlayer = true;
                break;
            }
            if (!foundPlayer) return false;

            for (int i = 0; i < intent.Items.Count; i++)
            {
                var entry = intent.Items[i];
                if (string.IsNullOrEmpty(entry.TemplateName)
                    || entry.TemplateName.Length > GuestSpawnMaxTemplateNameLength)
                    return false;

                Vector3 position = entry.Position.ToUnity();
                Quaternion rotation = entry.Rotation.ToUnity();
                if (!IsFinite(position) || !TryNormalize(rotation, out rotation)
                    || (position - playerPosition).sqrMagnitude > GuestSpawnMaxDistance * GuestSpawnMaxDistance
                    || FindSpawnBodyByName(entry.TemplateName, position, -1f, untrackedOnly: false) == null)
                    return false;

                entry.Position = new NetVector3(position.x, position.y, position.z);
                entry.Rotation = new NetQuaternion(rotation.x, rotation.y, rotation.z, rotation.w);
                intent.Items[i] = entry;
            }

            ushort lastSequence;
            if (_lastGuestSpawnSequences.TryGetValue(playerId, out lastSequence))
            {
                ushort difference = (ushort)(intent.Sequence - lastSequence);
                if (difference == 0 || difference > short.MaxValue)
                {
                    WinterMPPlugin.Log.LogDebug(
                        $"WorldSync: dropped stale spawn offer from player {playerId} (sequence {intent.Sequence}).");
                    return false;
                }
            }
            _lastGuestSpawnSequences[playerId] = intent.Sequence;
            return true;
        }

        /// <summary>Host: a guest's bag spilled — materialize its clones here, mint ids, broadcast.</summary>
        internal void OnHostSpawnIntent(SpawnIntent intent)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;
            if (intent.Items.Count == 0)
            {
                Util.BootTrace.Crumb($"SPAWN-INTENT-EMPTY host player {intent.PlayerId} {intent.ContainerNetId:X8}");
                return;
            }

            ushort epoch = MintSpawnEpoch(intent.ContainerNetId);
            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: spawn offer from player {intent.PlayerId}: {intent.ContainerNetId:X8} '{intent.StateName}', {intent.Items.Count} item(s) — minting #{epoch}.");
            Util.BootTrace.Crumb(
                $"SPAWN-INTENT-EXEC host player {intent.PlayerId} {intent.ContainerNetId:X8} #{epoch} '{intent.StateName}' offered={intent.Items.Count}");

            var manifest = new ItemSpawn
            {
                ContainerNetId = intent.ContainerNetId,
                Epoch = epoch,
                OwnerPlayerId = intent.PlayerId,
                StateName = intent.StateName,
                OfferSequence = intent.Sequence,
            };

            float now = Time.unscaledTime;
            for (int i = 0; i < intent.Items.Count; i++)
            {
                var offered = intent.Items[i];
                uint netId = StableHash.Fnv1a32("spawn:" + intent.ContainerNetId + ":" + epoch + ":" + i);
                if (_items.ContainsKey(netId))
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: spawn id collision {netId:X8}, skipping a clone.");
                    continue;
                }

                var entry = new ItemSpawn.Entry
                {
                    NetId = netId,
                    TemplateName = offered.TemplateName,
                    Position = offered.Position,
                    Rotation = offered.Rotation,
                };
                if (!MaterializeSpawnEntry(entry, intent.PlayerId, now, allowSteal: false)) continue;
                manifest.Items.Add(entry);
            }

            Util.BootTrace.Crumb(
                $"SPAWN-MINT host {intent.ContainerNetId:X8} #{epoch} offered={intent.Items.Count} minted={manifest.Items.Count} (guest spill)");
            SyncEventLog.Record("spawn", $"{intent.ContainerNetId:X8} #{epoch} x{manifest.Items.Count} (player {intent.PlayerId})");
            if (_bridge.SelfTest)
                SessionManager.Instance?.SendChat($"[ws] spilled {manifest.Items.Count} item(s) (player {intent.PlayerId})");

            if (manifest.Items.Count > 0)
                _hostSpawnManifests[SpawnKey(intent.ContainerNetId, epoch)] = manifest;

            // Answer even an all-failed offer: the empty manifest releases the guest's
            // parked clones immediately instead of letting them sit out the 6 s timeout.
            session.SendWorldMessage(manifest, Channel.ReliableOrdered);
        }

        /// <summary>Guest: a spawn manifest arrived; bind our own offered clones, or materialize.</summary>
        internal void StartGuestSpawnBind(ItemSpawn message)
        {
            if (message.IsFactory && !ValidateTrophyManifest(message)) return;
            if (!_spawnLifecycle.AcceptManifest(message))
            {
                WinterMPPlugin.Log.LogDebug(
                    $"WorldSync: duplicate or invalid spawn manifest {message.ContainerNetId:X8} #{message.Epoch} ignored.");
                return;
            }

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: spawn manifest {message.ContainerNetId:X8} #{message.Epoch} -> '{message.StateName}', {message.Items.Count} item(s), owner {message.OwnerPlayerId} (guest).");
            Util.BootTrace.Crumb(
                $"SPAWN-MANIFEST guest recv {message.ContainerNetId:X8} #{message.Epoch} items={message.Items.Count} owner={message.OwnerPlayerId} state='{message.StateName}'");

            // Every manifest id is host-named. Record them like snapshot ids so the
            // stale-clone steal's "host has never named this id" test stays sound for
            // items bound from live spills after the join snapshot.
            for (int i = 0; i < message.Items.Count; i++)
                _snapshotSeenIds.Add(message.Items[i].NetId);

            if (message.IsFactory) { QueueTrophyManifest(message); return; }

            var session = SessionManager.Instance;
            if (session != null && !message.IsReplay
                && message.OwnerPlayerId == session.LocalPlayerId && TryBindOfferManifest(message))
                return;

            // Repeated resyncs refresh one pending job without postponing it forever.
            foreach (var pending in _pendingSpawns)
            {
                if (pending.IsHost || pending.IsOffer || pending.ContainerId != message.ContainerNetId || pending.Epoch != message.Epoch) continue;
                pending.Descriptors = new List<ItemSpawn.Entry>(message.Items);
                pending.IsReplay |= message.IsReplay;
                pending.HostOwnerId = message.OwnerPlayerId;
                return;
            }

            // Someone else's spill (or our own offer already timed out): nothing to
            // gather locally — materialize each entry. Processing happens on the next
            // Update tick, so a despawn corpse destroyed in this receive frame is gone
            // before adoption can see it. Join-snapshot replays defer a full capture
            // window on top: the item scanner needs a pass to register stale clones
            // for the steal path.
            _pendingSpawns.Add(new PendingSpawn
            {
                ContainerId = message.ContainerNetId,
                Epoch = message.Epoch,
                StateName = message.StateName,
                HostOwnerId = message.OwnerPlayerId,
                Descriptors = new List<ItemSpawn.Entry>(message.Items),
                IsReplay = message.IsReplay,
                HardDeadline = Time.unscaledTime + (message.IsReplay ? SpawnCaptureHardSeconds : 0f),
                StableSince = Time.unscaledTime,
            });
        }

        /// <summary>The manifest answers one of our own offers: bind the parked clones to its ids.</summary>
        private bool TryBindOfferManifest(ItemSpawn message)
        {
            for (int i = 0; i < _pendingSpawns.Count; i++)
            {
                var pending = _pendingSpawns[i];
                if (!pending.IsOffer || !pending.AwaitingManifest) continue;
                // Paired by the echoed sequence — (container, state) alone cannot tell
                // two quick "Spawn one" offers on the same bag apart, and binding the
                // wrong one would swallow the other's manifest for good.
                if (pending.OfferSeq != message.OfferSequence) continue;
                if (pending.ContainerId != message.ContainerNetId || pending.StateName != message.StateName) continue;

                BindOfferClones(pending, message);
                _pendingSpawns.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Pair our captured clones with the minted entries by template name (identical
        /// clones are interchangeable, so first-free wins) and register them as locally
        /// owned — they are live local physics and we stream them from birth. Clones
        /// the host could not template stay unpaired and go back to the scanner.
        /// </summary>
        private void BindOfferClones(PendingSpawn pending, ItemSpawn message)
        {
            float now = Time.unscaledTime;
            var used = new bool[pending.Captured.Count];
            int bound = 0;

            for (int d = 0; d < message.Items.Count; d++)
            {
                var entry = message.Items[d];
                for (int c = 0; c < pending.Captured.Count; c++)
                {
                    if (used[c]) continue;
                    var body = pending.Captured[c];
                    if (body == null) { used[c] = true; continue; }
                    if (body.gameObject.name != entry.TemplateName) continue;

                    used[c] = true;
                    if (_spawnLifecycle.IsRetired(entry.NetId))
                    {
                        _trackedBodies.Remove(body); UnityEngine.Object.Destroy(body.gameObject);
                        break;
                    }
                    BindOfferedBodyAsOwner(body, entry.NetId, now);
                    bound++;
                    break;
                }
            }

            int released = 0;
            for (int c = 0; c < pending.Captured.Count; c++)
            {
                if (used[c]) continue;
                var extra = pending.Captured[c];
                if (extra != null) { _trackedBodies.Remove(extra); released++; }
            }

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: spawn {message.ContainerNetId:X8} #{message.Epoch} — bound {bound} own clone(s), released {released}.");
            Util.BootTrace.Crumb(
                $"SPAWN-OFFER-BIND guest {message.ContainerNetId:X8} #{message.Epoch} bound={bound} released={released}");
            if (_bridge.SelfTest)
                SessionManager.Instance?.SendChat($"[ws] spilled {bound} item(s) (guest)");
        }

        private void BindOfferedBodyAsOwner(Rigidbody body, uint netId, float now)
        {
            if (_items.ContainsKey(netId)) return;

            var item = new SyncedItem
            {
                Body = body,
                Path = ScenePath.Of(body.transform),
                Id = netId,
                IsVehicle = false,
                LocallyOwned = true,
                LastPosition = body.transform.position,
                LastMovedAt = now,
            };
            _items[netId] = item;
            TryRegisterConsumableHooks(item);
        }

        internal void ProcessPendingSpawns(SessionManager session)
        {
            ProcessPackageRemovals();
            ProcessPackages(session);
            ProcessReplacementParts(session);
            ProcessPackageOpening(session);
            ProcessPartFitting(session);
            ProcessTrophySpawns(session);
            if (_pendingSpawns.Count == 0) return;
            float now = Time.unscaledTime;

            // Captures/offers first, materializes second: a gather tracked-locks the
            // local spill's clones, which is what stops a same-tick manifest's adopt
            // from hijacking them (two players spilling side-by-side in one frame).
            for (int i = _pendingSpawns.Count - 1; i >= 0; i--)
            {
                var pending = _pendingSpawns[i];
                if (!pending.IsHost && !pending.IsOffer) continue;

                if (pending.IsOffer && pending.AwaitingManifest)
                {
                    if (now >= pending.HardDeadline)
                    {
                        // Host never answered: hand the clones back to the scanner so
                        // they at least stay usable locally instead of parked forever.
                        int released = ReleaseCapturedBodies(pending);
                        WinterMPPlugin.Log.LogWarning(
                            $"WorldSync: spawn offer {pending.ContainerId:X8} '{pending.StateName}' got no manifest; released {released} clone(s).");
                        Util.BootTrace.Crumb($"SPAWN-OFFER-TIMEOUT guest {pending.ContainerId:X8} released={released}");
                        _pendingSpawns.RemoveAt(i);
                    }
                    continue;
                }

                GatherSpawnClones(pending);

                if (pending.Captured.Count != pending.LastCount)
                {
                    pending.LastCount = pending.Captured.Count;
                    pending.StableSince = now;
                }

                bool ready = (pending.Captured.Count > 0 && now - pending.StableSince >= SpawnCaptureStableSeconds)
                    || now >= pending.HardDeadline;
                if (!ready) continue;

                if (pending.IsHost)
                {
                    FinalizeHostSpawn(session, pending);
                    _pendingSpawns.RemoveAt(i);
                }
                else if (TrySendSpawnOffer(session, pending))
                {
                    pending.AwaitingManifest = true;
                    pending.HardDeadline = now + SpawnOfferManifestSeconds;
                }
                else
                {
                    ReleaseCapturedBodies(pending);
                    Util.BootTrace.Crumb($"SPAWN-OFFER-EMPTY guest {pending.ContainerId:X8} '{pending.StateName}'");
                    _pendingSpawns.RemoveAt(i);
                }
            }

            // Other peers' spills: their clones live on those machines, so there is
            // nothing to gather here — materialize from the manifest when due.
            for (int i = _pendingSpawns.Count - 1; i >= 0; i--)
            {
                var pending = _pendingSpawns[i];
                if (pending.IsHost || pending.IsOffer) continue;

                if (now >= pending.HardDeadline)
                {
                    MaterializeGuestSpawn(pending);
                    _pendingSpawns.RemoveAt(i);
                }
            }
        }

        private int ReleaseCapturedBodies(PendingSpawn pending)
        {
            int released = 0;
            for (int i = 0; i < pending.Captured.Count; i++)
            {
                var body = pending.Captured[i];
                if (body != null && _trackedBodies.Remove(body)) released++;
            }

            return released;
        }

        /// <summary>False when every captured clone died before the send — nothing to offer.</summary>
        private bool TrySendSpawnOffer(SessionManager session, PendingSpawn pending)
        {
            var intent = new SpawnIntent
            {
                PlayerId = session.LocalPlayerId,
                ContainerNetId = pending.ContainerId,
                StateName = pending.StateName,
                Sequence = ++_outSpawnSequence,
            };

            for (int i = 0; i < pending.Captured.Count; i++)
            {
                var body = pending.Captured[i];
                if (body == null) continue;
                intent.Items.Add(new SpawnIntent.Entry
                {
                    TemplateName = body.gameObject.name,
                    Position = body.transform.position.ToNet(),
                    Rotation = body.transform.rotation.ToNet(),
                });
            }

            if (intent.Items.Count == 0) return false;

            pending.OfferSeq = intent.Sequence;
            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: offering {intent.Items.Count} spilled clone(s) of {pending.ContainerId:X8} '{pending.StateName}' to the host.");
            Util.BootTrace.Crumb($"SPAWN-OFFER-SENT guest {pending.ContainerId:X8} '{pending.StateName}' seq={intent.Sequence} items={intent.Items.Count}");
            session.SendWorldMessage(intent, Channel.ReliableOrdered);
            return true;
        }

        // Known limitation: the capture is a blind radius sweep, so an unrelated
        // pickable that APPEARS inside 6 m during the 2 s window (e.g. a replicated
        // store purchase spawning goods at the counter) gets minted into the
        // manifest; other peers may then hold a duplicate of their own copy. Rare
        // store-counter coincidence — revisit with a name filter if play surfaces it.
        private void GatherSpawnClones(PendingSpawn pending)
        {
            if (!pending.HasNear) return;

            float radiusSqr = SpawnCaptureRadius * SpawnCaptureRadius;
            var bodies = Resources.FindObjectsOfTypeAll(typeof(Rigidbody));
            foreach (var obj in bodies)
            {
                if (pending.Captured.Count >= ItemSpawn.MaxItems) break;

                var body = obj as Rigidbody;
                // Already-tracked bodies are existing save items, not fresh clones —
                // the tracked-skip is what keeps us from grabbing the player's held
                // chips or other loose cargo already in range.
                if (body == null || _trackedBodies.ContainsKey(body)) continue;

                try
                {
                    if (!body.gameObject.activeInHierarchy) continue;
                    if (FindPackageUse(body) != null || NativePartIdentity.FindData(body.transform) != null) continue;
                    if (!SyncCatalog.IsPickableRigidbody(body)) continue;
                    if ((body.transform.position - pending.Near).sqrMagnitude > radiusSqr) continue;

                    // Lock it from the periodic scanner immediately so it cannot be
                    // registered under a position-ordinal id that disagrees per peer.
                    _trackedBodies[body] = true;
                    pending.Captured.Add(body);
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogDebug($"WorldSync: skipped rigidbody during spawn capture: {e.Message}");
                }
            }
        }

        private void FinalizeHostSpawn(SessionManager session, PendingSpawn pending)
        {
            var manifest = new ItemSpawn
            {
                ContainerNetId = pending.ContainerId,
                Epoch = pending.Epoch,
                OwnerPlayerId = session.LocalPlayerId,
                StateName = pending.StateName,
            };

            float now = Time.unscaledTime;
            for (int i = 0; i < pending.Captured.Count; i++)
            {
                var body = pending.Captured[i];
                if (body == null) continue;

                uint netId = StableHash.Fnv1a32("spawn:" + pending.ContainerId + ":" + pending.Epoch + ":" + i);
                if (_items.ContainsKey(netId))
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: spawn id collision {netId:X8}, skipping a clone.");
                    continue;
                }

                var item = new SyncedItem
                {
                    Body = body,
                    Path = ScenePath.Of(body.transform),
                    Id = netId,
                    IsVehicle = false,
                    LocallyOwned = true,
                    LastPosition = body.transform.position,
                    LastMovedAt = now,
                };
                _items[netId] = item;
                TryRegisterConsumableHooks(item);

                manifest.Items.Add(new ItemSpawn.Entry
                {
                    NetId = netId,
                    TemplateName = body.gameObject.name,
                    Position = body.transform.position.ToNet(),
                    Rotation = body.transform.rotation.ToNet(),
                });
            }

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: spawn {pending.ContainerId:X8} #{pending.Epoch} — host minted {manifest.Items.Count} item(s).");
            Util.BootTrace.Crumb($"SPAWN-MINT host {pending.ContainerId:X8} #{pending.Epoch} captured={pending.Captured.Count} minted={manifest.Items.Count}");
            SyncEventLog.Record("spawn", $"{pending.ContainerId:X8} #{pending.Epoch} x{manifest.Items.Count}");
            if (_bridge.SelfTest)
                SessionManager.Instance?.SendChat($"[ws] spilled {manifest.Items.Count} item(s) (host)");

            if (manifest.Items.Count > 0)
            {
                _hostSpawnManifests[SpawnKey(pending.ContainerId, pending.Epoch)] = manifest;
                session.SendWorldMessage(manifest, Channel.ReliableOrdered);
            }
        }

        /// <summary>
        /// Host: manifests for this session's spills, refreshed against live item state
        /// (eaten/despawned entries dropped, poses updated), for the join snapshot. A
        /// late joiner otherwise has no way to ever see these items — they exist in no
        /// save, and the bag that spilled them is spent or absent on its side.
        /// </summary>
        internal IEnumerable<ItemSpawn> BuildSpawnReplayManifests()
        {
            foreach (var factory in BuildTrophyReplayManifests()) yield return factory;
            List<long>? dead = null;

            foreach (var pair in _hostSpawnManifests)
            {
                var stored = pair.Value;
                var replay = new ItemSpawn
                {
                    ContainerNetId = stored.ContainerNetId,
                    Epoch = stored.Epoch,
                    OwnerPlayerId = stored.OwnerPlayerId,
                    StateName = stored.StateName,
                    Flags = ItemSpawn.FlagReplay,
                };

                for (int i = 0; i < stored.Items.Count; i++)
                {
                    var entry = stored.Items[i];
                    if (_spawnLifecycle.IsRetired(entry.NetId) || !_items.TryGetValue(entry.NetId, out var live) || live.Body == null) continue;

                    entry.Position = live.Body.transform.position.ToNet();
                    entry.Rotation = live.Body.transform.rotation.ToNet();
                    replay.Items.Add(entry);
                }

                if (replay.Items.Count == 0)
                {
                    if (dead == null) dead = new List<long>();
                    dead.Add(pair.Key);
                    continue;
                }

                yield return replay;
            }

            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                    _hostSpawnManifests.Remove(dead[i]);
            }
        }

        /// <summary>
        /// Materialize a manifest we did not spill ourselves. Per entry: adopt an
        /// untracked same-named clone resting near the manifest pose, steal a stale
        /// scanner-registered clone (rejoin/replay case), else instantiate from a
        /// same-named or same-base-named template anywhere in the scene.
        /// </summary>
        private void MaterializeGuestSpawn(PendingSpawn pending)
        {
            var descriptors = pending.Descriptors;
            if (descriptors == null) return;

            // Stealing scanner-registered clones needs a trustworthy "host never named
            // this id" test. That holds on the join replay (the item snapshot arrived
            // one message earlier) and for our own timed-out offer (those ordinal ids
            // provably came from our spill); for other live manifests the seen-id set
            // may be stale, so template instantiation covers them instead.
            var session = SessionManager.Instance;
            bool allowSteal = pending.IsReplay
                || (session != null && pending.HostOwnerId == session.LocalPlayerId);

            float now = Time.unscaledTime;
            int done = 0;
            for (int i = 0; i < descriptors.Count; i++)
            {
                if (MaterializeSpawnEntry(descriptors[i], pending.HostOwnerId, now, allowSteal)) done++;
            }

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: spawn {pending.ContainerId:X8} #{pending.Epoch} — guest materialized {done}/{descriptors.Count} item(s).");
            Util.BootTrace.Crumb($"SPAWN-MATERIALIZE guest {pending.ContainerId:X8} #{pending.Epoch} done={done}/{descriptors.Count}");
            if (_bridge.SelfTest && done > 0)
                SessionManager.Instance?.SendChat($"[ws] materialized {done} item(s) (guest)");
        }

        private bool MaterializeSpawnEntry(ItemSpawn.Entry entry, byte ownerId, float now, bool allowSteal)
        {
            bool exists = _items.TryGetValue(entry.NetId, out var existing);
            if (!_spawnLifecycle.ShouldMaterialize(entry.NetId, exists && existing!.Body != null)) return false;
            if (exists) RemoveTrackedItem(entry.NetId, existing!.Body);

            try
            {
                Vector3 pos = entry.Position.ToUnity();
                if (!IsFinite(pos) || !TryNormalize(entry.Rotation.ToUnity(), out var rotation)) return false;
                entry.Rotation = rotation.ToNet();

                var adopted = FindSpawnBodyByName(entry.TemplateName, pos, SpawnAdoptRadius, untrackedOnly: true);
                if (adopted != null)
                {
                    _trackedBodies[adopted] = true;
                    BindSpawnedBody(adopted, entry, ownerId, now);
                    return true;
                }

                if (allowSteal && TryStealStaleClone(entry, pos, ownerId, now)) return true;

                var template = FindSpawnBodyByName(entry.TemplateName, pos, -1f, untrackedOnly: false);
                if (template == null)
                {
                    WinterMPPlugin.Log.LogWarning(
                        $"WorldSync: no template '{entry.TemplateName}' for spawned item {entry.NetId:X8}; skipped.");
                    Util.BootTrace.Crumb($"SPAWN-NOTEMPLATE '{entry.TemplateName}'");
                    return false;
                }

                var cloneGo = (GameObject)UnityEngine.Object.Instantiate(
                    template.gameObject, pos, entry.Rotation.ToUnity());
                // The manifest name, not the template's: the source may be a master
                // ("shopping bagx") or prefab whose name the game rewrites on spawn —
                // downstream adopt/steal/scan must see the canonical instance name.
                cloneGo.name = entry.TemplateName;
                if (!cloneGo.activeSelf) cloneGo.SetActive(true);

                var body = cloneGo.GetComponent<Rigidbody>();
                if (body == null)
                {
                    UnityEngine.Object.Destroy(cloneGo);
                    return false;
                }

                // The template may be one WE froze kinematic (a tracked host-owned item —
                // often the sibling clone bound a moment ago), and Instantiate copies that
                // frozen state. Recorded as-is it would become the clone's "original" and
                // the host's final release packet would leave it kinematic forever. Restore
                // the template's true original before binding records it.
                bool originalKinematic = template.isKinematic;
                foreach (var pair in _items)
                {
                    if (!ReferenceEquals(pair.Value.Body, template)) continue;
                    if (pair.Value.KinematicSaved) originalKinematic = pair.Value.OriginalKinematic;
                    break;
                }
                body.isKinematic = originalKinematic;

                _trackedBodies[body] = true;
                BindSpawnedBody(body, entry, ownerId, now);
                return true;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: materialize '{entry.TemplateName}' ({entry.NetId:X8}) failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Rejoin case: our scanner already registered last session's clone under a
        /// per-peer ordinal id before the manifest replay arrived. Steal it — remove the
        /// scanner registration and rebind under the manifest id. Only ids the host has
        /// provably never named in a snapshot are eligible: a same-name SAVE item nearby
        /// is host-known and stealing it would desync its shared id.
        /// </summary>
        private bool TryStealStaleClone(ItemSpawn.Entry entry, Vector3 near, byte ownerId, float now)
        {
            if (_snapshotSeenIds.Count == 0) return false;

            float radiusSqr = SpawnAdoptRadius * SpawnAdoptRadius;
            uint staleId = 0;
            SyncedItem? stale = null;

            foreach (var pair in _items)
            {
                var item = pair.Value;
                if (item.IsVehicle || item.Body == null) continue;
                if (FindPackageUse(item.Body) != null || NativePartIdentity.FindData(item.Body.transform) != null) continue;
                if (_snapshotSeenIds.Contains(pair.Key)) continue;
                if (item.Body.gameObject.name != entry.TemplateName) continue;
                if ((item.Body.transform.position - near).sqrMagnitude > radiusSqr) continue;

                staleId = pair.Key;
                stale = item;
                break;
            }

            if (stale == null || stale.Body == null) return false;

            _items.Remove(staleId);
            // If the old registration ever pinned this body, un-pin before the rebind
            // records "original" kinematic state — same trap as the instantiate path.
            if (stale.KinematicSaved) stale.Body.isKinematic = stale.OriginalKinematic;
            Util.BootTrace.Crumb($"SPAWN-STEAL guest {staleId:X8} -> {entry.NetId:X8} '{entry.TemplateName}'");
            BindSpawnedBody(stale.Body, entry, ownerId, now);
            return true;
        }

        /// <summary>
        /// Find a pickable rigidbody for <paramref name="name"/>. With a radius it must
        /// match the name exactly and rest within it of <paramref name="near"/>
        /// (adoption); radius &lt; 0 searches the whole scene for a clone source,
        /// preferring an exact-named live instance but falling back to anything whose
        /// base name matches — the game names store masters "&lt;base&gt;x" and live
        /// instances "&lt;base&gt;(itemx)" ("shopping bagx" spawns "shopping
        /// bag(itemx)"), so a peer that never spilled has NO exact-named object at all.
        /// </summary>
        private Rigidbody? FindSpawnBodyByName(string name, Vector3 near, float radius, bool untrackedOnly)
        {
            if (string.IsNullOrEmpty(name)) return null;

            string wantedBase = NormalizeSpawnName(name);
            float radiusSqr = radius * radius;
            Rigidbody? fallback = null;   // exact name, but inactive or untracked
            Rigidbody? baseMatch = null;  // master/prefab whose base name matches

            var bodies = Resources.FindObjectsOfTypeAll(typeof(Rigidbody));
            foreach (var obj in bodies)
            {
                var body = obj as Rigidbody;
                if (body == null) continue;

                try
                {
                    string candidate = body.gameObject.name;
                    bool exact = candidate == name;
                    if (!exact)
                    {
                        if (radius >= 0f) continue;
                        string candidateBase = NormalizeSpawnName(candidate);
                        if (!candidateBase.Equals(wantedBase, StringComparison.OrdinalIgnoreCase)
                            && !candidateBase.Equals(wantedBase + "x", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    bool tracked = _trackedBodies.ContainsKey(body);
                    if (FindPackageUse(body) != null || NativePartIdentity.FindData(body.transform) != null) continue;
                    if (untrackedOnly && tracked) continue;
                    if (!SyncCatalog.IsPickableRigidbody(body)) continue;

                    if (radius >= 0f)
                    {
                        if (!body.gameObject.activeInHierarchy) continue;
                        if ((body.transform.position - near).sqrMagnitude > radiusSqr) continue;
                        return body;
                    }

                    if (exact)
                    {
                        if (body.gameObject.activeInHierarchy && tracked) return body;
                        if (fallback == null || body.gameObject.activeInHierarchy) fallback = body;
                    }
                    else if (baseMatch == null || body.gameObject.activeInHierarchy)
                    {
                        baseMatch = body;
                    }
                }
                catch
                {
                    // Mid-teardown or asset-only objects can throw on access; skip them.
                }
            }

            return fallback ?? baseMatch;
        }

        private string[]? _spawnNameSuffixes;

        /// <summary>
        /// Base name of a spawn clone: the catalog's pooled-instance suffixes and
        /// Unity's "(Clone)" stripped, repeatedly — "sausages(itemx)(Clone)" and
        /// "sausages(itemx)" both normalize to "sausages".
        /// </summary>
        private string NormalizeSpawnName(string name)
        {
            if (_spawnNameSuffixes == null)
            {
                var suffixes = new List<string>(SyncCatalog.PickableNameSuffixes) { "(Clone)" };
                _spawnNameSuffixes = suffixes.ToArray();
            }

            string result = name.Trim();
            bool stripped = true;
            while (stripped)
            {
                stripped = false;
                foreach (string suffix in _spawnNameSuffixes)
                {
                    if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        result = result.Substring(0, result.Length - suffix.Length).Trim();
                        stripped = true;
                    }
                }
            }

            return result;
        }

        private void BindSpawnedBody(Rigidbody body, ItemSpawn.Entry entry, byte ownerId, float now)
        {
            if (_items.ContainsKey(entry.NetId)) return;

            Vector3 pos = entry.Position.ToUnity();
            Quaternion rot = entry.Rotation.ToUnity();

            var item = new SyncedItem
            {
                Body = body,
                Path = ScenePath.Of(body.transform),
                Id = entry.NetId,
                IsVehicle = false,
                LastPosition = pos,
            };

            // Owner-followed from birth: freeze and snap to the manifest pose. The
            // owner's subsequent ItemTransform stream keeps it in sync, and a final
            // packet releases it to local physics at rest.
            item.OriginalKinematic = body.isKinematic;
            item.KinematicSaved = true;
            body.isKinematic = true;
            body.transform.position = pos;
            body.transform.rotation = rot;

            item.RemoteOwner = ownerId;
            item.RemoteIsDriver = false;
            item.RemoteVehicleStream = false;
            item.TargetPosition = pos;
            item.TargetRotation = rot;
            item.LastRemoteAt = now;
            item.LastRemoteSequence = 0;
            item.LastRemoteSequenceOwner = ownerId;

            _items[entry.NetId] = item;
            TryRegisterConsumableHooks(item);
        }
    }
}
