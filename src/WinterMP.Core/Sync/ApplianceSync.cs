using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Host-owned kitchen stove simulation. Guests request native knob steps and
    /// receive absolute settings, heat and cooking outputs while their simulation
    /// is paused. Ignition commits remain edge-carried because their native states
    /// are transients that polling cannot reliably observe.
    /// </summary>
    internal sealed partial class ApplianceSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 1.5f;
        private const float KeepAliveSeconds = 20f;
        private const float HeatScale = 2.55f; // heat 0..100 -> byte 0..255

        // Plate index 0-3 → the FSM's ignition-commit state.
        private static readonly string[] IgnitionStates =
        {
            "Start fire", "Start fire 2", "Start fire 3", "Start fire 4",
        };

        private sealed class Oven
        {
            public uint Id;
            public string ContainerPath = string.Empty;
            public bool LoggedFound;
            public PlayMakerFSM? Sim;
            public FsmFloat?[] Heats = new FsmFloat?[4];
            public FsmBool? Fuse;
            public bool HasLast;
            public byte LastFlags;
            public byte[] LastHeat = new byte[4];
            // Host: ignition edge counter + which plate lit (1-4). Guest: replay bookkeeping —
            // the first received count only seeds (a fire that predates the join is not
            // re-ignited hours later on the joiner).
            public byte FireCount;
            public byte LastFirePlate;
            public byte LastSentFireCount;
            public readonly HashSet<string> HookedFireStates = new HashSet<string>();
            public byte AppliedFireCount;
            public bool FireSeeded;
            public StoveBinding? Stove;
            public bool StoveFailed;
            public ApplianceState? StoveObserved, StoveReceived, StovePending;
            public uint StoveSentRevision;
            // Guest fire reports (v89): pacing + replay echo suppression.
            public float NextFireReportAt;
            public float SuppressFireReportUntil;
            public bool Ready => Sim != null && Heats[0] != null;
        }

        private readonly List<Oven> _ovens = new List<Oven>();
        private readonly Dictionary<byte, ushort> _lastFireReportSequences = new Dictionary<byte, ushort>();
        private ushort _outFireSequence;
        private bool _built;
        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;

        public void Clear()
        {
            foreach (var oven in _ovens) RestoreStove(oven);
            _ovens.Clear();
            _lastFireReportSequences.Clear();
            _outFireSequence = 0;
            _built = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            EnsureBuilt();
            if (Time.unscaledTime >= _nextProbeAt)
            {
                _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds;
                for (int i = 0; i < _ovens.Count; i++)
                {
                    Locate(_ovens[i]);
                    EnsureFireHooks(_ovens[i]);
                }
            }
            foreach (var oven in _ovens)
                if (oven.StovePending != null && oven.Stove != null) ReceiveStove(oven, oven.StovePending);

            if (!session.IsHost) return;
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
            for (int i = 0; i < _ovens.Count; i++) HostBroadcastIfChanged(session, _ovens[i], keepAlive);
        }

        public IEnumerable<ApplianceState> BuildSnapshots()
        {
            EnsureBuilt();
            for (int i = 0; i < _ovens.Count; i++)
            {
                Locate(_ovens[i]);
                if (_ovens[i].Ready && !StoveWaiting(_ovens[i]))
                {
                    var state = BuildState(_ovens[i]);
                    if (!_ovens[i].StoveFailed) yield return state;
                }
            }
        }

        public void Apply(ApplianceState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            EnsureBuilt();
            Oven? oven = null;
            for (int i = 0; i < _ovens.Count; i++) if (_ovens[i].Id == message.ApplianceId) { oven = _ovens[i]; break; }
            if (oven == null) return;
            Locate(oven);
            if (message.StoveRevision != 0) { ReceiveStove(oven, message); return; }
            if (!oven.Ready) return;
            if (oven.Stove != null) return;

            byte[] heats = { message.Heat1, message.Heat2, message.Heat3, message.Heat4 };
            try
            {
                for (int i = 0; i < 4; i++)
                    if (oven.Heats[i] != null) oven.Heats[i]!.Value = heats[i] / HeatScale;
                if (oven.Fuse != null) oven.Fuse.Value = (message.Flags & ApplianceState.FlagFuseOk) != 0;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("ApplianceSync: apply failed for " + oven.ContainerPath + ": " + e.Message);
            }

            ApplyIgnition(oven, message);
        }

        private static void ApplyIgnition(Oven oven, ApplianceState message)
        {
            if (!oven.FireSeeded)
            {
                oven.FireSeeded = true;
                oven.AppliedFireCount = message.FireCount;
            }
            else if (message.FireCount != oven.AppliedFireCount)
            {
                oven.AppliedFireCount = message.FireCount;
                if (message.FirePlate >= 1 && message.FirePlate <= 4)
                {
                    // Our own hook fires on the replayed entry; that must not read as a
                    // fresh local roll and get reported back (v89 guest fire reports).
                    oven.SuppressFireReportUntil = Time.unscaledTime + 1f;
                    ReplayIgnition(oven, message.FirePlate);
                }
            }
        }

        /// <summary>Guest: the host's oven ignited — run the same commit state here, once.</summary>
        private static void ReplayIgnition(Oven oven, byte plate)
        {
            if (oven.Sim == null) return;
            string stateName = IgnitionStates[plate - 1];
            if (oven.Stove != null && oven.Stove.Pause.Active)
            {
                foreach (var action in oven.Stove.Ignitions[plate - 1]) if (action.Enabled) action.OnEnter();
                return;
            }
            if (!FsmHook.EnsureRemoteEntry(oven.Sim, stateName))
            {
                WinterMPPlugin.Log.LogWarning($"ApplianceSync: cannot replay ignition '{stateName}' on {oven.ContainerPath}.");
                return;
            }

            var world = WorldSyncManager.Instance;
            if (world != null) world.ApplyingRemote = true;
            try { FsmHook.FireRemoteEntry(oven.Sim, stateName); }
            finally { if (world != null) world.ApplyingRemote = false; }
            WinterMPPlugin.Log.LogInfo($"ApplianceSync: replayed ignition (plate {plate}) on {oven.ContainerPath}.");
        }

        private void EnsureFireHooks(Oven oven)
        {
            if (oven.Sim == null || oven.HookedFireStates.Count == IgnitionStates.Length) return;
            for (int plate = 0; plate < IgnitionStates.Length; plate++)
            {
                string stateName = IgnitionStates[plate];
                if (oven.HookedFireStates.Contains(stateName)) continue;
                byte plateNumber = (byte)(plate + 1);
                // Per-state latch (not all-or-nothing) so a not-yet-ready state retries
                // without re-hooking the ones that already took.
                if (FsmHook.OnStateEnter(oven.Sim, stateName, () => OnIgnition(oven, plateNumber)))
                    oven.HookedFireStates.Add(stateName);
            }
        }

        private void OnIgnition(Oven oven, byte plate)
        {
            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            if (!session.IsHost)
            {
                // A guest's own sim rolled the fire (FireHazard RNG is per-client even over
                // synced heats). Report it so the host single-sources the house fire —
                // EXCEPT when this entry is our own replay of the host's ignition, which
                // must not echo (the replay bumps AppliedFireCount just before firing).
                if (Time.unscaledTime < oven.SuppressFireReportUntil) return;
                if (Time.unscaledTime < oven.NextFireReportAt) return;
                oven.NextFireReportAt = Time.unscaledTime + 30f;
                session.SendWorldMessage(new ApplianceFireReport
                {
                    ApplianceId = oven.Id,
                    Plate = plate,
                    PlayerId = session.LocalPlayerId,
                    Sequence = ++_outFireSequence,
                }, Channel.ReliableOrdered);
                WinterMPPlugin.Log.LogInfo($"ApplianceSync: reported local ignition (plate {plate}) on {oven.ContainerPath}.");
                return;
            }

            oven.FireCount++;
            oven.LastFirePlate = plate;
            // Picked up by the next 1.5 s host tick via the FireCount change predicate.
            WinterMPPlugin.Log.LogInfo($"ApplianceSync: oven ignition (plate {plate}) on {oven.ContainerPath}.");
        }

        /// <summary>Host: a guest's oven sim ignited — replay it here so the fire is shared.</summary>
        public bool OnHostFireReport(ApplianceFireReport message)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return false;
            if (message.Plate < 1 || message.Plate > 4) return false;
            EnsureBuilt();
            Oven? oven = null;
            for (int i = 0; i < _ovens.Count; i++) if (_ovens[i].Id == message.ApplianceId) { oven = _ovens[i]; break; }
            if (oven == null) return false;
            Locate(oven);
            if (!oven.Ready || StoveWaiting(oven) || oven.Stove != null || oven.StoveFailed) return false;

            if (_lastFireReportSequences.TryGetValue(message.PlayerId, out ushort previous))
            {
                ushort diff = (ushort)(message.Sequence - previous);
                if (diff == 0 || diff > short.MaxValue) return false;
            }
            _lastFireReportSequences[message.PlayerId] = message.Sequence;

            // The replayed entry triggers our own hook → OnIgnition (host role) → count bump.
            ReplayIgnition(oven, message.Plate);
            return true;
        }

        /// <summary>Host: a player (re)joined — its report counter restarted; drop the stale latch.</summary>
        public void ForgetPlayer(byte playerId)
        {
            _lastFireReportSequences.Remove(playerId);
            foreach (var oven in _ovens) oven.Stove?.Ledger.Forget(playerId);
        }

        private void HostBroadcastIfChanged(SessionManager session, Oven oven, bool keepAlive)
        {
            Locate(oven);
            if (!oven.Ready || StoveWaiting(oven)) return;
            var state = BuildState(oven);
            if (oven.StoveFailed) return;
            bool changed = !oven.HasLast || oven.LastFlags != state.Flags
                || oven.LastSentFireCount != state.FireCount || oven.StoveSentRevision != state.StoveRevision;
            byte[] heats = { state.Heat1, state.Heat2, state.Heat3, state.Heat4 };
            for (int i = 0; i < 4 && !changed; i++) changed = oven.LastHeat[i] != heats[i];
            if (!changed && !keepAlive) return;

            oven.HasLast = true;
            oven.LastFlags = state.Flags;
            oven.LastSentFireCount = state.FireCount;
            oven.StoveSentRevision = state.StoveRevision;
            for (int i = 0; i < 4; i++) oven.LastHeat[i] = heats[i];
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private static ApplianceState BuildState(Oven oven)
        {
            byte Read(int i) => (byte)Mathf.Clamp((oven.Heats[i] != null ? oven.Heats[i]!.Value : 0f) * HeatScale, 0f, 255f);
            byte flags = 0;
            bool onFire = false;
            try { onFire = oven.Sim != null && oven.Sim.Fsm != null && oven.Sim.Fsm.ActiveStateName == "Fire"; } catch { }
            if (onFire) flags |= ApplianceState.FlagFire;
            if (oven.Fuse == null || oven.Fuse.Value) flags |= ApplianceState.FlagFuseOk;
            var state = new ApplianceState
            {
                ApplianceId = oven.Id,
                Kind = ApplianceState.KindOven,
                Flags = flags,
                Heat1 = Read(0), Heat2 = Read(1), Heat3 = Read(2), Heat4 = Read(3),
                FireCount = oven.FireCount,
                FirePlate = oven.LastFirePlate,
            };
            try { CaptureStove(oven, state); }
            catch (System.Exception error) { FailStove(oven, error); }
            return state;
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            Add("YARD/Building/KITCHEN/OvenStove");
            Add("HOMENEW/Functions/ElectricThings/OvenStove");
        }

        private void Add(string containerPath)
        {
            _ovens.Add(new Oven { Id = StableHash.Fnv1a32(containerPath), ContainerPath = containerPath });
        }

        private void Locate(Oven oven)
        {
            if (oven.Ready) { BindStove(oven); return; }
            GameObject? container;
            try { container = GameObject.Find(oven.ContainerPath); }
            catch { return; }
            if (container == null) return;

            var sim = container.transform.Find("Simulation");
            if (sim == null) return;
            foreach (var fsm in sim.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null || fsm.FsmName != "Data" || !fsm.Fsm.Initialized || !fsm.Fsm.Started) continue;
                oven.Sim = fsm;
                var v = fsm.FsmVariables;
                for (int i = 0; i < 4; i++) oven.Heats[i] = v.FindFsmFloat("HotPlate" + (i + 1) + "Heat");
                oven.Fuse = v.FindFsmBool("Fuse");
                break;
            }

            if (!oven.LoggedFound && oven.Ready)
            {
                oven.LoggedFound = true;
                WinterMPPlugin.Log.LogInfo($"ApplianceSync: located oven '{oven.ContainerPath}'.");
            }
            if (oven.Ready) BindStove(oven);
        }
    }
}
