using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Shared kitchen appliances (oven/stove) as host-owned world state
    /// (COVERAGE-ROADMAP 6.1 / 6.4). The hotplate heats, fire-hazard sim and fuse run
    /// per-client, so an unattended stove burning down one player's house is absent on the
    /// other's. The <b>host</b> owns each oven: it reads <c>OvenStove/Simulation :: Data</c>
    /// and broadcasts the hotplate heats + fire + fuse on change + join; guests apply them
    /// (fire hazard is deterministic from the synced heats). Knob settings show up in the
    /// synced heats. A multi-source host broadcaster like <see cref="UtilityBillSync"/>.
    /// </summary>
    internal sealed class ApplianceSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 1.5f;
        private const float KeepAliveSeconds = 20f;
        private const float HeatScale = 2.55f; // heat 0..100 -> byte 0..255

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
            public bool Ready => Sim != null && Heats[0] != null;
        }

        private readonly List<Oven> _ovens = new List<Oven>();
        private bool _built;
        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;

        public void Clear()
        {
            _ovens.Clear();
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
                for (int i = 0; i < _ovens.Count; i++) Locate(_ovens[i]);
            }

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
                if (_ovens[i].Ready) yield return BuildState(_ovens[i]);
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
            if (!oven.Ready) return;

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
        }

        private void HostBroadcastIfChanged(SessionManager session, Oven oven, bool keepAlive)
        {
            Locate(oven);
            if (!oven.Ready) return;
            var state = BuildState(oven);
            bool changed = !oven.HasLast || oven.LastFlags != state.Flags;
            byte[] heats = { state.Heat1, state.Heat2, state.Heat3, state.Heat4 };
            for (int i = 0; i < 4 && !changed; i++) changed = oven.LastHeat[i] != heats[i];
            if (!changed && !keepAlive) return;

            oven.HasLast = true;
            oven.LastFlags = state.Flags;
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
            return new ApplianceState
            {
                ApplianceId = oven.Id,
                Kind = ApplianceState.KindOven,
                Flags = flags,
                Heat1 = Read(0), Heat2 = Read(1), Heat3 = Read(2), Heat4 = Read(3),
            };
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
            if (oven.Ready) return;
            GameObject? container;
            try { container = GameObject.Find(oven.ContainerPath); }
            catch { return; }
            if (container == null) return;

            var sim = container.transform.Find("Simulation");
            if (sim == null) return;
            foreach (var fsm in sim.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null || fsm.FsmName != "Data") continue;
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
        }
    }
}
