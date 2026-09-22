using System;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool _taxiMenuArmed;

        internal void TaxiMenuLoaded()
        {
            if (!_taxiMenuArmed || Application.loadedLevelName != "MainMenu") return;
            _taxiMenuArmed = false;
            string result;
            try
            {
                var world = WorldSyncManager.Instance!; var sync = Get(world, "_taxiJob");
                var meter = Get(sync, "_meter");
                var previous = Get(sync, "_remoteMeter") as TaxiMeterState;
                if (meter == null || previous == null || (Transform)Get(meter, "_modeKnob") != null)
                    throw new InvalidOperationException("Expected cached meter with destroyed native knob before world cleanup.");
                var late = new TaxiMeterState { Revision = unchecked(previous.Revision + 1), ControlRevision = previous.ControlRevision,
                    Flags = previous.Flags, Mode = previous.Mode, Values = (float[])previous.Values.Clone(),
                    Display = previous.Display, ModeDisplay = previous.ModeDisplay, KnobRotation = previous.KnobRotation };
                world.OnTaxiMeterState(late);
                if ((bool)Get(sync, "_meterFailed") || !ReferenceEquals(previous, Get(sync, "_remoteMeter")))
                    throw new InvalidOperationException("Late menu packet reached the destroyed meter binding.");
                result = "PASS|newer meter packet ignored after native destruction, before world cleanup";
            }
            catch (Exception error) { result = "FAIL|" + error; }
            File.WriteAllText(Path.Combine(_output, _role + "-taxi-menu.txt"), result);
        }

        private static bool TaxiJourneyCommand(string[] args)
        {
            if (!args[1].StartsWith("taxi-journey-", StringComparison.Ordinal)) return false;
            if (args[1] == "taxi-journey-arm-menu")
            {
                if (SessionManager.Instance!.IsHost || Application.loadedLevelName != "GAME")
                    throw new InvalidOperationException("Arm menu packet check on the connected guest.");
                _taxiMenuArmed = true; return true;
            }
            if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host journey fixture only.");
            switch (args[1])
            {
                case "taxi-journey-prepare":
                    if (TaxiMeter.FsmVariables.FindFsmFloat("IncomeTotal").Value != 0
                        || TaxiMeter.FsmVariables.FindFsmFloat("IncomeReceipts").Value != 0)
                        throw new InvalidOperationException("Journey must start with an empty native earnings ledger.");
                    // Select the luggage/receipt branches before the actual customer
                    // lifecycle runs; do not inject a later customer or its earnings.
                    var amounts = LuggageList("Amounts"); amounts.Clear(); amounts.Add(1);
                    var choices = LuggageList("Luggage"); choices.Clear(); choices.Add(LuggageBody(0).gameObject);
                    foreach (var action in TaxiWalker.Fsm.GetState("State 5").Actions)
                        if (action.GetType().Name == "SendRandomEvent")
                        {
                            var events = (FsmEvent[])action.GetType().GetField("events").GetValue(action);
                            var weights = (FsmFloat[])action.GetType().GetField("weights").GetValue(action);
                            bool receipt = false;
                            for (int i = 0; i < events.Length; i++)
                            { bool chosen = events[i].Name == "PROCEED"; weights[i].Value = chosen ? 1 : 0; receipt |= chosen; }
                            if (!receipt) throw new InvalidOperationException("Native receipt choice missing.");
                        }
                    var odo = Find(TaxiRoot + "/Functions/Dashboard/Odometer", "Data");
                    TaxiPayments.FsmVariables.FindFsmFloat("CarOdoOldF").Value = odo.FsmVariables.FindFsmInt("OdometerReading").Value * 10f
                        + odo.FsmVariables.FindFsmFloat("Odo").Value / 1000f;
                    return true;
                case "taxi-journey-route":
                    if (TaxiWalker.ActiveStateName != "Call") throw new InvalidOperationException("Choose short route while the actual customer calls.");
                    TaxiWalker.FsmVariables.FindFsmGameObject("PickupPoint").Value.transform.position =
                        TaxiWalker.FsmVariables.FindFsmGameObject("CarGetInPivot").Value.transform.position + TaxiCar.transform.forward * 1.5f;
                    TaxiWalker.FsmVariables.FindFsmGameObject("DropOffPoint").Value.transform.position =
                        TaxiCar.transform.position + TaxiCar.transform.forward * 40;
                    return true;
                case "taxi-journey-payday":
                    if (TaxiMeter.FsmVariables.FindFsmFloat("IncomeTotal").Value <= 0
                        || TaxiMeter.FsmVariables.FindFsmFloat("IncomeReceipts").Value <= 0
                        || !TaxiMeter.FsmVariables.FindFsmBool("Off").Value)
                        throw new InvalidOperationException("Finish and receipt the actual fare, then switch the meter off.");
                    TaxiPayments.FsmVariables.FindFsmInt("PaymentDay").Value = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay").Value;
                    Enter(TaxiPayments, "Add day"); return true;
                default: throw new InvalidOperationException("Unknown journey fixture.");
            }
        }
    }
}
