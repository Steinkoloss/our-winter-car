using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool _paneTracing;
        private static readonly BepInEx.Logging.ManualLogSource PaneLog = BepInEx.Logging.Logger.CreateLogSource("V11 pane diagnostic");
        private static int _paneGlassCalls, _paneEffectCalls;
        private static float _paneHeatDelta;
        private static string _paneContext = "none", _paneDecision = "none";
        private static ScraperAction? _paneLastStroke;
        private static ScraperAction? _paneOriginalStroke;
        private static bool _paneWrongPane;
        private static SessionManager? _paneMutationSession;
        private static string _paneStrokePackets = "none", _paneMutationPackets = "none";
        private static bool PaneProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_V11_PANE_TEST") == "1";
        private static bool PaneTraceAllowed => PaneProbe
            && Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_BAG_TEST") == "1"
            && File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "wintermp-live-bag-sandbox.txt"))
            && File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "wintermp-v11-pane-sandbox.txt"));

        private static void ArmPaneWrongPane()
        {
            var session = SessionManager.Instance;
            if (!PaneTraceAllowed || session == null || session.IsHost || session.State != SessionState.Connected)
            {
                ClearPaneMutation();
                throw new InvalidOperationException("Wrong-pane diagnostic requires opt-in, both sandbox markers and a connected guest.");
            }
            if (_paneWrongPane) throw new InvalidOperationException("One wrong-pane stroke is already armed.");
            try
            {
                StartPaneTrace();
                _paneMutationSession = session;
                _paneWrongPane = true;
            }
            catch { ClearPaneMutation(); throw; }
        }
        private static void ClearPaneMutation()
        {
            _paneWrongPane = false; _paneMutationSession = null;
            _paneLastStroke = _paneOriginalStroke = null;
            _paneStrokePackets = _paneMutationPackets = "none";
        }
        private static void TracePaneFailure(Exception? __exception)
        {
            if (__exception != null) ClearPaneMutation();
        }

        private static void StartPaneTrace()
        {
            if (!PaneTraceAllowed) { ClearPaneMutation(); throw new InvalidOperationException("Pane tracing requires opt-in and sandbox markers."); }
            if (_paneTracing) return;
            var type = PaneBridge.GetType();
            var harmony = new Harmony("com.ourwintercar.probe.pane-observe");
            harmony.Patch(type.GetMethod("ReadContext", Members), postfix: new HarmonyMethod(typeof(LiveBagProbe).GetMethod("TracePaneContext", Members)));
            harmony.Patch(type.GetMethod("ApplyWindshieldDelta", Members), postfix: new HarmonyMethod(typeof(LiveBagProbe).GetMethod("TracePaneGlass", Members)));
            harmony.Patch(type.GetMethod("ApplyPersonalEffects", Members),
                prefix: new HarmonyMethod(typeof(LiveBagProbe).GetMethod("BeforePaneEffects", Members)),
                postfix: new HarmonyMethod(typeof(LiveBagProbe).GetMethod("AfterPaneEffects", Members)));
            harmony.Patch(typeof(SessionManager).GetMethod("SendWorldMessage", Members),
                prefix: new HarmonyMethod(typeof(LiveBagProbe).GetMethod("TracePaneMessage", Members)),
                finalizer: new HarmonyMethod(typeof(LiveBagProbe).GetMethod("TracePaneFailure", Members)));
            foreach (string method in new[] { "ResetSession", "Fail" })
                harmony.Patch(type.GetMethod(method, Members), prefix: new HarmonyMethod(typeof(LiveBagProbe).GetMethod("ClearPaneMutation", Members)));
            harmony.Patch(typeof(Plugin).GetMethod("OnDestroy", Members),
                prefix: new HarmonyMethod(typeof(LiveBagProbe).GetMethod("ClearPaneMutation", Members)));
            foreach (string method in new[] { "Tick", "Execute", "Snapshot" })
                harmony.Patch(typeof(LiveBagProbe).GetMethod(method, Members),
                    finalizer: new HarmonyMethod(typeof(LiveBagProbe).GetMethod("TracePaneFailure", Members)));
            _paneTracing = true;
        }
        private static void TracePaneContext(PaneScrapeHostContext __result)
        {
            var c = __result;
            _paneContext = c.ActorPresent + "|" + c.ActorAlive + "|" + c.ActorOutside + "|" + c.PoseAgeSeconds.ToString("R", CultureInfo.InvariantCulture)
                + "|" + c.EquipmentActor + "|" + c.IsIceScraper + "|" + c.Unobstructed + "|" + c.ContactDistance.ToString("R", CultureInfo.InvariantCulture)
                + "|" + c.VehicleParked;
        }
        private static void TracePaneGlass() { _paneGlassCalls++; }
        private static float PanePlayerTemp() => (FsmVariables.GlobalVariables.FindFsmFloat("PlayerTemp")
            ?? throw new InvalidOperationException("PlayerTemp missing during pane trace.")).Value;
        private static void BeforePaneEffects(out float __state) { __state = PanePlayerTemp(); }
        private static void AfterPaneEffects(float __state)
        {
            _paneEffectCalls++;
            _paneHeatDelta = PanePlayerTemp() - __state;
        }
        private static void TracePaneMessage(ref IMessage message)
        {
            try
            {
                var session = SessionManager.Instance;
                if (!PaneTraceAllowed) { ClearPaneMutation(); return; }
                if (_paneWrongPane && (session != _paneMutationSession || session == null
                    || session.IsHost || session.State != SessionState.Connected)) ClearPaneMutation();
                var request = message as ScraperAction;
                if (request != null && request.Operation == ScraperOperation.Stroke && session != null
                    && !session.IsHost && session.State == SessionState.Connected && request.Actor == session.LocalPlayerId)
                {
                    byte[] original = PacketCodec.Encode(request);
                    var transmitted = (ScraperAction)PacketCodec.Decode(original);
                    if (_paneLastStroke != null && request.Epoch != _paneLastStroke.Epoch) ClearPaneMutation();
                    bool fresh = _paneLastStroke == null || request.Sequence > _paneLastStroke.Sequence;
                    bool mutate = _paneWrongPane && fresh;
                    if (mutate)
                    {
                        // Never edit the production request object or generate a sequence/lease.
                        // Replayed strokes and equipment traffic cannot consume the one shot.
                        _paneWrongPane = false; _paneMutationSession = null;
                        if (request.Pane != PaneScrapeIntent.Windshield)
                            throw new InvalidOperationException("Expected an outgoing production windshield stroke.");
                        transmitted.Pane = 2;
                    }
                    string packets = Convert.ToBase64String(original) + "|" + Convert.ToBase64String(PacketCodec.Encode(transmitted));
                    PaneLog.LogInfo("pane-stroke-packets|" + packets);
                    _paneStrokePackets = packets;
                    if (mutate) _paneMutationPackets = packets;
                    if (fresh)
                    {
                        _paneOriginalStroke = (ScraperAction)PacketCodec.Decode(original);
                        _paneLastStroke = transmitted;
                    }
                    if (mutate) message = transmitted;
                }
                var update = message as PaneScrapeUpdate;
                if (update != null && update.IsDecision)
                    _paneDecision = update.Actor + "|" + update.Sequence + "|" + update.HighWater + "|" + update.Status + "|" + update.Revision;
            }
            catch { ClearPaneMutation(); throw; }
        }
        private static void PaneTraceSnapshot(List<string> rows)
        {
            StartPaneTrace();
            rows.Add("pane-observed|" + _paneGlassCalls + "|" + _paneEffectCalls + "|" + _paneHeatDelta.ToString("R", CultureInfo.InvariantCulture));
            rows.Add("pane-context|" + _paneContext);
            rows.Add("pane-decision|" + _paneDecision);
            rows.Add("pane-last-stroke|" + (_paneLastStroke == null ? 0 : _paneLastStroke.Sequence));
            rows.Add("pane-wrong-armed|" + _paneWrongPane);
            rows.Add("pane-stroke-packets|" + _paneStrokePackets);
            rows.Add("pane-mutation-packets|" + _paneMutationPackets);
        }
    }
}
