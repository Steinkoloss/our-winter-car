using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveDriveProbe
    {
        private static bool RecoveryEnabled => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_RECOVERY_TEST") == "1";
        private static float _walkUntil;
        private static int _walkUpdates;

        private static void StartRecovery()
        {
            if (!RecoveryEnabled) return;
            var input = Assembly.Load("Assembly-UnityScript-firstpass").GetType("FPSInputController", true);
            new Harmony("com.ourwintercar.probe.recovery-walk").Patch(input.GetMethod("Update", Members),
                postfix: new HarmonyMethod(typeof(LiveDriveProbe).GetMethod("WalkInput", BindingFlags.Static | BindingFlags.NonPublic)));
        }

        private static void WalkInput(Component __instance)
        {
            if (Time.unscaledTime >= _walkUntil || Application.loadedLevelName != "GAME") return;
            var motor = Get(__instance, "motor");
            motor.GetType().GetField("inputMoveDirection", Members).SetValue(motor, __instance.transform.forward);
            _walkUpdates++;
        }

        private static void RequireRecovery(bool allowNativeFixture = false)
        {
            if (!RecoveryEnabled || !File.Exists(Path.Combine(Application.persistentDataPath, "wintermp-persistence-sandbox.txt"))
                || File.ReadAllText(Path.Combine(Application.persistentDataPath, "wintermp-persistence-sandbox.txt")) != "WinterMP persistence audit 20260913\n")
                throw new InvalidOperationException("Recovery requires a marked disposable profile.");
            if (SessionManager.Instance!.PermanentDeathEnabled
                || (!allowNativeFixture && FsmVariables.GlobalVariables.FindFsmBool("PlayerPermaDeath")?.Value == true))
                throw new InvalidOperationException("Recovery probe cannot exercise permadeath.");
        }

        private static bool RecoveryCommand(string[] args)
        {
            if (!RecoveryEnabled || !args[1].StartsWith("recovery-", StringComparison.Ordinal)) return false;
            RequireRecovery(args[1] == "recovery-normal-fixture");
            if (args[1] == "recovery-normal-fixture")
            {
                FsmVariables.GlobalVariables.FindFsmBool("PlayerPermaDeath").Value = false;
                return true;
            }
            if (args[1] == "recovery-quit") { Application.Quit(); return true; }
            if (args[1] == "recovery-walk")
            {
                if (Application.loadedLevelName != "GAME" || Passenger.IsLocalSeated || Player.parent != null
                    || Player.GetComponent<CharacterController>()?.enabled != true
                    || (bool)Get(Get(DeathSyncManager.Instance!, "_deathHook"), "_localDeathActive"))
                    throw new InvalidOperationException("Walking requires a recovered player on foot.");
                _walkUpdates = 0; _walkUntil = Time.unscaledTime + Number(args[2], 1, 5);
                return true;
            }
            if (args[1] == "recovery-load")
            {
                if (Application.loadedLevelName != "MainMenu") throw new InvalidOperationException("Main menu required.");
                using (var log = new BepInEx.Logging.ManualLogSource("Recovery probe"))
                    new WinterMP.FastBoot.MenuContinue().TryAdvance(log, false);
                return true;
            }
            var death = Find("Systems/Death", "Activate Dead Body");
            if (args[1] == "recovery-die")
            {
                if (!Passenger.IsLocalSeated || death.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("A live seated passenger with an inactive native death graph is required.");
                death.FsmVariables.FindFsmBool("Fatigue").Value = true;
                death.gameObject.SetActive(true);
                return true;
            }
            if (args[1] == "recovery-continue")
            {
                if (death.ActiveStateName != "State 2") throw new InvalidOperationException("Native newspaper required.");
                death.SendEvent("FINISHED");
                return true;
            }
            throw new InvalidOperationException("Unknown recovery command.");
        }

        private static void RecoverySnapshot(List<string> rows)
        {
            if (!RecoveryEnabled) return;
            rows.Add("level|" + Application.loadedLevelName);
            var session = SessionManager.Instance!;
            rows.Add("recovery-session|" + session.State + "|" + session.PlayerCount + "|" + session.PermanentDeathEnabled);
            rows.Add("native-permadeath|" + FsmVariables.GlobalVariables.FindFsmBool("PlayerPermaDeath")?.Value);
            rows.Add("walking-input|" + _walkUpdates + "|" + (Time.unscaledTime < _walkUntil));
            var manager = DeathSyncManager.Instance!;
            rows.Add("local-death|" + Get(Get(manager, "_deathHook"), "_localDeathActive") + "|" + Get(manager, "_respawnWatchScheduled"));
            foreach (var peer in session.Players) rows.Add("peer-death|" + peer.PlayerId + "|" + peer.IsDead);
            if (Application.loadedLevelName != "GAME") return;
            var death = Find("Systems/Death", "Activate Dead Body");
            rows.Add("native-death|" + death.ActiveStateName + "|" + death.enabled + "|" + death.gameObject.activeInHierarchy);
            rows.Add("movement|" + Player.gameObject.activeInHierarchy + "|" + (Player.GetComponent("FPSInputController") != null)
                + "|" + (Player.GetComponent("CharacterMotor") != null) + "|" + (Player.GetComponent<CharacterController>() != null));
        }
    }
}
