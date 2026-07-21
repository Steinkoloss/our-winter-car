using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Host-side source of truth for the several delivery-job sites. The game keeps
    /// a separate FSM per house, so a single global job snapshot cannot converge a
    /// guest that is looking at a different customer's well or wood pile.
    /// </summary>
    internal sealed class JobSiteSync
    {
        private const float ScanIntervalSeconds = 5f;
        private const float SendIntervalSeconds = 4f;
        private const float ChangeEpsilon = 0.01f;

        private sealed class Site
        {
            public uint Id;
            public byte Kind;
            public string Path = string.Empty;
            public FsmFloat? Primary;
            public FsmInt? PrimaryInt;   // farm JobStage is an int, not a float
            public FsmFloat? Secondary;
            public FsmBool? Active;
            public FsmBool? HoseAttached;
            public FsmBool? HoseInWaste;
            public FsmBool? Sucking;
            public ushort OutSequence;
            public ushort LastRemoteSequence;
            public float LastPrimary = float.NaN;
            public float LastSecondary = float.NaN;
            public byte LastFlags = byte.MaxValue;
        }

        private readonly Dictionary<uint, Site> _sites = new Dictionary<uint, Site>();
        private readonly Dictionary<uint, JobSiteState> _pending = new Dictionary<uint, JobSiteState>();
        private float _nextScanAt;
        private float _nextSendAt;

        public void Clear()
        {
            _sites.Clear();
            _pending.Clear();
            _nextScanAt = 0f;
            _nextSendAt = 0f;
        }

        public void Update(SessionManager session)
        {
            Scan();
            ApplyPending();
            if (!session.IsHost || session.PlayerCount == 0 || Time.unscaledTime < _nextSendAt)
                return;

            _nextSendAt = Time.unscaledTime + SendIntervalSeconds;
            foreach (var state in BuildStates(changedOnly: true))
                session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        public IEnumerable<JobSiteState> BuildSnapshots()
        {
            Scan(force: true);
            foreach (var state in BuildStates(changedOnly: false))
                yield return state;
        }

        public void Apply(JobSiteState message)
        {
            Scan();
            if (!_sites.TryGetValue(message.SiteId, out var site) || site.Kind != message.Kind)
            {
                _pending[message.SiteId] = message;
                return;
            }

            ushort diff = (ushort)(message.Sequence - site.LastRemoteSequence);
            if (site.LastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            site.LastRemoteSequence = message.Sequence;

            if (!IsFinite(message.Primary) || !IsFinite(message.Secondary)) return;
            if (site.PrimaryInt != null) site.PrimaryInt.Value = Mathf.Max(0, Mathf.RoundToInt(message.Primary));
            if (site.Primary != null) site.Primary.Value = Mathf.Max(0f, message.Primary);
            if (site.Secondary != null) site.Secondary.Value = Mathf.Max(0f, message.Secondary);
            if (site.Active != null) site.Active.Value = message.IsActive;
            if (site.HoseAttached != null)
                site.HoseAttached.Value = (message.Flags & JobSiteState.FlagHoseAttached) != 0;
            if (site.HoseInWaste != null)
                site.HoseInWaste.Value = (message.Flags & JobSiteState.FlagHoseInWaste) != 0;
            if (site.Sucking != null)
                site.Sucking.Value = (message.Flags & JobSiteState.FlagSucking) != 0;
        }

        private IEnumerable<JobSiteState> BuildStates(bool changedOnly)
        {
            foreach (var site in _sites.Values)
            {
                float primary = site.PrimaryInt != null ? site.PrimaryInt.Value
                    : (site.Primary != null ? site.Primary.Value : 0f);
                float secondary = site.Secondary != null ? site.Secondary.Value : 0f;
                byte flags = ReadFlags(site);
                bool changed = float.IsNaN(site.LastPrimary)
                    || Mathf.Abs(primary - site.LastPrimary) > ChangeEpsilon
                    || Mathf.Abs(secondary - site.LastSecondary) > ChangeEpsilon
                    || flags != site.LastFlags;
                if (changedOnly && !changed) continue;

                // Only the periodic delta path (changedOnly) owns the change-detection
                // baseline. The join-snapshot path (changedOnly==false) emits current
                // values to the joining peer ONLY, so it must NOT advance Last*: doing so
                // would mark a not-yet-broadcast change as sent and strand already-connected
                // guests on a stale value (job-site state is in no checksum, so it never heals).
                if (changedOnly)
                {
                    site.LastPrimary = primary;
                    site.LastSecondary = secondary;
                    site.LastFlags = flags;
                }
                yield return new JobSiteState
                {
                    SiteId = site.Id,
                    Kind = site.Kind,
                    Flags = flags,
                    Sequence = ++site.OutSequence,
                    Primary = primary,
                    Secondary = secondary,
                };
            }
        }

        private void Scan(bool force = false)
        {
            if (!force && Time.unscaledTime < _nextScanAt) return;
            _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;

            var fsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM));
            foreach (var obj in fsms)
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;

                try
                {
                    string path = ScenePath.Of(fsm.transform);
                    byte kind;
                    FsmFloat? primary;
                    FsmInt? primaryInt = null;
                    FsmFloat? secondary;
                    FsmBool? active;
                    FsmBool? hoseAttached = null;
                    FsmBool? hoseInWaste = null;
                    FsmBool? sucking = null;

                    if (path == "JOBS/Farm/Farmer/Walker" && fsm.FsmName == "Speak")
                    {
                        kind = JobSiteState.KindFarm;
                        primary = null;
                        primaryInt = fsm.FsmVariables.FindFsmInt("JobStage");
                        secondary = null;
                        active = fsm.FsmVariables.FindFsmBool("Done");
                    }
                    else if (path.StartsWith("JOBS/HouseShit", StringComparison.Ordinal)
                        && fsm.FsmName == "Level")
                    {
                        kind = JobSiteState.KindSewage;
                        primary = fsm.FsmVariables.FindFsmFloat("ShitLevel");
                        secondary = fsm.FsmVariables.FindFsmFloat("BasePrice");
                        active = fsm.FsmVariables.FindFsmBool("Called");
                    }
                    else if (path.StartsWith("JOBS/HouseWood", StringComparison.Ordinal)
                        && fsm.FsmName == "Logic")
                    {
                        kind = JobSiteState.KindFirewood;
                        primary = fsm.FsmVariables.FindFsmFloat("Surplus");
                        secondary = fsm.FsmVariables.FindFsmFloat("Penalty");
                        active = fsm.FsmVariables.FindFsmBool("Order");
                    }
                    else if (path == "GIFU(750/450psi)/ShitTank" && fsm.FsmName == "Pump")
                    {
                        kind = JobSiteState.KindSewageTruck;
                        primary = fsm.FsmVariables.FindFsmFloat("ShitLevel");
                        secondary = fsm.FsmVariables.FindFsmFloat("PumpEfficiency");
                        active = fsm.FsmVariables.FindFsmBool("PumpRunning");
                        hoseAttached = fsm.FsmVariables.FindFsmBool("HoseAttached");
                        hoseInWaste = fsm.FsmVariables.FindFsmBool("HoseInShit");
                        sucking = fsm.FsmVariables.FindFsmBool("Sucking");
                    }
                    else
                    {
                        continue;
                    }

                    if ((primary == null && primaryInt == null) || active == null) continue;
                    uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
                    if (_sites.ContainsKey(id)) continue;

                    _sites[id] = new Site
                    {
                        Id = id,
                        Kind = kind,
                        Path = path,
                        Primary = primary,
                        PrimaryInt = primaryInt,
                        Secondary = secondary,
                        Active = active,
                        HoseAttached = hoseAttached,
                        HoseInWaste = hoseInWaste,
                        Sucking = sucking,
                    };
                    WinterMPPlugin.Log.LogInfo($"JobSiteSync: registered {kind} site '{path}'.");
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogDebug($"JobSiteSync: skipped FSM: {e.Message}");
                }
            }
        }

        private void ApplyPending()
        {
            if (_pending.Count == 0) return;
            var applied = new List<uint>();
            foreach (var pair in _pending)
            {
                if (!_sites.ContainsKey(pair.Key)) continue;
                Apply(pair.Value);
                applied.Add(pair.Key);
            }
            foreach (uint id in applied) _pending.Remove(id);
        }

        private static byte ReadFlags(Site site)
        {
            byte flags = site.Active != null && site.Active.Value ? JobSiteState.FlagActive : (byte)0;
            if (site.HoseAttached != null && site.HoseAttached.Value)
                flags |= JobSiteState.FlagHoseAttached;
            if (site.HoseInWaste != null && site.HoseInWaste.Value)
                flags |= JobSiteState.FlagHoseInWaste;
            if (site.Sucking != null && site.Sucking.Value)
                flags |= JobSiteState.FlagSucking;
            return flags;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
