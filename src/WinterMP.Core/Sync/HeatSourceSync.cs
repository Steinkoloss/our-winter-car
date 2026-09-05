using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Home heating &amp; cooking as host-owned shared world state (PLAN.md §4.8):
    /// cabin woodstove, sauna kiuas, cottage/living-room fireplaces. Lit / fuel / heat
    /// output and sauna temperature are host-authoritative and broadcast; guests write
    /// the received values back onto their local FSMs so each client's own
    /// position-derived body-temp calc warms consistently ("thaw at the same sauna").
    /// Lighting, feeding wood, grilling and löyly are anyone-triggers intents: the guest
    /// asks, the host fires the real game FSM event on its authoritative instance, and
    /// the resulting state broadcast carries the progression back to everyone.
    ///
    /// All FSM/var lookups are best-effort and crash-contained — a save that lacks a
    /// given source (or a game patch that renames a var) simply skips it.
    /// </summary>
    internal sealed class HeatSourceSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 1.5f;
        private const float KeepAliveSeconds = 20f;
        private const float SaunaTempScale = 100f;
        private const float IntentCooldownSeconds = 0.75f;
        private const float IntentPlayerPoseMaxAgeSeconds = 2f;
        private const float IntentPlayerMaxDistance = 8f;

        private enum Kind { Woodstove, Sauna, Fireplace }

        private sealed class Source
        {
            public uint Id;
            public string ContainerPath = string.Empty;
            public Kind Kind;
            public bool LoggedFound;
            public Transform? Anchor;

            // Located sub-FSMs (any may stay null).
            public PlayMakerFSM? SetFire;        // ".../SetFire" :: Use    — lighting
            public PlayMakerFSM? WoodTrigger;    // ".../WoodTrigger" :: Trigger — feed wood
            public PlayMakerFSM? SausageTrigger; // ".../SausageTrigger" :: Logic — grill
            public PlayMakerFSM? Simulation;     // sauna ".../Simulation" :: Time
            public PlayMakerFSM? StoveTrigger;   // sauna ".../StoveTrigger" :: Steam

            // Cached authoritative vars.
            public FsmBool? Hiillos;             // embers present (lit proxy)
            public FsmInt? Woods;                // firewood loaded
            public FsmFloat? HeatingEfficiency;
            public FsmFloat? SaunaHeat;
            public FsmFloat? StoveHeat;

            // Last broadcast (host) / applied (guest) — for change detection.
            public bool HasLast;
            public byte LastFlags;
            public byte LastFuel;
            public byte LastHeat;
            public ushort LastSaunaTemp;

            // Guest-side anyone-triggers hooks (installed once per FSM), debounced.
            public bool HookedSetFire;
            public bool HookedStove;
            public bool HookedSausage;
            public bool HookedWood;
            public float NextIntentAt;
            public ushort OutIntentSequence;

            // A source is "present" once at least one of its FSMs resolves — saves without
            // a given cottage/sauna never broadcast phantom state.
            public bool AnyFsm =>
                SetFire != null || WoodTrigger != null || SausageTrigger != null
                || Simulation != null || StoveTrigger != null;
        }

        private readonly List<Source> _sources = new List<Source>();
        private readonly Dictionary<uint, Source> _byId = new Dictionary<uint, Source>();
        // Keyed by (player, source): guests count intents per source, so a per-player-only
        // latch would reject a second stove/sauna's fresh low counter as stale.
        private readonly Dictionary<ulong, ushort> _lastIntentSequences = new Dictionary<ulong, ushort>();

        private static ulong IntentKey(byte playerId, uint sourceId) => ((ulong)playerId << 32) | sourceId;
        private bool _built;
        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;

        public void Clear()
        {
            _sources.Clear();
            _byId.Clear();
            _lastIntentSequences.Clear();
            _built = false;
            _nextProbeAt = 0f;
            _nextHostTickAt = 0f;
            _nextKeepAliveAt = 0f;
        }

        /// <summary>Host: a player (re)joined — its intent counters restarted; drop stale latches.</summary>
        public void ForgetPlayer(byte playerId)
        {
            var stale = new List<ulong>();
            foreach (var key in _lastIntentSequences.Keys)
                if ((byte)(key >> 32) == playerId) stale.Add(key);
            for (int i = 0; i < stale.Count; i++) _lastIntentSequences.Remove(stale[i]);
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            EnsureBuilt();

            if (Time.unscaledTime >= _nextProbeAt)
            {
                _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds;
                LocatePending();
            }

            if (!session.IsHost) return;
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;

            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;

            for (int i = 0; i < _sources.Count; i++)
                HostBroadcastIfChanged(session, _sources[i], keepAlive);
        }

        /// <summary>
        /// Host: make the next tick re-broadcast every located source regardless of
        /// change detection. Heat sources are not part of the chunked join snapshot,
        /// so without this a fresh guest sits cold for up to the 20 s keepalive.
        /// </summary>
        public void ForceBroadcast()
        {
            _nextHostTickAt = 0f;
            _nextKeepAliveAt = 0f;
        }

        // ---- Guest: apply host state onto the local world ---------------------

        public void OnRemoteState(HeatSourceState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            EnsureBuilt();
            if (!_byId.TryGetValue(message.SourceId, out var source)) return;

            LocateSource(source);

            try
            {
                if (source.Kind == Kind.Sauna)
                {
                    WriteFloat(source.SaunaHeat, message.SaunaTemp / SaunaTempScale);
                    WriteFloat(source.StoveHeat, message.HeatOutput);
                }
                else
                {
                    WriteBool(source.Hiillos, message.IsLit);
                    WriteInt(source.Woods, message.Fuel);
                }
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("HeatSourceSync: apply failed for " + source.ContainerPath + ": " + e.Message);
            }
        }

        // ---- Host: apply a guest's anyone-triggers intent --------------------

        public bool TryAcceptIntent(HeatSourceIntent intent)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return false;

            EnsureBuilt();
            if (!_byId.TryGetValue(intent.SourceId, out var source)) return false;

            LocateSource(source);
            if (source.Anchor == null || !TryGetAction(source, intent.Action, out var fsm, out var eventName)
                || fsm == null || !IsGuestNear(session, intent.PlayerId, source.Anchor.position))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"HeatSourceSync: dropped invalid or distant intent {intent.SourceId:X8} action {intent.Action} from player {intent.PlayerId}.");
                return false;
            }
            ulong intentKey = IntentKey(intent.PlayerId, intent.SourceId);
            if (_lastIntentSequences.TryGetValue(intentKey, out ushort previous))
            {
                ushort difference = (ushort)(intent.Sequence - previous);
                if (difference == 0 || difference > short.MaxValue)
                {
                    WinterMPPlugin.Log.LogWarning(
                        $"HeatSourceSync: dropped stale intent sequence {intent.Sequence} from player {intent.PlayerId}.");
                    return false;
                }
            }

            _lastIntentSequences[intentKey] = intent.Sequence;
            FireEvent(fsm, eventName);

            SyncEventLog.Record("heat-intent", $"{intent.SourceId:X8} action {intent.Action} player {intent.PlayerId}");
            // Force a fresh broadcast on the next host tick so the initiator sees the result promptly.
            _nextHostTickAt = 0f;
            return true;
        }

        // ---- Host broadcast ---------------------------------------------------

        private void HostBroadcastIfChanged(SessionManager session, Source source, bool keepAlive)
        {
            if (!source.AnyFsm) return;

            byte flags = 0, fuel = 0, heat = 0;
            ushort saunaTemp = 0;

            try
            {
                if (source.Kind == Kind.Sauna)
                {
                    float stove = ReadFloat(source.StoveHeat);
                    float sauna = ReadFloat(source.SaunaHeat);
                    if (stove > 1f) flags |= HeatSourceState.FlagLit;
                    saunaTemp = ClampUShort(sauna * SaunaTempScale);
                    heat = ClampByte(stove);
                    fuel = ClampByte(ReadInt(source.Woods));
                }
                else
                {
                    bool lit = ReadBool(source.Hiillos);
                    if (lit) flags |= HeatSourceState.FlagLit;
                    fuel = ClampByte(ReadInt(source.Woods));
                    heat = lit ? HeatByte(source.HeatingEfficiency) : (byte)0;
                }
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("HeatSourceSync: read failed for " + source.ContainerPath + ": " + e.Message);
                return;
            }

            bool changed = !source.HasLast
                || source.LastFlags != flags
                || source.LastFuel != fuel
                || source.LastHeat != heat
                || source.LastSaunaTemp != saunaTemp;

            if (!changed && !keepAlive) return;

            source.HasLast = true;
            source.LastFlags = flags;
            source.LastFuel = fuel;
            source.LastHeat = heat;
            source.LastSaunaTemp = saunaTemp;

            session.SendWorldMessage(
                new HeatSourceState
                {
                    SourceId = source.Id,
                    Flags = flags,
                    Fuel = fuel,
                    HeatOutput = heat,
                    SaunaTemp = saunaTemp,
                },
                Channel.ReliableOrdered);
        }

        // ---- Discovery --------------------------------------------------------

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            Add("CABIN/Cabin/woodstove/Fireplace", Kind.Woodstove);
            Add("COTTAGE/Stuff/Sauna/Stove", Kind.Sauna);
            Add("COTTAGE/Stuff/Fireplace", Kind.Fireplace);
            Add("YARD/Building/LIVINGROOM/Fireplace", Kind.Fireplace);
            Add("COTTAGE/Stuff/Grill/Fireplace", Kind.Fireplace);
            // Home electric sauna (COVERAGE-ROADMAP 6.2): its Simulation::Time exposes the same
            // SaunaHeat/StoveHeat vars, so it heats identically via the existing sauna path.
            // Its Power/Fuse follow the home electricity, so the 1.3 blackout cuts it too.
            Add("YARD/Building/SAUNA/Sauna", Kind.Sauna);
        }

        private void Add(string containerPath, Kind kind)
        {
            var source = new Source
            {
                Id = StableHash.Fnv1a32(containerPath),
                ContainerPath = containerPath,
                Kind = kind,
            };
            _sources.Add(source);
            _byId[source.Id] = source;
        }

        private void LocatePending()
        {
            for (int i = 0; i < _sources.Count; i++)
                LocateSource(_sources[i]);
        }

        private void LocateSource(Source source)
        {
            GameObject? container;
            try
            {
                container = GameObject.Find(source.ContainerPath);
            }
            catch
            {
                return;
            }

            if (container == null)
            {
                // Not (yet) in the scene — keep trying quietly; some saves lack a source entirely.
                return;
            }

            var root = container.transform;
            source.Anchor = root;

            if (source.SetFire == null) source.SetFire = FindChildFsm(root, "SetFire", "Use");
            if (source.WoodTrigger == null) source.WoodTrigger = FindChildFsm(root, "WoodTrigger", "Trigger");
            if (source.SausageTrigger == null) source.SausageTrigger = FindChildFsm(root, "SausageTrigger", "Logic");
            if (source.Simulation == null) source.Simulation = FindChildFsm(root, "Simulation", "Time");
            if (source.StoveTrigger == null) source.StoveTrigger = FindChildFsm(root, "StoveTrigger", "Steam");

            if (source.SetFire != null)
            {
                if (source.Hiillos == null) source.Hiillos = source.SetFire.FsmVariables.FindFsmBool("Hiillos");
                if (source.Woods == null) source.Woods = source.SetFire.FsmVariables.FindFsmInt("Woods");
                if (source.HeatingEfficiency == null) source.HeatingEfficiency = source.SetFire.FsmVariables.FindFsmFloat("HeatingEfficiency");
            }

            if (source.Woods == null && source.WoodTrigger != null)
                source.Woods = source.WoodTrigger.FsmVariables.FindFsmInt("Woods");

            if (source.Simulation != null)
            {
                if (source.SaunaHeat == null) source.SaunaHeat = source.Simulation.FsmVariables.FindFsmFloat("SaunaHeat");
                if (source.StoveHeat == null) source.StoveHeat = source.Simulation.FsmVariables.FindFsmFloat("StoveHeat");
            }

            if (!source.LoggedFound)
            {
                source.LoggedFound = true;
                WinterMPPlugin.Log.LogInfo($"HeatSourceSync: located {source.Kind} '{source.ContainerPath}' (id {source.Id:X8}).");
            }

            InstallGuestHooks(source);
        }

        // On a guest, the player lighting/feeding/grilling/löyly'ing a source fires that
        // source's own FSM state locally; we relay it to the host as an anyone-triggers
        // intent so the authoritative (save-owning) world advances and re-broadcasts.
        private void InstallGuestHooks(Source source)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            InstallHookOnce(ref source.HookedSetFire, source.SetFire, "Start fire 2", source, HeatSourceIntent.ActionLight);
            InstallHookOnce(ref source.HookedStove, source.StoveTrigger, "Steam", source, HeatSourceIntent.ActionSaunaThrow);
            InstallHookOnce(ref source.HookedSausage, source.SausageTrigger, "State 4", source, HeatSourceIntent.ActionGrill);
            InstallHookOnce(ref source.HookedWood, source.WoodTrigger, "State 1", source, HeatSourceIntent.ActionFeedWood);
        }

        private void InstallHookOnce(ref bool installed, PlayMakerFSM? fsm, string stateName, Source source, byte action)
        {
            if (installed || fsm == null) return;

            // FsmHook.OnStateEnter prepends a fresh action each call, so it must run once per FSM.
            var captured = source;
            var capturedAction = action;
            if (FsmHook.OnStateEnter(fsm, stateName, () => EmitIntent(captured, capturedAction)))
                installed = true;
        }

        private void EmitIntent(Source source, byte action)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.PlayerCount == 0) return;
            if (Time.unscaledTime < source.NextIntentAt) return;
            source.NextIntentAt = Time.unscaledTime + IntentCooldownSeconds;

            session.SendWorldMessage(
                new HeatSourceIntent
                {
                    SourceId = source.Id,
                    Action = action,
                    PlayerId = session.LocalPlayerId,
                    Sequence = ++source.OutIntentSequence,
                },
                Channel.ReliableOrdered);
            SyncEventLog.Record("heat-intent-out", $"{source.Id:X8} action {action}");
        }

        private static PlayMakerFSM? FindChildFsm(Transform root, string childName, string fsmName)
        {
            try
            {
                // Transform.Find reaches inactive children (GameObject.Find does not).
                var child = root.Find(childName);
                if (child == null) return null;

                var fsms = child.GetComponents<PlayMakerFSM>();
                if (fsms == null) return null;

                foreach (var fsm in fsms)
                {
                    if (fsm != null && fsm.FsmName == fsmName) return fsm;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static void FireEvent(PlayMakerFSM? fsm, string eventName)
        {
            if (fsm == null) return;
            try
            {
                fsm.SendEvent(eventName);
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("HeatSourceSync: event '" + eventName + "' failed: " + e.Message);
            }
        }

        private static bool TryGetAction(Source source, byte action, out PlayMakerFSM? fsm, out string eventName)
        {
            switch (action)
            {
                case HeatSourceIntent.ActionLight:
                    fsm = source.SetFire;
                    eventName = "USE";
                    return true;
                case HeatSourceIntent.ActionFeedWood:
                    fsm = source.WoodTrigger;
                    eventName = "WOOD";
                    return true;
                case HeatSourceIntent.ActionGrill:
                    fsm = source.SausageTrigger;
                    eventName = "SAUSAGE";
                    return true;
                case HeatSourceIntent.ActionSaunaThrow:
                    fsm = source.StoveTrigger;
                    eventName = "STEAM";
                    return true;
                default:
                    fsm = null;
                    eventName = string.Empty;
                    return false;
            }
        }

        private static bool IsGuestNear(SessionManager session, byte playerId, Vector3 targetPosition)
        {
            float now = Time.unscaledTime;
            foreach (var player in session.Players)
            {
                if (player.PlayerId != playerId) continue;
                if (player.LastTransformTime <= 0f || now - player.LastTransformTime > IntentPlayerPoseMaxAgeSeconds)
                    return false;
                return (player.Position - targetPosition).sqrMagnitude
                    <= IntentPlayerMaxDistance * IntentPlayerMaxDistance;
            }
            return false;
        }

        // ---- var helpers ------------------------------------------------------

        private static bool ReadBool(FsmBool? v) => v != null && v.Value;
        private static int ReadInt(FsmInt? v) => v != null ? v.Value : 0;
        private static float ReadFloat(FsmFloat? v) => v != null ? v.Value : 0f;

        private static void WriteBool(FsmBool? v, bool value) { if (v != null) v.Value = value; }
        private static void WriteInt(FsmInt? v, int value) { if (v != null) v.Value = value; }
        private static void WriteFloat(FsmFloat? v, float value) { if (v != null) v.Value = value; }

        private static byte HeatByte(FsmFloat? efficiency)
        {
            if (efficiency == null) return 200;
            float e = efficiency.Value;
            // HeatingEfficiency reads as a 0..1 factor in the dumps; fall back gracefully if a
            // patch rescales it.
            float scaled = e <= 1f ? e * 255f : e;
            return ClampByte(scaled);
        }

        private static byte ClampByte(float value) => (byte)Mathf.Clamp(value, 0f, 255f);
        private static ushort ClampUShort(float value) => (ushort)Mathf.Clamp(value, 0f, 65535f);
    }
}
