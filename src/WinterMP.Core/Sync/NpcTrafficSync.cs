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

        public NpcTrafficSync(WorldSyncBridge bridge) => _bridge = bridge;

        public int NpcCount => _npcs.Count;

        public void Clear()
        {
            ReleaseAllKinematic();
            _npcs.Clear();
            _trackedBodies.Clear();
        }

        public void ReleaseSession() => ReleaseAllKinematic();

        internal int Scan()
        {
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

            if (newcomers.Count == 0) return 0;

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

            return added;
        }

        internal void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            _bridge.FindLocalPlayer();
            float now = Time.unscaledTime;

            if (session.IsHost)
                UpdateHost(session, now);
            else
                UpdateGuest(now);
        }

        public void OnRemoteNpcTransform(NpcTransform message)
        {
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
            var deadIds = new List<uint>();
            foreach (var pair in _npcs)
            {
                var npc = pair.Value;
                var body = npc.Body;
                if (body == null)
                {
                    deadIds.Add(pair.Key);
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

            for (int i = 0; i < deadIds.Count; i++)
                _npcs.Remove(deadIds[i]);
        }

        private void UpdateGuest(float now)
        {
            foreach (var pair in _npcs)
            {
                var npc = pair.Value;
                var body = npc.Body;
                if (body == null) continue;

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
