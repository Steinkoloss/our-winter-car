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
    internal sealed partial class FsmWorldSync
    {
        internal void RunDoorTest()
        {
            if (_bridge.DoorTestStep >= 2 || _bridge.FirstDoorRegisteredAt < 0f) return;

            float dueAt = _bridge.FirstDoorRegisteredAt + _bridge.DoorTestDelay + (_bridge.DoorTestStep == 0 ? 0f : 4f);
            if (Time.unscaledTime < dueAt) return;

            // Prefer the home WC door; otherwise the lowest-id door so both
            // machines deterministically pick the same one.
            SyncedDoor? target = null;
            uint targetId = 0;
            foreach (var pair in _doors)
            {
                var door = pair.Value;
                if (door.Fsm == null || Array.IndexOf(door.SyncedStates, "Open door") < 0) continue;
                if (door.Path.IndexOf("HOMENEW") >= 0 && door.Path.IndexOf("DoorWC") >= 0)
                {
                    target = door;
                    break;
                }
                if (target == null || pair.Key < targetId)
                {
                    target = door;
                    targetId = pair.Key;
                }
            }

            if (target == null) return;

            string state = _bridge.DoorTestStep == 0 ? "Open door" : "Close door";
            WinterMPPlugin.Log.LogInfo($"WorldSync: [door test] firing '{state}' on {target.Path}.");
            // Local entry through the MP transition — the state-enter hook then
            // broadcasts it like any player-triggered door.
            FsmHook.FireRemoteEntry(target.Fsm, state);
            _bridge.DoorTestStep++;
        }

    }
}
