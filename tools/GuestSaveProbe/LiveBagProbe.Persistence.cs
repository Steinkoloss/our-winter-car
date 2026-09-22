using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.UI;
using WinterMP.Net.Messages;
using WinterMP.Net.Transport;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private const string SaveButton = "COTTAGE/Stuff/SHITHOUSE/SavePivot/SAVEGAME";
        private static bool Persistence;
        private const int GuestPort = 38949;

        private static void StartPersistence()
        {
            if (Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_PERSIST_TEST") != "1") return;
            RequirePersistenceSandbox();
            Persistence = true;
            // UDP normally keys peers by a fresh ephemeral port. This isolated test
            // uses one stable endpoint to exercise the returning-profile path.
            new Harmony("com.ourwintercar.probe.persistence").Patch(typeof(UdpTransport).GetMethod("CreateClient"),
                prefix: new HarmonyMethod(typeof(LiveBagProbe).GetMethod("StableGuest", Members)));
        }

        private static void RequirePersistenceSandbox()
        {
            string marker = Path.Combine(Application.persistentDataPath, "wintermp-persistence-sandbox.txt");
            if (!File.Exists(marker) || File.ReadAllText(marker) != "WinterMP persistence audit 20260913\n")
                throw new InvalidOperationException("Persistence probe requires its own disposable save profile.");
        }

        private static bool StableGuest(IPEndPoint host, ref UdpTransport __result)
        {
            RequirePersistenceSandbox();
            __result = (UdpTransport)Activator.CreateInstance(typeof(UdpTransport), Members, null,
                new object[] { false, GuestPort, host }, CultureInfo.InvariantCulture);
            return false;
        }

        private static bool PersistenceCommand(string[] args, List<string> rows)
        {
            if (!Persistence) return false;
            RequirePersistenceSandbox();
            switch (args[1])
            {
                case "join": SessionManager.Instance!.StartJoinLocal("127.0.0.1", UdpTransport.DefaultPort); return true;
                case "disconnect": SessionManager.Instance!.Shutdown("Persistence test disconnect"); return true;
                case "refresh-world":
                    SessionManager.Instance!.SendWorldMessage(new WorldSnapshotRequest {
                        IdHash = WinterMP.Core.Sync.WorldSyncManager.Instance!.IdHash }, WinterMP.Net.Channel.ReliableOrdered); return true;
                case "quit": Application.Quit(); return true;
                case "save-description": Describe(Find(SaveButton, "Button"), rows); return true;
                case "save": Enter(Find(SaveButton, "Button"), "Mute audio"); return true;
                case "spawn":
                    var prompt = GuestSpawnPrompt.Instance ?? throw new InvalidOperationException("Spawn prompt missing.");
                    if (!prompt.IsBlockingInput) throw new InvalidOperationException("No returning guest choice.");
                    Call(prompt, args[2] == "last" ? "ChooseLast" : "ChooseHost"); return true;
                case "needs":
                    if (SessionManager.Instance!.IsHost) throw new InvalidOperationException("Guest need fixture only.");
                    var names = new[] { "PlayerHunger", "PlayerFatigue", "PlayerThirst", "PlayerUrine" };
                    for (int i = 0; i < names.Length; i++)
                    {
                        float value = float.Parse(args[i + 2], CultureInfo.InvariantCulture);
                        if (float.IsNaN(value) || value < 0 || value > 80) throw new InvalidOperationException("Invalid fixture need.");
                        var variable = FsmVariables.GlobalVariables.FindFsmFloat(names[i]) ?? throw new InvalidOperationException("Missing " + names[i]);
                        variable.Value = value;
                    }
                    return true;
                default: return false;
            }
        }

        private static void PersistenceSnapshot(List<string> rows)
        {
            if (!Persistence) return;
            rows.Add("level|" + Application.loadedLevelName);
            rows.Add("save-path|" + Application.persistentDataPath);
            foreach (var peer in SessionManager.Instance!.Players)
                rows.Add("peer-profile|" + peer.PlayerId + "|" + peer.SteamId + "|" + peer.ReturningGuest);
            var prompt = GuestSpawnPrompt.Instance;
            var offer = prompt != null ? Get(prompt, "_offer") as GuestSpawn : null;
            rows.Add("offer|" + (offer == null ? "none" : offer.Flags + "|" + offer.Hunger + "|" + offer.Fatigue
                + "|" + offer.Thirst + "|" + offer.Urine + "|" + offer.LastPosition.X + "," + offer.LastPosition.Y + "," + offer.LastPosition.Z));
            foreach (string name in new[] { "PlayerHunger", "PlayerFatigue", "PlayerThirst", "PlayerUrine", "PlayerTemp", "PlayerDirtiness" })
                rows.Add("need|" + name + "|" + FsmVariables.GlobalVariables.FindFsmFloat(name)?.Value.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
