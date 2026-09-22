using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool WiringProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_WIRING_TEST") == "1";
        private const string WirePath = "CORRIS/Wiring/DatabaseWiring/WiringIgnitionFusebox";
        private static GameObject WireTool => FsmVariables.GlobalVariables.FindFsmGameObject("WiringTool").Value;
        private static PlayMakerFSM WireEnd(string end) => Find("CORRIS/Wiring/Triggers/IgnitionFusebox/" + end, "Assemble");
        private static float _wireNeedsAt;
        private static string? _wirePinnedEnd;
        private static bool _wireToolKinematic;
        private static Behaviour? _wireMotor;
        private static bool _wireMotorEnabled;
        private static void TickWiringFixture()
        {
            if (!WiringProbe || Application.loadedLevelName != "GAME") return;
            if (_wirePinnedEnd != null && Player != null)
            {
                if (_wireMotor != null) _wireMotor.enabled = false;
                var position = WireEnd(_wirePinnedEnd).transform.position;
                Player.position = position + new Vector3(0, -.7f, 1);
                var body = WireTool.GetComponent<Rigidbody>(); body.isKinematic = true;
                body.position = position; WireTool.transform.position = position;
            }
            if (Time.unscaledTime < _wireNeedsAt) return;
            RequirePersistenceSandbox(); _wireNeedsAt = Time.unscaledTime + 5;
            foreach (string name in new[] { "PlayerHunger", "PlayerThirst", "PlayerFatigue", "PlayerStress", "PlayerUrine", "PlayerTemp" })
            { var v = FsmVariables.GlobalVariables.FindFsmFloat(name); if (v != null) v.Value = name == "PlayerTemp" ? 50 : 0; }
        }
        private static bool WiringCommand(string[] args, List<string> rows)
        {
            if (!WiringProbe || !args[1].StartsWith("wire-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox(); var session = SessionManager.Instance!;
            var data = Find(WirePath, "Data");
            switch (args[1])
            {
                case "wire-view": return true;
                case "wire-needs":
                    foreach (string name in new[] { "PlayerHunger", "PlayerThirst", "PlayerFatigue", "PlayerStress", "PlayerUrine" })
                    { var v = FsmVariables.GlobalVariables.FindFsmFloat(name); if (v != null) v.Value = 0; }
                    return true;
                case "wire-fixture":
                    if (!session.IsHost && session.State != SessionState.Idle) throw new InvalidOperationException("Disconnected fixture only.");
                    Find("CORRIS/Assemblies/VINP_SteeringColumn", "Data").FsmVariables.FindFsmBool("Installed").Value = args[2] == "1";
                    data.FsmVariables.FindFsmBool("Installed").Value = args[3] == "1"; Enter(data, "Basic state"); return true;
                case "wire-destroy": data.SendEvent("DESTROY"); return true;
                case "wire-tool-tag":
                    if (session.IsHost || session.State != SessionState.Idle) throw new InvalidOperationException("Disconnected guest fixture only.");
                    foreach (var save in WireTool.GetComponents<PlayMakerFSM>())
                        if (save.FsmName == "Save") save.FsmVariables.FindFsmString("UniqueTag").Value = args[2] == "valid" ? "wiring mess(itemx)" : "wiring test invalid";
                    return true;
                case "wire-read":
                    var reader = Find("CORRIS/Simulation/Systems/Electrics", "Electrics");
                    // The incomplete car can leave Electrics waiting for other
                    // engine inputs. Inspect the source it is configured to consume.
                    var input = Call(Items, "ReadWiringInputs", (uint)5) as WiringState;
                    rows.Add("wire-read|" + (input != null && (input.Flags & WiringState.Installed) != 0));
                    rows.Add("wire-reader-ready|" + reader.Fsm.Started + "|" + reader.enabled); return true;
                case "wire-tool":
                    Player!.position = WireTool.transform.position + new Vector3(0, 0, 1);
                    var pickup = Find(Hand, "PickUp"); pickup.FsmVariables.FindFsmGameObject("PickedObject").Value = WireTool;
                    Enter(pickup, "Set pivot 2"); return true;
                case "wire-align":
                    if (_wirePinnedEnd == null)
                    {
                        _wireToolKinematic = WireTool.GetComponent<Rigidbody>().isKinematic;
                        _wireMotor = Player!.GetComponent("CharacterMotor") as Behaviour;
                        _wireMotorEnabled = _wireMotor != null && _wireMotor.enabled;
                    }
                    _wirePinnedEnd = args[2]; TickWiringFixture(); return true;
                case "wire-drop":
                    _wirePinnedEnd = null; WireTool.GetComponent<Rigidbody>().isKinematic = _wireToolKinematic;
                    if (_wireMotor != null) _wireMotor.enabled = _wireMotorEnabled;
                    Find(Hand, "PickUp").SendEvent("DROP_PART"); return true;
                case "wire-select":
                    var end = WireEnd(args[2]);
                    if (end.ActiveStateName != "Assemble") throw new InvalidOperationException("Endpoint is not natively selectable: " + end.ActiveStateName);
                    end.SendEvent("ASSEMBLE"); return true;
                case "wire-far": Player!.position += new Vector3(30, 0, 0); return true;
                case "wire-request":
                    if (session.IsHost) throw new InvalidOperationException("Guest request only.");
                    session.SendWorldMessage(new WiringInstallRequest { PlayerId = session.LocalPlayerId, SourceId = 5,
                        ExpectedRevision = uint.Parse(args[2]), Sequence = uint.Parse(args[3]), Token = ulong.Parse(args[4]) }, Channel.ReliableOrdered); return true;
                default: throw new InvalidOperationException("Unknown wiring command.");
            }
        }
        private static void WiringSnapshot(List<string> rows)
        {
            if (Application.loadedLevelName != "GAME") return;
            var data = Find(WirePath, "Data"); var session = SessionManager.Instance!;
            WiringState? state = null;
            if (session.IsHost)
                foreach (var value in (IEnumerable)Call(Items, "BuildWiringStates"))
                    if (value is WiringState wire && wire.SourceId == 5) state = wire;
            if (!session.IsHost) state = Call(Items, "ReadWiringInputs", (uint)5) as WiringState;
            rows.Add("wire|" + data.FsmVariables.FindFsmBool("Installed").Value + "|" + data.enabled + "|" + data.ActiveStateName
                + "|" + data.FsmVariables.FindFsmGameObject("WireMesh").Value.activeSelf + "|" + data.FsmVariables.FindFsmGameObject("WireTriggers").Value.activeSelf
                + "|" + state?.Revision + "|" + state?.Flags);
            var binding = Get(Items, "_wireConnection"); rows.Add("wire-binding|" + (binding == null ? "none" : Get(binding, "Failed")));
            rows.Add("wire-column|" + Find("CORRIS/Assemblies/VINP_SteeringColumn", "Data").FsmVariables.FindFsmBool("Installed").Value);
            foreach (string name in new[] { "Fusebox", "Ignition" })
            {
                var end = WireEnd(name);
                rows.Add("wire-end|" + name + "|" + end.gameObject.activeInHierarchy + "|" + end.ActiveStateName + "|" + Vector(end.transform.position)
                    + "|" + (end.transform.position - WireTool.transform.position).magnitude);
            }
            foreach (DictionaryEntry entry in (IDictionary)Get(Items, "_items"))
                if (Get(entry.Value, "Body") is Rigidbody body && body.gameObject == WireTool)
                    rows.Add("wire-tool|" + entry.Key + "|" + Get(entry.Value, "LocallyOwned") + "|" + Get(entry.Value, "RemoteOwner") + "|" + Vector(body.position));
            rows.Add("wire-player|" + (Player != null ? Vector(Player.position) : "none"));
            var client = Get(Items, "_wireInstallClient");
            rows.Add("wire-finish-count|" + Get(Items, "_wireFinishCount"));
            rows.Add("wire-pending|" + (Get(Items, "_wireInstalling") != null));
            rows.Add("wire-client-pending|" + (client != null && Get(client, "_pending") != null));
            var ledger = Get(Items, "_wireInstallLedger");
            foreach (DictionaryEntry entry in (IDictionary)Get(ledger, "_entries"))
            {
                var receipt = (WiringInstallReceipt)Get(entry.Value, "Receipt");
                rows.Add("wire-receipt|" + receipt.PlayerId + "|" + receipt.Token + "|" + receipt.Sequence + "|" + receipt.SourceId + "|" + receipt.Status);
            }
            rows.Add("wire-client|" + (client == null ? "none" : Get(client, "_token") + "|" + Get(client, "_sequence")));
            var electrics = Find("CORRIS/Simulation/Systems/Electrics", "Electrics");
            rows.Add("wire-electrics|" + electrics.FsmVariables.FindFsmBool("Installed3").Value);
        }
    }
}
