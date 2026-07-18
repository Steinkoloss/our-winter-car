using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Host-authoritative transform streaming for NPC/traffic rigidbodies (TRAFFIC/,
    /// NPC_CARS/, HUMANS/). Guests pin bodies kinematic and ease toward host poses.
    /// </summary>
    internal sealed class NpcTrafficSync
    {
        private const float MoveThresholdSqr = 0.01f;
        private const float RemoteLerpSpeed = 10f;
        private const float RemoteSnapDistance = 20f;

        private readonly WorldSyncBridge _bridge;
        private readonly Dictionary<uint, SyncedNpc> _npcs = new Dictionary<uint, SyncedNpc>();
        private readonly Dictionary<Rigidbody, bool> _trackedBodies = new Dictionary<Rigidbody, bool>();
        // Reused across UpdateHost/UpdateGuest (host XOR guest per tick) to avoid a per-frame List alloc.
        private readonly List<uint> _deadIds = new List<uint>();

        // Transform-driven AI movers that have no streamable root rigidbody (the moose
        // is animated/walked by a PlayMaker FSM with per-peer RNG, so each peer's moose
        // wanders to a different spot). We stream the host's root transform over the
        // existing NpcTransform message and freeze the guest's AI FSM so it stops rolling
        // its own route. Kept separate from the rigidbody _npcs flow so traffic is untouched.
        private static readonly ScriptedMoverDef[] MoverDefs =
        {
            new ScriptedMoverDef("AnimalsMoose/Moose", "Move"),
        };
        private readonly Dictionary<uint, ScriptedMover> _movers = new Dictionary<uint, ScriptedMover>();

        public NpcTrafficSync(WorldSyncBridge bridge) => _bridge = bridge;

        public int NpcCount => _npcs.Count;

        public void Clear()
        {
            ReleaseAllKinematic();
            RestoreAllMovers();
            _npcs.Clear();
            _trackedBodies.Clear();
            _movers.Clear();
        }

        public void ReleaseSession()
        {
            ReleaseAllKinematic();
            RestoreAllMovers();
        }

        internal int Scan()
        {
            // Movers scan unconditionally: they hang off GameObject.Find, which fails
            // until the world streams the object in — tying them to rigidbody newcomers
            // could miss a moose that appears after traffic has all been tracked.
            int moverAdded = ScanMovers();

            var newcomers = new List<SyncedNpc>();
            var bodies = Resources.FindObjectsOfTypeAll(typeof(Rigidbody));
            foreach (var obj in bodies)
            {
                var body = obj as Rigidbody;
                if (body == null || _trackedBodies.ContainsKey(body)) continue;

                try
                {
                    if (!body.gameObject.activeInHierarchy) continue;

                    string path = ScenePath.Of(body.transform);
                    if (!IsNpcTrafficPath(path)) continue;
                    if (SyncCatalog.IsVehicleRoot(body) || SyncCatalog.IsPickableRigidbody(body)) continue;

                    newcomers.Add(new SyncedNpc
                    {
                        Body = body,
                        Path = path,
                        LastPosition = body.transform.position,
                    });
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogDebug($"WorldSync: skipped NPC rigidbody during scan: {e.Message}");
                }
            }

            if (newcomers.Count == 0) return moverAdded;

            var byPath = new Dictionary<string, List<SyncedNpc>>();
            foreach (var npc in newcomers)
            {
                if (!byPath.TryGetValue(npc.Path, out var group))
                    byPath[npc.Path] = group = new List<SyncedNpc>();
                group.Add(npc);
            }

            int added = 0;
            foreach (var pair in byPath)
            {
                var group = pair.Value;
                if (group.Count > 1)
                    group.Sort(CompareByInitialPosition);

                for (int i = 0; i < group.Count; i++)
                {
                    var npc = group[i];
                    string idSource = group.Count > 1 ? "npc:" + npc.Path + "#" + i : "npc:" + npc.Path;
                    npc.NetId = StableHash.Fnv1a32(idSource);
                    _trackedBodies[npc.Body] = true;

                    if (_npcs.ContainsKey(npc.NetId))
                    {
                        WinterMPPlugin.Log.LogWarning($"WorldSync: NPC id collision, not syncing '{npc.Path}'.");
                        continue;
                    }

                    _npcs[npc.NetId] = npc;
                    added++;
                }
            }

            return added + moverAdded;
        }

        private int ScanMovers()
        {
            int added = 0;
            foreach (var def in MoverDefs)
            {
                uint netId = StableHash.Fnv1a32("mover:" + def.Path);
                if (_movers.ContainsKey(netId)) continue;

                try
                {
                    var go = GameObject.Find(def.Path);
                    if (go == null || !go.activeInHierarchy) continue;

                    PlayMakerFSM? ai = null;
                    foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                    {
                        if (fsm != null && fsm.FsmName == def.AiFsmName) { ai = fsm; break; }
                    }

                    _movers[netId] = new ScriptedMover
                    {
                        Transform = go.transform,
                        AiFsm = ai,
                        Path = def.Path,
                        NetId = netId,
                        LastPosition = go.transform.position,
                    };
                    added++;
                    WinterMPPlugin.Log.LogInfo($"WorldSync: scripted mover registered: '{def.Path}' (ai={(ai != null)}).");
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogDebug($"WorldSync: skipped mover '{def.Path}': {e.Message}");
                }
            }

            return added;
        }

        internal void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            _bridge.FindLocalPlayer();
            float now = Time.unscaledTime;

            if (session.IsHost)
            {
                UpdateHost(session, now);
                UpdateHostMovers(session, now);
            }
            else
            {
                UpdateGuest(now);
                UpdateGuestMovers(now);
            }
        }

        public void OnRemoteNpcTransform(NpcTransform message)
        {
            if (_movers.TryGetValue(message.NetId, out var mover))
            {
                ApplyMoverRemote(mover, message);
                return;
            }

            if (!_npcs.TryGetValue(message.NetId, out var npc) || npc.Body == null) return;

            if (ItemTransformPolicy.IsStaleSequence(npc.LastRemoteSequence, message.Sequence))
            {
                if (!message.IsFinal)
                    ConnectionQuality.Instance.NoteUnreliableDropped();
                return;
            }

            if (!message.IsFinal)
                ConnectionQuality.Instance.NoteUnreliableReceived();

            npc.LastRemoteSequence = message.Sequence;
            var body = npc.Body;

            if (!npc.KinematicSaved)
            {
                npc.OriginalKinematic = body.isKinematic;
                npc.KinematicSaved = true;
            }

            var position = message.Position.ToUnity();
            var rotation = message.Rotation.ToUnity();

            if (message.IsFinal)
            {
                body.isKinematic = npc.OriginalKinematic;
                body.transform.position = position;
                body.transform.rotation = rotation;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.Sleep();
                }

                npc.GuestRemoteActive = false;
                npc.LastRemoteAt = -999f;
                npc.LastPosition = position;
            }
            else
            {
                body.isKinematic = true;
                if (!npc.GuestRemoteActive)
                {
                    body.transform.position = position;
                    body.transform.rotation = rotation;
                }

                npc.TargetPosition = position;
                npc.TargetRotation = rotation;
                npc.LastRemoteAt = Time.unscaledTime;
                npc.GuestRemoteActive = true;
            }
        }

        private void UpdateHost(SessionManager session, float now)
        {
            _deadIds.Clear();
            foreach (var pair in _npcs)
            {
                var npc = pair.Value;
                var body = npc.Body;
                if (body == null)
                {
                    _deadIds.Add(pair.Key);
                    continue;
                }

                TrackMotion(npc, body, now);
                bool moving = IsMoving(npc, body, now);
                float distance = GetMinPlayerDistance(session, body.transform.position);
                float rateHz = NpcTransformPolicy.GetEffectiveSendRateHz(distance, moving);

                if (!NpcTransformPolicy.ShouldStream(distance, moving))
                {
                    if (npc.HostStreaming && !moving)
                        SendTransform(session, npc, body, final: true);
                    else if (npc.HostStreaming && body.IsSleeping())
                        SendTransform(session, npc, body, final: true);
                    continue;
                }

                if (!moving && body.IsSleeping())
                {
                    if (npc.HostStreaming)
                        SendTransform(session, npc, body, final: true);
                    continue;
                }

                if (now < npc.NextSendAt) continue;

                SendTransform(session, npc, body, final: false);
                npc.NextSendAt = now + 1f / rateHz;
            }

            PruneDead();
        }

        private void UpdateGuest(float now)
        {
            _deadIds.Clear();
            foreach (var pair in _npcs)
            {
                var npc = pair.Value;
                var body = npc.Body;
                if (body == null)
                {
                    _deadIds.Add(pair.Key);
                    continue;
                }

                if (!npc.GuestRemoteActive)
                    continue;

                if (!NpcTransformPolicy.IsRemoteStreamLive(npc.LastRemoteAt, now))
                {
                    if (npc.KinematicSaved)
                        body.isKinematic = npc.OriginalKinematic;
                    npc.GuestRemoteActive = false;
                    continue;
                }

                ApplyRemoteSmoothing(npc, body);
            }

            PruneDead();
        }

        private void PruneDead()
        {
            for (int i = 0; i < _deadIds.Count; i++)
            {
                // Drop the destroyed body's tracking key too, or _trackedBodies grows unbounded
                // with fake-null keys over a long session of traffic spawning/despawning.
                if (_npcs.TryGetValue(_deadIds[i], out var dead))
                    _trackedBodies.Remove(dead.Body);
                _npcs.Remove(_deadIds[i]);
            }
        }

        private void SendTransform(SessionManager session, SyncedNpc npc, Rigidbody body, bool final)
        {
            byte flags = final ? NpcTransform.FlagFinal : (byte)0;
            var message = new NpcTransform
            {
                NetId = npc.NetId,
                Sequence = ++npc.OutSequence,
                Flags = flags,
                Position = body.transform.position.ToNet(),
                Rotation = body.transform.rotation.ToNet(),
            };

            session.SendWorldMessage(message, NpcTransformPolicy.SelectSendChannel(final));
            npc.HostStreaming = !final;
        }

        private static void ApplyRemoteSmoothing(SyncedNpc npc, Rigidbody body)
        {
            var transform = body.transform;
            if ((transform.position - npc.TargetPosition).sqrMagnitude > RemoteSnapDistance * RemoteSnapDistance)
            {
                transform.position = npc.TargetPosition;
                transform.rotation = npc.TargetRotation;
            }
            else
            {
                float t = 1f - Mathf.Exp(-RemoteLerpSpeed * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, npc.TargetPosition, t);
                transform.rotation = Quaternion.Slerp(transform.rotation, npc.TargetRotation, t);
            }

            npc.LastPosition = transform.position;
        }

        private static void TrackMotion(SyncedNpc npc, Rigidbody body, float now)
        {
            var position = body.transform.position;
            if ((position - npc.LastPosition).sqrMagnitude > MoveThresholdSqr)
            {
                npc.LastMovedAt = now;
                npc.LastPosition = position;
            }
        }

        private static bool IsMoving(SyncedNpc npc, Rigidbody body, float now)
        {
            if (now - npc.LastMovedAt < NpcTransformPolicy.StillSeconds)
                return true;
            return !body.IsSleeping() && body.velocity.sqrMagnitude > MoveThresholdSqr;
        }

        private float GetMinPlayerDistance(SessionManager session, Vector3 position)
        {
            float minSq = float.MaxValue;

            if (_bridge.LocalPlayer != null)
            {
                float sq = (position - _bridge.LocalPlayer.position).sqrMagnitude;
                if (sq < minSq) minSq = sq;
            }

            foreach (var player in session.Players)
            {
                if (player.IsDead) continue;
                float sq = (position - player.Position).sqrMagnitude;
                if (sq < minSq) minSq = sq;
            }

            return minSq == float.MaxValue ? 0f : Mathf.Sqrt(minSq);
        }

        private void ReleaseAllKinematic()
        {
            foreach (var npc in _npcs.Values)
            {
                if (npc.Body == null || !npc.KinematicSaved) continue;
                npc.Body.isKinematic = npc.OriginalKinematic;
                npc.KinematicSaved = false;
                npc.GuestRemoteActive = false;
                npc.HostStreaming = false;
            }
        }

        // ---------------------------------------------------------------- scripted movers

        private void UpdateHostMovers(SessionManager session, float now)
        {
            foreach (var mover in _movers.Values)
            {
                var t = mover.Transform;
                if (t == null) continue;

                try
                {
                    Vector3 pos = t.position;
                    if ((pos - mover.LastPosition).sqrMagnitude > MoveThresholdSqr)
                    {
                        mover.LastMovedAt = now;
                        mover.LastPosition = pos;
                    }

                    bool moving = now - mover.LastMovedAt < NpcTransformPolicy.StillSeconds;
                    float distance = GetMinPlayerDistance(session, pos);

                    if (!NpcTransformPolicy.ShouldStream(distance, moving))
                    {
                        if (mover.HostStreaming && !moving)
                            SendMoverTransform(session, mover, final: true);
                        continue;
                    }

                    if (now < mover.NextSendAt) continue;

                    SendMoverTransform(session, mover, final: false);
                    mover.NextSendAt = now + 1f / NpcTransformPolicy.GetEffectiveSendRateHz(distance, moving);
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogDebug($"WorldSync: mover host update '{mover.Path}': {e.Message}");
                }
            }
        }

        private void UpdateGuestMovers(float now)
        {
            foreach (var mover in _movers.Values)
            {
                if (mover.Transform == null || !mover.GuestRemoteActive) continue;

                try
                {
                    if (!NpcTransformPolicy.IsRemoteStreamLive(mover.LastRemoteAt, now))
                    {
                        RestoreMoverAi(mover);
                        mover.GuestRemoteActive = false;
                        continue;
                    }

                    var t = mover.Transform;
                    if ((t.position - mover.TargetPosition).sqrMagnitude > RemoteSnapDistance * RemoteSnapDistance)
                    {
                        t.position = mover.TargetPosition;
                        t.rotation = mover.TargetRotation;
                    }
                    else
                    {
                        float k = 1f - Mathf.Exp(-RemoteLerpSpeed * Time.deltaTime);
                        t.position = Vector3.Lerp(t.position, mover.TargetPosition, k);
                        t.rotation = Quaternion.Slerp(t.rotation, mover.TargetRotation, k);
                    }

                    mover.LastPosition = t.position;
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogDebug($"WorldSync: mover guest update '{mover.Path}': {e.Message}");
                }
            }
        }

        private void SendMoverTransform(SessionManager session, ScriptedMover mover, bool final)
        {
            var message = new NpcTransform
            {
                NetId = mover.NetId,
                Sequence = ++mover.OutSequence,
                Flags = final ? NpcTransform.FlagFinal : (byte)0,
                Position = mover.Transform.position.ToNet(),
                Rotation = mover.Transform.rotation.ToNet(),
            };

            session.SendWorldMessage(message, NpcTransformPolicy.SelectSendChannel(final));
            mover.HostStreaming = !final;
        }

        private void ApplyMoverRemote(ScriptedMover mover, NpcTransform message)
        {
            if (mover.Transform == null) return;

            if (ItemTransformPolicy.IsStaleSequence(mover.LastRemoteSequence, message.Sequence))
            {
                if (!message.IsFinal)
                    ConnectionQuality.Instance.NoteUnreliableDropped();
                return;
            }

            if (!message.IsFinal)
                ConnectionQuality.Instance.NoteUnreliableReceived();

            mover.LastRemoteSequence = message.Sequence;
            Vector3 position = message.Position.ToUnity();
            Quaternion rotation = message.Rotation.ToUnity();

            if (message.IsFinal)
            {
                // Host's moose came to rest — hand control back to the local AI from here.
                mover.Transform.position = position;
                mover.Transform.rotation = rotation;
                RestoreMoverAi(mover);
                mover.GuestRemoteActive = false;
                mover.LastRemoteAt = -999f;
                mover.LastPosition = position;
                return;
            }

            // Freeze the local route AI so it stops fighting the host stream with its own RNG.
            FreezeMoverAi(mover);
            if (!mover.GuestRemoteActive)
            {
                mover.Transform.position = position;
                mover.Transform.rotation = rotation;
            }

            mover.TargetPosition = position;
            mover.TargetRotation = rotation;
            mover.LastRemoteAt = Time.unscaledTime;
            mover.GuestRemoteActive = true;
        }

        private static void FreezeMoverAi(ScriptedMover mover)
        {
            if (mover.AiFrozen || mover.AiFsm == null) return;
            try { mover.AiFsm.enabled = false; mover.AiFrozen = true; }
            catch { }
        }

        private static void RestoreMoverAi(ScriptedMover mover)
        {
            if (!mover.AiFrozen || mover.AiFsm == null) { mover.AiFrozen = false; return; }
            try { mover.AiFsm.enabled = true; }
            catch { }
            mover.AiFrozen = false;
        }

        private void RestoreAllMovers()
        {
            foreach (var mover in _movers.Values)
            {
                RestoreMoverAi(mover);
                mover.GuestRemoteActive = false;
                mover.HostStreaming = false;
            }
        }

        private readonly struct ScriptedMoverDef
        {
            public readonly string Path;
            public readonly string AiFsmName;
            public ScriptedMoverDef(string path, string aiFsmName) { Path = path; AiFsmName = aiFsmName; }
        }

        private sealed class ScriptedMover
        {
            public Transform Transform = null!;
            public PlayMakerFSM? AiFsm;
            public string Path = string.Empty;
            public uint NetId;
            public ushort OutSequence;
            public ushort LastRemoteSequence;
            public bool AiFrozen;
            public bool GuestRemoteActive;
            public bool HostStreaming;
            public float LastMovedAt;
            public float NextSendAt;
            public float LastRemoteAt = -999f;
            public Vector3 LastPosition;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation = Quaternion.identity;
        }

        private static bool IsNpcTrafficPath(string path)
        {
            if (path.Length == 0 || path.StartsWith("PLAYER", StringComparison.Ordinal)) return false;
            return path.StartsWith("TRAFFIC/", StringComparison.Ordinal)
                || path.StartsWith("NPC_CARS/", StringComparison.Ordinal)
                || path.StartsWith("HUMANS/", StringComparison.Ordinal);
        }

        private static int CompareByInitialPosition(SyncedNpc a, SyncedNpc b)
        {
            long ax = Quantize(a.LastPosition.x), bx = Quantize(b.LastPosition.x);
            if (ax != bx) return ax.CompareTo(bx);
            long ay = Quantize(a.LastPosition.y), by = Quantize(b.LastPosition.y);
            if (ay != by) return ay.CompareTo(by);
            return Quantize(a.LastPosition.z).CompareTo(Quantize(b.LastPosition.z));
        }

        private static long Quantize(float value) => (long)Math.Round(value * 1000f);
    }
}
