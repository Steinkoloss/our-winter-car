using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Host source of truth for the inspection office's persistent result record.
    /// The inspection itself is evaluated against the host car; peers only mirror
    /// the resulting stamp, renewal and individual failure checkmarks.
    /// </summary>
    internal sealed class InspectionSync
    {
        private const float ScanIntervalSeconds = 5f;
        private const float SendIntervalSeconds = 4f;
        // 38 wire slots; ShockRL/ShockRR don't exist on the current build's Results FSM
        // (dump-23268598: only front shocks) so those two bits stay 0 on every peer.
        // Kept anyway: dropping them would reshuffle bit assignments, and a future game
        // build that adds rear-shock checks binds into the existing slots for free.
        private static readonly string[] ChecklistNames =
        {
            "Body", "Brakes", "Chassis", "Dashboard", "Emissions", "Engine", "Exhaust", "FuelLine",
            "FuelTank", "Handbrake", "HeadlightLeft", "HeadlightRight", "InspectionPass", "Leima",
            "MuseumOrdered", "RearLightLeft", "RearLightRight", "SeatDriver", "ShockFL", "ShockFR",
            "ShockRL", "ShockRR", "SteeringColumn", "SteeringRack", "SteeringRodLeft", "SteeringRodRight",
            "SteeringWheel", "SuspensionFL", "SuspensionFR", "SuspensionRL", "SuspensionRR", "Tires",
            "TrailArmRL", "TrailArmRR", "Transmission", "WarningTriangle", "WheelAlign", "Windshield",
        };

        private PlayMakerFSM? _inspectFsm;
        private PlayMakerFSM? _resultsFsm;
        private PlayMakerFSM? _standardPlateFsm;
        private PlayMakerFSM? _museumPlateFsm;
        private FsmBool? _carInspected;
        private FsmBool? _stampOnPaper;
        private FsmBool? _museumRegistered;
        private FsmInt? _nextInspectionDay;
        private FsmInt? _inspectionIntervalDays;
        private FsmInt? _inspectionIntervalLetter;
        private FsmString? _standardPlate;
        private FsmString? _museumPlate;
        private FsmGameObject? _standardPlate1;
        private FsmGameObject? _standardPlate2;
        private FsmGameObject? _museumPlate1;
        private FsmGameObject? _museumPlate2;
        private FsmBool?[] _checklist = new FsmBool?[ChecklistNames.Length];
        private InspectionState? _pending;
        private float _nextScanAt;
        private float _nextSendAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private byte _lastFlags;
        private int _lastNextDay;
        private int _lastIntervalDays;
        private int _lastIntervalLetter;
        private uint _lastChecklistLow;
        private uint _lastChecklistHigh;
        private byte _lastPlateFlags;
        private string _lastStandardPlate = string.Empty;
        private string _lastMuseumPlate = string.Empty;

        public void Clear()
        {
            _inspectFsm = null;
            _resultsFsm = null;
            _standardPlateFsm = null;
            _museumPlateFsm = null;
            _carInspected = null;
            _stampOnPaper = null;
            _museumRegistered = null;
            _nextInspectionDay = null;
            _inspectionIntervalDays = null;
            _inspectionIntervalLetter = null;
            _standardPlate = null;
            _museumPlate = null;
            _standardPlate1 = null;
            _standardPlate2 = null;
            _museumPlate1 = null;
            _museumPlate2 = null;
            _checklist = new FsmBool?[ChecklistNames.Length];
            _pending = null;
            _nextScanAt = 0f;
            _nextSendAt = 0f;
            _outSequence = 0;
            _lastRemoteSequence = 0;
            _hasLast = false;
        }

        public void Update(SessionManager session)
        {
            Scan();
            ApplyPending();
            if (!session.IsHost || session.PlayerCount == 0 || Time.unscaledTime < _nextSendAt)
                return;

            _nextSendAt = Time.unscaledTime + SendIntervalSeconds;
            var state = BuildState(changedOnly: true);
            if (state != null)
                session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        public InspectionState? BuildSnapshot()
        {
            Scan(force: true);
            // Targeted join send — must not advance the periodic change baseline.
            return BuildState(changedOnly: false, advanceBaseline: false);
        }

        public void Apply(InspectionState message)
        {
            Scan();
            if (_inspectFsm == null || _resultsFsm == null
                || _standardPlateFsm == null || _museumPlateFsm == null)
            {
                _pending = message;
                return;
            }

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            Set(_carInspected, (message.Flags & InspectionState.FlagCarInspected) != 0);
            Set(_stampOnPaper, (message.Flags & InspectionState.FlagStampOnPaper) != 0);
            Set(_museumRegistered, (message.Flags & InspectionState.FlagMuseumRegistered) != 0);
            Set(_nextInspectionDay, message.NextInspectionDay);
            Set(_inspectionIntervalDays, message.InspectionIntervalDays);
            Set(_inspectionIntervalLetter, message.InspectionIntervalLetter);
            for (int i = 0; i < _checklist.Length; i++)
                Set(_checklist[i], IsChecklistSet(message.ChecklistLow, message.ChecklistHigh, i));
            Set(_standardPlate, message.StandardPlate);
            Set(_museumPlate, message.MuseumPlate);
            SetActive(_standardPlate1, (message.PlateFlags & 1) != 0);
            SetActive(_standardPlate2, (message.PlateFlags & 2) != 0);
            SetActive(_museumPlate1, (message.PlateFlags & 4) != 0);
            SetActive(_museumPlate2, (message.PlateFlags & 8) != 0);
        }

        private InspectionState? BuildState(bool changedOnly, bool advanceBaseline = true)
        {
            if (_inspectFsm == null || _resultsFsm == null
                || _standardPlateFsm == null || _museumPlateFsm == null) return null;

            byte flags = ReadFlags();
            int nextDay = Read(_nextInspectionDay);
            int intervalDays = Read(_inspectionIntervalDays);
            int intervalLetter = Read(_inspectionIntervalLetter);
            BuildChecklist(out uint checklistLow, out uint checklistHigh);
            byte plateFlags = ReadPlateFlags();
            string standardPlate = Read(_standardPlate);
            string museumPlate = Read(_museumPlate);
            bool changed = !_hasLast
                || flags != _lastFlags
                || nextDay != _lastNextDay
                || intervalDays != _lastIntervalDays
                || intervalLetter != _lastIntervalLetter
                || checklistLow != _lastChecklistLow
                || checklistHigh != _lastChecklistHigh
                || plateFlags != _lastPlateFlags
                || standardPlate != _lastStandardPlate
                || museumPlate != _lastMuseumPlate;
            if (changedOnly && !changed) return null;

            // Only broadcast-to-all paths own the change baseline; the join snapshot
            // (advanceBaseline:false) must not suppress a pending delta to connected guests.
            if (advanceBaseline)
            {
                _hasLast = true;
                _lastFlags = flags;
                _lastNextDay = nextDay;
                _lastIntervalDays = intervalDays;
                _lastIntervalLetter = intervalLetter;
                _lastChecklistLow = checklistLow;
                _lastChecklistHigh = checklistHigh;
                _lastPlateFlags = plateFlags;
                _lastStandardPlate = standardPlate;
                _lastMuseumPlate = museumPlate;
            }
            return new InspectionState
            {
                Flags = flags,
                Sequence = ++_outSequence,
                NextInspectionDay = nextDay,
                InspectionIntervalDays = intervalDays,
                InspectionIntervalLetter = intervalLetter,
                ChecklistLow = checklistLow,
                ChecklistHigh = checklistHigh,
                PlateFlags = plateFlags,
                StandardPlate = standardPlate,
                MuseumPlate = museumPlate,
            };
        }

        private void Scan(bool force = false)
        {
            if (!force && Time.unscaledTime < _nextScanAt) return;
            _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
            if (_inspectFsm != null && _resultsFsm != null
                && _standardPlateFsm != null && _museumPlateFsm != null) return;

            var fsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM));
            foreach (var obj in fsms)
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                try
                {
                    string path = ScenePath.Of(fsm.transform);
                    if (path == "INSPECTION/Functions/InspectionStandard" && fsm.FsmName == "Inspect" && _inspectFsm == null)
                        BindInspect(fsm);
                    else if (path == "INSPECTION/Functions/InspectionStandard" && fsm.FsmName == "Results" && _resultsFsm == null)
                        BindResults(fsm);
                    else if (path == "INSPECTION/Regplates" && fsm.FsmName == "Generate" && _standardPlateFsm == null)
                        BindPlateGenerator(fsm, museum: false);
                    else if (path == "INSPECTION/RegplatesMuseum" && fsm.FsmName == "Generate" && _museumPlateFsm == null)
                        BindPlateGenerator(fsm, museum: true);
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogDebug($"InspectionSync: skipped FSM: {e.Message}");
                }
            }
        }

        private void BindInspect(PlayMakerFSM fsm)
        {
            _inspectFsm = fsm;
            _carInspected = fsm.FsmVariables.FindFsmBool("_CarInspected");
            _stampOnPaper = fsm.FsmVariables.FindFsmBool("_LeimaPaperissa");
            _museumRegistered = fsm.FsmVariables.FindFsmBool("_MuseumRegistered");
            _nextInspectionDay = fsm.FsmVariables.FindFsmInt("_NextInspectionDay");
            _inspectionIntervalDays = fsm.FsmVariables.FindFsmInt("_InspectionIntervalDays");
            _inspectionIntervalLetter = fsm.FsmVariables.FindFsmInt("_InspectionIntervalLetter");
            WinterMPPlugin.Log.LogInfo("InspectionSync: registered inspection record.");
        }

        private void BindResults(PlayMakerFSM fsm)
        {
            _resultsFsm = fsm;
            for (int i = 0; i < ChecklistNames.Length; i++)
                _checklist[i] = fsm.FsmVariables.FindFsmBool(ChecklistNames[i]);
            WinterMPPlugin.Log.LogInfo("InspectionSync: registered inspection checklist.");
        }

        private void BindPlateGenerator(PlayMakerFSM fsm, bool museum)
        {
            var plate = fsm.FsmVariables.FindFsmString("Plate");
            var plate1 = fsm.FsmVariables.GetFsmGameObject("Plate1");
            var plate2 = fsm.FsmVariables.GetFsmGameObject("Plate2");
            if (museum)
            {
                _museumPlateFsm = fsm;
                _museumPlate = plate;
                _museumPlate1 = plate1;
                _museumPlate2 = plate2;
            }
            else
            {
                _standardPlateFsm = fsm;
                _standardPlate = plate;
                _standardPlate1 = plate1;
                _standardPlate2 = plate2;
            }
            WinterMPPlugin.Log.LogInfo("InspectionSync: registered " + (museum ? "museum" : "standard") + " plates.");
        }

        private void ApplyPending()
        {
            if (_pending == null || _inspectFsm == null || _resultsFsm == null) return;
            var pending = _pending;
            _pending = null;
            Apply(pending);
        }

        private byte ReadFlags()
        {
            byte flags = 0;
            if (Read(_carInspected)) flags |= InspectionState.FlagCarInspected;
            if (Read(_stampOnPaper)) flags |= InspectionState.FlagStampOnPaper;
            if (Read(_museumRegistered)) flags |= InspectionState.FlagMuseumRegistered;
            if (ReadChecklist(12)) flags |= InspectionState.FlagPassed;
            if (ReadChecklist(13)) flags |= InspectionState.FlagStampIssued;
            if (ReadChecklist(14)) flags |= InspectionState.FlagMuseumOrdered;
            return flags;
        }

        private byte ReadPlateFlags()
        {
            byte flags = 0;
            if (IsActive(_standardPlate1)) flags |= 1;
            if (IsActive(_standardPlate2)) flags |= 2;
            if (IsActive(_museumPlate1)) flags |= 4;
            if (IsActive(_museumPlate2)) flags |= 8;
            return flags;
        }

        private void BuildChecklist(out uint low, out uint high)
        {
            low = 0;
            high = 0;
            for (int i = 0; i < _checklist.Length; i++)
            {
                if (!ReadChecklist(i)) continue;
                if (i < 32) low |= 1u << i;
                else high |= 1u << (i - 32);
            }
        }

        private bool ReadChecklist(int index) => index >= 0 && index < _checklist.Length && Read(_checklist[index]);
        private static bool IsChecklistSet(uint low, uint high, int index)
            => index < 32 ? (low & (1u << index)) != 0 : (high & (1u << (index - 32))) != 0;
        private static bool Read(FsmBool? value) => value != null && value.Value;
        private static int Read(FsmInt? value) => value != null ? value.Value : 0;
        private static string Read(FsmString? value) => value != null ? value.Value ?? string.Empty : string.Empty;
        private static void Set(FsmBool? value, bool result) { if (value != null) value.Value = result; }
        private static void Set(FsmInt? value, int result) { if (value != null) value.Value = result; }
        private static void Set(FsmString? value, string result) { if (value != null) value.Value = result ?? string.Empty; }

        private static bool IsActive(FsmGameObject? value)
        {
            return value != null && value.Value != null && value.Value.activeSelf;
        }

        private static void SetActive(FsmGameObject? value, bool active)
        {
            if (value == null || value.Value == null) return;
            try { value.Value.SetActive(active); }
            catch (Exception e) { WinterMPPlugin.Log.LogDebug("InspectionSync: plate activation failed: " + e.Message); }
        }
    }
}
