using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        // One synchronous capture owns these roots. It performs only reads, and
        // discards the lookup before any state is published or yielded to callers.
        private sealed class EngineSourceLookup
        {
            private readonly Dictionary<string, GameObject?> _roots = new Dictionary<string, GameObject?>(StringComparer.Ordinal);

            internal GameObject? Find(string path)
            {
                int slash = path.IndexOf('/');
                string rootName = slash < 0 ? path : path.Substring(0, slash);
                if (!_roots.TryGetValue(rootName, out var root))
                {
                    root = GameObject.Find("/" + rootName);
                    _roots.Add(rootName, root);
                }
                if (root == null || slash < 0) return root;
                var target = root.transform.Find(path.Substring(slash + 1));
                return target == null || !target.gameObject.activeInHierarchy ? null : target.gameObject;
            }
        }

        private readonly EngineBlockReplica _engineBlockReplica = new EngineBlockReplica();
        private EngineBlockPublication _engineBlockPublication = new EngineBlockPublication();
        private bool _engineBlockCaptureFailed;
        private float _nextEngineBlockPoll, _nextEngineBlockKeepalive;

        internal void OnEngineBlockState(EngineBlockState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected) return;
            var old = _engineBlockReplica.Inputs;
            if (!_engineBlockReplica.Receive(state)) { SyncEventLog.Record("engine-block-rejected", "revision=" + state.Revision); return; }
            if (old == null || old.Revision != state.Revision)
                SyncEventLog.Record("engine-block-received", "revision=" + state.Revision + " flags=" + state.Flags + " wear=" + state.Wear
                    + " chamber=" + state.FuelChamber + " reserve=" + state.CarbReserve + " mixture=" + state.SettingMixture
                    + " carbPower=" + state.CarburettorPower + "/" + state.CarburettorTorque + "/" + state.CarburettorPowerAdd
                    + " exhaust=" + state.ExhaustFlags + " valves=" + state.ValvesAvailable
                    + " filterPower=" + state.AirCleanerPower + "/" + state.AirCleanerTorque + "/" + state.AirCleanerPowerAdd);
        }

        internal EngineBlockState? BuildEngineBlockState()
        {
            var session = SessionManager.Instance; var rule = SyncCatalog.GuestEngineInputs?.Block;
            if (session == null || !session.IsHost || rule == null) return null;
            var lookup = new EngineSourceLookup();
            byte flags = 0; float wear = 0; GameObject? head = null, block = null;
            try
            {
                CaptureEngineBlock(lookup, rule, out flags, out wear, out head, out block); _engineBlockCaptureFailed = false;
            }
            catch (Exception error)
            {
                flags = 0; wear = 0; head = null; block = null;
                if (!_engineBlockCaptureFailed)
                {
                    _engineBlockCaptureFailed = true;
                    WinterMPPlugin.Log.LogWarning("WorldSync: engine block input unavailable: " + error.Message);
                    SyncEventLog.Record("engine-block-unavailable", error.Message);
                }
            }
            var state = new EngineBlockState { Flags = flags, Wear = wear };
            if (head != null)
            {
                CaptureRockerCover(head, rule.RockerCover, state);
                CaptureCarburettor(head, rule.Carburettor, state);
                CaptureAirCleaner(head, rule.AirCleaner, state);
            }
            CaptureExhaust(lookup, head, rule.Exhaust, state);
            CaptureValveSettings(head, state);
            CaptureOilpan(block, rule.Oilpan, state);
            CaptureRadiator(lookup, rule.Radiator, state);
            CaptureCoolantHoses(lookup, rule.CoolantHoses, state);
            CaptureCoolingAirflow(lookup, rule.CoolingAirflow, state);
            CaptureCoolingAmbient(lookup, rule.CoolingAmbient, state);
            return _engineBlockPublication.Observe(state);
        }

        private void CaptureEngineBlock(EngineSourceLookup lookup, EngineBlockData rule, out byte flags, out float wear, out GameObject? head, out GameObject? block)
        {
            flags = 0; wear = 0; head = null; block = null;
            var mount = lookup.Find(rule.Path);
            if (mount == null || !mount.activeInHierarchy) return;
            if (ScenePath.Of(mount.transform) != rule.Path) throw new InvalidOperationException("Engine block mount path changed.");
            var data = EngineBlockDataFsm(mount, rule.Fsm);
            var installed = data.FsmVariables.FindFsmBool("Installed");
            var damaged = data.FsmVariables.FindFsmBool("Damaged");
            var value = data.FsmVariables.FindFsmFloat("Wear");
            var active = data.FsmVariables.FindFsmGameObject("ActivePart");
            if (installed == null || damaged == null || value == null || active == null)
                throw new InvalidOperationException("Missing engine block inputs.");
            if (!data.enabled || !data.Fsm.Initialized || !data.Fsm.Started) return;
            if (!installed.Value)
            {
                if (data.ActiveStateName == rule.IdleState) flags = EngineBlockState.Available;
                return;
            }
            // Installed is set before the native parent/body transition finishes.
            // Only the steady Update state may expose the fitted engine to guests.
            var part = active.Value;
            if (data.ActiveStateName != rule.ReadyState || part == null || !part.activeInHierarchy
                || part.transform.parent != mount.transform) return;
            var partData = EngineBlockDataFsm(part, rule.Fsm);
            if (partData.FsmVariables.FindFsmInt("AssemblyID")?.Value != 1
                || partData.FsmVariables.FindFsmFloat("Wear") == null) return;
            if (float.IsNaN(value.Value) || float.IsInfinity(value.Value)) throw new InvalidOperationException("Invalid engine block wear.");
            flags = (byte)(EngineBlockState.Available | EngineBlockState.Installed | (damaged.Value ? EngineBlockState.Damaged : 0));
            wear = value.Value;
            block = part;
            head = CaptureCylinderHead(part, rule.Head);
            if (head != null) flags |= EngineBlockState.HeadInstalled;
        }

        private static PlayMakerFSM EngineBlockDataFsm(GameObject obj, string name)
        {
            PlayMakerFSM? found = null;
            foreach (var candidate in obj.GetComponents<PlayMakerFSM>())
                if (candidate.FsmName == name)
                { if (found != null) throw new InvalidOperationException("Ambiguous engine block Data."); found = candidate; }
            return found ?? throw new InvalidOperationException("Missing engine block Data.");
        }

        private void ProcessEngineBlock(SessionManager session)
        {
            if (!session.IsHost || Time.unscaledTime < _nextEngineBlockPoll) return;
            _nextEngineBlockPoll = Time.unscaledTime + .2f;
            var state = BuildEngineBlockState(); if (state == null) return;
            if (!_engineBlockPublication.NeedsBroadcast && Time.unscaledTime < _nextEngineBlockKeepalive) return;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
            if (_engineBlockPublication.NeedsBroadcast)
                SyncEventLog.Record("engine-block-published", "revision=" + state.Revision + " flags=" + state.Flags + " wear=" + state.Wear
                    + " chamber=" + state.FuelChamber + " reserve=" + state.CarbReserve + " mixture=" + state.SettingMixture
                    + " carbPower=" + state.CarburettorPower + "/" + state.CarburettorTorque + "/" + state.CarburettorPowerAdd
                    + " exhaust=" + state.ExhaustFlags + " valves=" + state.ValvesAvailable
                    + " filterPower=" + state.AirCleanerPower + "/" + state.AirCleanerTorque + "/" + state.AirCleanerPowerAdd);
            _engineBlockPublication.MarkBroadcast(state.Revision); _nextEngineBlockKeepalive = Time.unscaledTime + 5f;
        }

        private void ClearEngineBlock()
        {
            _engineBlockReplica.Clear(); _engineBlockPublication = new EngineBlockPublication(); _engineBlockCaptureFailed = false; _engineHeadCaptureFailed = false; _carburettorCaptureFailed = false; _airCleanerCaptureFailed = false; _valveCaptureFailed = false; _oilpanCaptureFailed = false; _rockerCoverCaptureFailed = false; _radiatorCaptureFailed = false; _coolingAmbientCaptureFailed = false; Array.Clear(_coolingAirflowCaptureFailed, 0, _coolingAirflowCaptureFailed.Length); Array.Clear(_coolantHoseCaptureFailed, 0, _coolantHoseCaptureFailed.Length); Array.Clear(_exhaustCaptureFailed, 0, _exhaustCaptureFailed.Length);
            _nextEngineBlockPoll = _nextEngineBlockKeepalive = 0;
        }
    }
}
