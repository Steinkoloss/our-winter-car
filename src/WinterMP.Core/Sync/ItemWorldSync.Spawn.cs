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
        // Bag inventories are host-owned. Exact native factory outputs are captured
        // in BagSpill; this partial publishes their manifests and creates peer views.
        private const float SpawnCaptureHardSeconds = 2f;

        /// <summary>Finalize a capture once the clone count holds steady this long.</summary>
        private const float SpawnCaptureStableSeconds = 0.3f;


        private const float SpawnMaterializeRetrySeconds = 0.5f;
        private const float SpawnMaterializeLogSeconds = 30f;


        private sealed class PendingSpawn
        {
            public uint ContainerId;
            public ushort Epoch;
            public string StateName = string.Empty;
            public bool IsHost;
            public byte HostOwnerId = WorldSyncIds.NoOwner;
            public List<ItemSpawn.Entry>? Descriptors;
            public bool IsReplay;           // join-snapshot replay — stale-clone rules apply

            public Vector3 Near;
            public bool HasNear;
            public float HardDeadline;
            public float NextMaterializeAt;
            public float NextMaterializeLogAt;
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
        // One counter also serves additional packets from bags larger than 32 items.
        private readonly Dictionary<uint, ushort> _spawnEpochs = new Dictionary<uint, ushort>();


        private static long SpawnKey(uint containerId, ushort epoch) => ((long)containerId << 16) | epoch;

        private ushort MintSpawnEpoch(uint containerId)
        {
            _spawnEpochs.TryGetValue(containerId, out ushort last);
            ushort next = (ushort)(last + 1);
            _spawnEpochs[containerId] = next;
            return next;
        }

        /// <summary>Guest: queue host-created contents for materialization.</summary>
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

            // Repeated resyncs refresh one pending job without postponing it forever.
            foreach (var pending in _pendingSpawns)
            {
                if (pending.ContainerId != message.ContainerNetId || pending.Epoch != message.Epoch) continue;
                pending.Descriptors = new List<ItemSpawn.Entry>(message.Items);
                pending.IsReplay |= message.IsReplay;
                pending.HostOwnerId = message.OwnerPlayerId;
                return;
            }

            // The host's spill has no local outputs: nothing to
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

            // Other peers' spills: their clones live on those machines, so there is
            // nothing to gather here — materialize from the manifest when due.
            for (int i = _pendingSpawns.Count - 1; i >= 0; i--)
            {
                var pending = _pendingSpawns[i];

                if (now >= pending.HardDeadline && now >= pending.NextMaterializeAt)
                {
                    if (MaterializeGuestSpawn(pending)) _pendingSpawns.RemoveAt(i);
                    else pending.NextMaterializeAt = now + SpawnMaterializeRetrySeconds;
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
                if (_items.TryGetValue(netId, out var existing))
                {
                    if (existing.Body != body) throw new InvalidOperationException("Spawn item ID collision.");
                }
                else
                {
                    var item = new SyncedItem { Body = body, Path = ScenePath.Of(body.transform), Id = netId,
                        IsVehicle = false, LocallyOwned = true, LastPosition = body.transform.position, LastMovedAt = now };
                    _items[netId] = item;
                    TryRegisterConsumableHooks(item);
                }

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
        private bool MaterializeGuestSpawn(PendingSpawn pending)
        {
            var descriptors = pending.Descriptors;
            if (descriptors == null) return true;

            // Stealing scanner-registered clones needs a trustworthy "host never named
            // this id" test. That holds on the join replay (the item snapshot arrived
            // one message earlier); for live manifests the seen-id set may be stale.
            bool allowSteal = pending.IsReplay;

            float now = Time.unscaledTime;
            bool report = now >= pending.NextMaterializeLogAt;
            bool logEntryFailure = report;
            int requested = descriptors.Count;
            int done = _spawnLifecycle.MaterializePending(descriptors,
                id => _items.TryGetValue(id, out var item) && item.Body != null,
                entry =>
                {
                    bool created = MaterializeSpawnEntry(entry, pending.HostOwnerId, now, allowSteal, logEntryFailure);
                    if (!created) logEntryFailure = false;
                    return created;
                });

            if (done > 0 || descriptors.Count == 0 || report)
            {
                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: spawn {pending.ContainerId:X8} #{pending.Epoch} — guest materialized {done}/{requested} item(s), {descriptors.Count} awaiting creation.");
                Util.BootTrace.Crumb($"SPAWN-MATERIALIZE guest {pending.ContainerId:X8} #{pending.Epoch} done={done}/{requested} pending={descriptors.Count}");
                pending.NextMaterializeLogAt = now + SpawnMaterializeLogSeconds;
            }
            if (_bridge.SelfTest && done > 0)
                SessionManager.Instance?.SendChat($"[ws] materialized {done} item(s) (guest)");
            return descriptors.Count == 0;
        }

        private bool MaterializeSpawnEntry(ItemSpawn.Entry entry, byte ownerId, float now, bool allowSteal, bool logFailures = true)
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

                var bagTemplate = FindBagSpillTemplate(entry.TemplateName);
                var template = bagTemplate ?? FindSpawnBodyByName(entry.TemplateName, pos, -1f, untrackedOnly: false);
                if (template == null)
                {
                    if (logFailures)
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"WorldSync: no template '{entry.TemplateName}' for spawned item {entry.NetId:X8}.");
                        Util.BootTrace.Crumb($"SPAWN-NOTEMPLATE '{entry.TemplateName}'");
                    }
                    return false;
                }

                var cloneGo = (GameObject)UnityEngine.Object.Instantiate(
                    template.gameObject, pos, entry.Rotation.ToUnity());
                // Native Init reads this name as its save key before restoring the
                // display name. An ephemeral key cannot load an unrelated guest item.
                cloneGo.name = bagTemplate != null ? "wintermp-spawn-" + entry.NetId.ToString("X8") : entry.TemplateName;
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
                bool originalKinematic = bagTemplate == null && template.isKinematic;
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
                if (logFailures)
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
