using System;
using System.Collections;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunTirePressureChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            var root = new GameObject("CORRIS"); var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            var child = new GameObject("TirePressure"); child.transform.SetParent(root.transform, false);
            // Keep the audited variables and state graph, with no native physics or enable actions.
            var data = NativeBagPartChecks.MakeFsm(child, NativeBagPartChecks.Find(ReadOccupancyRows("tire-pressure-probe.json"),
                "CORRIS/Simulation/Systems/TirePressure", "Data"));
            data.Fsm.Init(data); NativeBagPartChecks.Start(data);
            var pressure = data.FsmVariables.FindFsmFloat("Pressure"); var optimum = data.FsmVariables.FindFsmFloat("PressureOptimal");
            var enabled = data.FsmVariables.FindFsmBool("Enabled");
            var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
            Set(item, "Id", VehicleId); Set(item, "Path", "CORRIS"); Set(item, "Body", body); Set(item, "IsVehicle", true);
            items[VehicleId] = item;
            Action clean = () =>
            {
                reset(); pressure.Value = 190; optimum.Value = 190; enabled.Value = false;
                Set(item, "LocallyOwned", false); Set(item, "RemoteOwner", (byte)255);
                Call(vehicles, "ClearConditionStreams");
                Set(item, "HasSentCondition", false); Set(item, "NextConditionTickAt", 0f); Set(item, "NextConditionKeepAliveAt", 0f);
                SetProperty(session, "IsHost", true); SetProperty(session, "State", SessionState.Hosting); SetProperty(session, "LocalPlayerId", (byte)0);
                capture.Packets.Clear();
            };
            Func<VehicleCondition> snapshot = () => (VehicleCondition)Call(vehicles, "TryBuildConditionSnapshot", item, (byte)0)!;
            Func<byte, ushort, VehicleCondition> state = (value, seq) => new VehicleCondition { Availability = VehicleCondition.AvailableAll, VehicleId = VehicleId,
                OwnerPlayerId = 0, Sequence = seq, TirePressure = value };
            Action unchanged = () => Require(optimum.Value == 190 && !enabled.Value && data.ActiveStateName == "Probe idle",
                "Condition replay changed native optimum/enable state or dispatched a physics event.");
            try
            {
                check("tire pressure: actual condition discovery captures the native 190 default as wire 190", () =>
                {
                    clean(); var result = snapshot();
                    Require(ReferenceEquals(Get(item, "TirePressureVar"), pressure) && result.TirePressure == 190 && pressure.Value == 190,
                        "Native default was treated as 190 bar or capture mutated it."); unchanged();
                });
                check("tire pressure: ordinary owner publication keeps native units", () =>
                {
                    clean(); Set(item, "LocallyOwned", true); pressure.Value = 230;
                    Call(vehicles, "UpdateVehicleCondition", session); int seen = 0;
                    foreach (var p in capture.Packets) if (p.Message is VehicleCondition s && s.VehicleId == VehicleId)
                    { Require(s.TirePressure == 230 && p.Channel == Channel.ReliableOrdered, "Owner publication corrupted pressure."); seen++; }
                    Require(seen > 0 && pressure.Value == 230, "Owner condition was not sent."); unchanged();
                });
                check("tire pressure: observer packet writes native 190 and recaptures the same wire value", () =>
                {
                    clean(); SetProperty(session, "IsHost", false); SetProperty(session, "State", SessionState.Connected);
                    SetProperty(session, "LocalPlayerId", (byte)3); pressure.Value = 17;
                    Call(session, "OnPacketReceived", new PeerId(999), PacketCodec.Encode(state(190, 1)), Channel.ReliableOrdered);
                    Require(pressure.Value == 190 && snapshot().TirePressure == 190, "Observer pressure was divided by 100."); unchanged();
                });
                foreach (byte value in new byte[] { 0, 1, 127, 190, 230, 254, 255 })
                {
                    byte expected = value;
                    check("tire pressure: native apply and repeated recapture preserve wire " + expected, () =>
                    {
                        clean();
                        SetProperty(session, "IsHost", false); SetProperty(session, "State", SessionState.Connected);
                        SetProperty(session, "LocalPlayerId", (byte)3);
                        for (ushort n = 1; n <= 10; n++)
                        {
                            Call(vehicles, "ApplyVehicleCondition", state(expected, n));
                            Require(pressure.Value == expected && snapshot().TirePressure == expected, "Pressure changed units during recapture.");
                        }
                        unchanged();
                    });
                }
                check("tire pressure: native fractions round once and respect the existing wire range", () =>
                {
                    clean(); pressure.Value = 189.5f; Require(snapshot().TirePressure == 190 && pressure.Value == 189.5f, "Fractional capture lost rounding or mutated source.");
                    pressure.Value = 300; Require(snapshot().TirePressure == 255 && pressure.Value == 300, "Wire maximum changed native source."); unchanged();
                });
                check("tire pressure: locally owned cars ignore remote pressure writes", () =>
                {
                    clean(); Set(item, "LocallyOwned", true); pressure.Value = 230; Call(vehicles, "ApplyVehicleCondition", state(0, 1));
                    Require(pressure.Value == 230, "Remote state wrote onto the current owner."); unchanged();
                });
                check("tire pressure: condition snapshots leave live publication baseline unchanged", () =>
                {
                    clean(); Set(item, "LocallyOwned", true); Call(vehicles, "UpdateVehicleCondition", session); capture.Packets.Clear();
                    pressure.Value = 231; Require(snapshot().TirePressure == 231, "Snapshot pressure lost native units.");
                    Call(vehicles, "UpdateVehicleCondition", session); int seen = 0;
                    foreach (var p in capture.Packets) if (p.Message is VehicleCondition s && s.VehicleId == VehicleId)
                    { Require(s.TirePressure == 231, "Post-snapshot delta lost pressure."); seen++; }
                    Require(seen > 0, "Snapshot consumed the pressure delta."); unchanged();
                });
            }
            finally { items[VehicleId] = original; UnityEngine.Object.DestroyImmediate(root); reset(); }
        }
    }
}
