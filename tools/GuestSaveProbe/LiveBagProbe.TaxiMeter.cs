using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static PlayMakerFSM TaxiMeter => Find(TaxiRoot + "/TaxiFunctions/Tripmeter", "Function");
        private static PlayMakerFSM TaxiKnob => Find(TaxiRoot + "/TaxiFunctions/Tripmeter/KnobMode", "Knob");
        private static PlayMakerFSM TaxiButton => Find(TaxiRoot + "/TaxiFunctions/Tripmeter/ButtonMode", "Use");
        private static PlayMakerFSM? _taxiSpeedo;
        private static bool _taxiSpeedoEnabled;
        private static float _taxiSpeedoValue;
        private static object? TaxiItem
        {
            get
            {
                foreach (DictionaryEntry pair in (IDictionary)Get(Items, "_items"))
                    if (Get(pair.Value, "Body") is Rigidbody body && body.gameObject == TaxiCar) return pair.Value;
                return null;
            }
        }
        private static bool TaxiMeterCommand(string[] args)
        {
            if (!args[1].StartsWith("taxi-meter-", StringComparison.Ordinal)) return false;
            switch (args[1])
            {
                case "taxi-meter-near":
                    if (Player == null) throw new InvalidOperationException("Player not ready.");
                    Player.position = TaxiMeter.transform.position + Vector3.up; return true;
                case "taxi-meter-up": Enter(TaxiKnob, "Volume dec"); return true;
                case "taxi-meter-down": Enter(TaxiKnob, "Volume inc"); return true;
                case "taxi-meter-toggle": Enter(TaxiButton, "Flip Light"); return true;
                case "taxi-meter-reset": Enter(TaxiButton, "Reset tripmeter"); return true;
                case "taxi-meter-intent":
                    var session = SessionManager.Instance!;
                    session.SendWorldMessage(new TaxiMeterIntent { PlayerId = session.LocalPlayerId, Sequence = uint.Parse(args[2]),
                        ExpectedControlRevision = uint.Parse(args[3]), Action = (TaxiMeterAction)int.Parse(args[4]) }, Channel.ReliableOrdered); return true;
                case "taxi-meter-speed":
                    if (SessionManager.Instance!.IsHost) throw new InvalidOperationException("Guest gauge fixture only.");
                    if (_taxiSpeedo == null)
                    {
                        _taxiSpeedo = Find(TaxiRoot + "/LOD/Gauges", "Speedo");
                        _taxiSpeedoEnabled = _taxiSpeedo.enabled; _taxiSpeedoValue = _taxiSpeedo.FsmVariables.FindFsmFloat("Speed").Value;
                    }
                    _taxiSpeedo.enabled = false; _taxiSpeedo.FsmVariables.FindFsmFloat("Speed").Value = float.Parse(args[2], CultureInfo.InvariantCulture); return true;
                case "taxi-meter-stream":
                    var item = TaxiItem ?? throw new InvalidOperationException("Taxi body missing.");
                    item.GetType().GetField("NextVehicleStateAt", Members).SetValue(item, args[2] == "off" ? float.MaxValue : 0); return true;
                case "taxi-meter-speed-restore":
                    if (_taxiSpeedo != null) { _taxiSpeedo.FsmVariables.FindFsmFloat("Speed").Value = _taxiSpeedoValue; _taxiSpeedo.enabled = _taxiSpeedoEnabled; _taxiSpeedo = null; } return true;
                case "taxi-meter-binding":
                    var sync = Get(WorldSyncManager.Instance!, "_taxiJob"); Call(sync, "ClearMeter");
                    sync.GetType().GetField("_meterFailed", Members).SetValue(sync, args[2] == "off"); return true;
                default: throw new InvalidOperationException("Unknown taxi meter command.");
            }
        }
        private static void TaxiMeterSnapshot(List<string> rows)
        {
            var meter = TaxiMeter; var knob = TaxiKnob; var button = TaxiButton;
            var sync = Get(WorldSyncManager.Instance!, "_taxiJob"); var binding = Get(sync, "_meter");
            rows.Add("meter-binding|" + (binding != null) + "|" + Get(sync, "_meterFailed") + "|" + (binding == null ? "0" : Get(binding, "_controlRevision")));
            rows.Add("meter-control|" + knob.FsmVariables.FindFsmInt("RotationInt").Value + "|" + button.FsmVariables.FindFsmBool("TaxiLightOn").Value
                + "|" + meter.FsmVariables.FindFsmBool("On").Value + "|" + meter.FsmVariables.FindFsmBool("Off").Value + "|" + knob.ActiveStateName + "|" + button.ActiveStateName);
            rows.Add("meter-simulation|" + meter.enabled + "|" + meter.ActiveStateName
                + "|" + Find(TaxiRoot + "/TaxiFunctions/Tripmeter/Indicators/LCDdata", "GetData").enabled
                + "|" + Find(TaxiRoot + "/TaxiFunctions/PaymentTerminal/Payment", "Use").enabled);
            foreach (string name in new[] { "Price", "BaseCost", "OdoTrip", "OdoTotal", "IncomeTotal", "IncomeReceipts", "Odo100meters", "Interval", "MpS" })
                rows.Add("meter-value|" + name + "|" + meter.FsmVariables.FindFsmFloat(name).Value.ToString("R", CultureInfo.InvariantCulture));
            if (binding != null)
            {
                rows.Add("meter-lcd|" + ((TextMesh)Get(binding, "_modeText")).text + "|" + ((TextMesh)Get(binding, "_costText")).text);
                var pose = ((Transform)Get(binding, "_modeKnob")).localRotation;
                rows.Add("meter-visible|" + pose.x + "," + pose.y + "," + pose.z + "," + pose.w
                    + "|" + ((GameObject)Get(binding, "_indicators")).activeSelf + "|" + ((GameObject)Get(binding, "_lightIndicator")).activeSelf
                    + "|" + (((Renderer)Get(binding, "_dome")).sharedMaterial == (Material)Get(binding, "_lightOn")));

            }
            rows.Add("meter-native-read|" + meter.Fsm.GetState("State 1").Actions[0].GetType().Name);
            rows.Add("meter-native-control|" + knob.Fsm.GetState("Volume dec").Actions[0].GetType().Name);
            var remote = Get(sync, "_remoteMeter") as TaxiMeterState;
            if (remote != null) rows.Add("meter-remote|" + remote.Revision + "|" + remote.ControlRevision + "|" + remote.Flags + "|" + remote.Mode);
            if (TaxiItem is object item)
            {
                var accepted = Get(item, "AcceptedVehicleState") as VehicleState;
                rows.Add("meter-speed|" + (accepted == null ? "none" : accepted.SpeedTenthsKmh + "|" + accepted.OwnerPlayerId + "|" + accepted.Sequence)
                    + "|" + Get(item, "RemoteEngineUntil") + "|" + Time.unscaledTime);
            }
        }
    }
}
