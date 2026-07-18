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
    /// Mirrors the host-owned values behind the three M8 systems whose visual FSMs
    /// initialize independently on every client: classifieds, factory employment,
    /// and the market magazine. It writes only documented scalar state; gameplay
    /// actions remain normal FSM events and shared wallet transactions stay host-owned.
    /// </summary>
    internal sealed class WorldProgressSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float SendIntervalSeconds = 5f;

        private PlayMakerFSM? _classifieds;
        private PlayMakerFSM? _factory;
        private PlayMakerFSM? _magazine;
        private readonly Dictionary<byte, WorldProgressState> _pending = new Dictionary<byte, WorldProgressState>();
        private float _nextProbeAt;
        private float _nextSendAt;
        private ushort _classifiedsSequence;
        private ushort _factorySequence;
        private ushort _magazineSequence;

        public void Reset()
        {
            _classifieds = null;
            _factory = null;
            _magazine = null;
            _pending.Clear();
            _nextProbeAt = 0f;
            _nextSendAt = 0f;
            _classifiedsSequence = 0;
            _factorySequence = 0;
            _magazineSequence = 0;
        }

        public void Update(SessionManager session)
        {
            Locate();
            ApplyPending();
            if (!session.IsHost || session.PlayerCount == 0) return;
            if (Time.unscaledTime < _nextSendAt) return;

            _nextSendAt = Time.unscaledTime + SendIntervalSeconds;
            foreach (var message in BuildSnapshots())
                session.SendWorldMessage(message, Channel.ReliableOrdered);
        }

        public IEnumerable<WorldProgressState> BuildSnapshots()
        {
            Locate();

            if (_classifieds != null)
            {
                yield return new WorldProgressState
                {
                    Kind = WorldProgressKind.Classifieds,
                    Phase = ReadByte(_classifieds, "JobStage"),
                    Sequence = ++_classifiedsSequence,
                    Primary = ReadInt(_classifieds, "Delivered"),
                    Secondary = ReadInt(_classifieds, "Sheets"),
                    Tertiary = ReadInt(_classifieds, "NewDay"),
                    Value = ReadFloat(_classifieds, "Salary"),
                };
            }

            if (_factory != null)
            {
                yield return new WorldProgressState
                {
                    Kind = WorldProgressKind.Factory,
                    Phase = ReadByte(_factory, "EmploymentStage"),
                    Sequence = ++_factorySequence,
                    Primary = ReadInt(_factory, "ProgressPackagesEmpty"),
                    Secondary = ReadInt(_factory, "ProgressPackagesTotal"),
                    Tertiary = ReadInt(_factory, "Paychecks"),
                    Value = ReadFloat(_factory, "WorkMinutesDayF"),
                };
            }

            if (_magazine != null)
            {
                yield return new WorldProgressState
                {
                    Kind = WorldProgressKind.MarkettiMagazine,
                    Phase = ReadByte(_magazine, "LayoutType"),
                    Sequence = ++_magazineSequence,
                    Primary = ReadInt(_magazine, "Day"),
                    Secondary = ReadInt(_magazine, "Index"),
                    Tertiary = ReadInt(_magazine, "Issue"),
                };
            }
        }

        public void Apply(WorldProgressState message)
        {
            Locate();
            switch (message.Kind)
            {
                case WorldProgressKind.Classifieds:
                    if (!ApplyClassifieds(message)) _pending[message.Kind] = message;
                    break;
                case WorldProgressKind.Factory:
                    if (!ApplyFactory(message)) _pending[message.Kind] = message;
                    break;
                case WorldProgressKind.MarkettiMagazine:
                    if (!ApplyMagazine(message)) _pending[message.Kind] = message;
                    break;
            }
        }

        private void ApplyPending()
        {
            if (_pending.Count == 0) return;

            var applied = new List<byte>();
            foreach (var pair in _pending)
            {
                bool done = pair.Key == WorldProgressKind.Classifieds ? ApplyClassifieds(pair.Value)
                    : pair.Key == WorldProgressKind.Factory ? ApplyFactory(pair.Value)
                    : pair.Key == WorldProgressKind.MarkettiMagazine && ApplyMagazine(pair.Value);
                if (done) applied.Add(pair.Key);
            }
            foreach (byte kind in applied) _pending.Remove(kind);
        }

        private bool ApplyClassifieds(WorldProgressState message)
        {
            if (_classifieds == null) return false;
            WriteInt(_classifieds, "JobStage", message.Phase);
            WriteInt(_classifieds, "Delivered", message.Primary);
            WriteInt(_classifieds, "Sheets", message.Secondary);
            WriteInt(_classifieds, "NewDay", message.Tertiary);
            WriteFloat(_classifieds, "Salary", message.Value);
            return true;
        }

        private bool ApplyFactory(WorldProgressState message)
        {
            if (_factory == null) return false;
            WriteInt(_factory, "EmploymentStage", message.Phase);
            WriteInt(_factory, "ProgressPackagesEmpty", message.Primary);
            WriteInt(_factory, "ProgressPackagesTotal", message.Secondary);
            WriteInt(_factory, "Paychecks", message.Tertiary);
            WriteFloat(_factory, "WorkMinutesDayF", message.Value);
            return true;
        }

        private bool ApplyMagazine(WorldProgressState message)
        {
            if (_magazine == null) return false;
            WriteInt(_magazine, "LayoutType", message.Phase);
            WriteInt(_magazine, "Day", message.Primary);
            WriteInt(_magazine, "Index", message.Secondary);
            WriteInt(_magazine, "Issue", message.Tertiary);
            return true;
        }

        private void Locate()
        {
            if (Time.unscaledTime < _nextProbeAt) return;
            _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds;
            if (_classifieds == null) _classifieds = FindFsm("JOBS/ADs", "Data");
            if (_factory == null) _factory = FindFsm("JOBS/FACTORY", "PlayerData");
            if (_magazine == null) _magazine = FindFsm("Systems/MarkettiMagazine", "Logic");
        }

        private static PlayMakerFSM? FindFsm(string path, string name)
        {
            try
            {
                var target = GameObject.Find(path);
                if (target == null) return null;
                foreach (var fsm in target.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == name) return fsm;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug($"WorldProgressSync: {path}/{name}: {e.Message}");
            }
            return null;
        }

        private static int ReadInt(PlayMakerFSM fsm, string name)
        {
            var value = fsm.FsmVariables.FindFsmInt(name);
            return value != null ? value.Value : 0;
        }

        private static byte ReadByte(PlayMakerFSM fsm, string name)
        {
            return (byte)Mathf.Clamp(ReadInt(fsm, name), 0, byte.MaxValue);
        }

        private static float ReadFloat(PlayMakerFSM fsm, string name)
        {
            var value = fsm.FsmVariables.FindFsmFloat(name);
            return value != null ? value.Value : 0f;
        }

        private static void WriteInt(PlayMakerFSM fsm, string name, int value)
        {
            var target = fsm.FsmVariables.FindFsmInt(name);
            if (target != null) target.Value = value;
        }

        private static void WriteFloat(PlayMakerFSM fsm, string name, float value)
        {
            var target = fsm.FsmVariables.FindFsmFloat(name);
            if (target != null && !float.IsNaN(value) && !float.IsInfinity(value)) target.Value = value;
        }
    }
}
