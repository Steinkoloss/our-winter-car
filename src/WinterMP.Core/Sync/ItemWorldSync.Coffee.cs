using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class CoffeeBinding
        {
            internal uint Id, Revision;
            internal byte Kind;
            internal Rigidbody Body = null!;
            internal PlayMakerFSM Fsm = null!;
            internal FsmFloat? Water, Ground, Coffee, Caffeine, Volume;
            internal FsmBool? Cap;
            internal CoffeeState? Received, Sent;
            internal float NextSend;
            internal bool Replica;
        }
        private readonly Dictionary<uint, CoffeeBinding> _coffee = new Dictionary<uint, CoffeeBinding>();
        private readonly Dictionary<uint, CoffeeState> _pendingCoffee = new Dictionary<uint, CoffeeState>();
        private readonly List<Action> _coffeeRestore = new List<Action>();
        private readonly Dictionary<byte, uint> _coffeeSequences = new Dictionary<byte, uint>();
        private CoffeeBinding? _coffeePot, _coffeeCup;
        private PlayMakerFSM? _coffeeCap, _coffeePour;
        private Transform? _coffeeCupTarget;
        private Material? _coffeePotMaterial, _coffeeCupMaterial;
        private bool _coffeeFailed;
        private float _coffeeProbeAt, _coffeeSendAt, _coffeePourUntil, _coffeePourTick, _coffeeDrinkDeadline;
        private byte _coffeePourActor;
        private uint _coffeeOutSequence, _coffeePendingDrink;
        internal static bool IsCoffeeFsm(PlayMakerFSM fsm)
        {
            var c = SyncCatalog.Coffee;
            if (c == null) return false;
            string path = ScenePath.Of(fsm.transform);
            if (path == c["cup"] || path == c["pot"] || path.StartsWith(c["pot"] + "/", StringComparison.Ordinal)) return true;
            // Picking up and dropping a native item reparents it outside EQUIPMENTS.
            // Its save tag still distinguishes the household cup from vendor cups.
            for (var node = fsm.transform; node != null; node = node.parent)
            {
                if (!c["pot"].EndsWith("/" + node.name, StringComparison.Ordinal) && !c["cup"].EndsWith("/" + node.name, StringComparison.Ordinal)) continue;
                foreach (var candidate in node.GetComponents<PlayMakerFSM>())
                    if (HomeCoffeeKind(candidate, c) >= 0) return true;
            }
            return false;
        }
        private static int HomeCoffeeKind(PlayMakerFSM fsm, CoffeeData c)
        {
            if (!fsm.Fsm.Initialized) return -1;
            string tag = fsm.FsmVariables.FindFsmString(c["saveTagVar"])?.Value ?? "";
            if (fsm.FsmName == c["data"] && tag == c["potSaveTag"]) return 0;
            if (fsm.FsmName == c["use"] && tag == c["cupSaveTag"]) return 1;
            return -1;
        }

        private void RefreshCoffee()
        {
            var c = SyncCatalog.Coffee; var session = SessionManager.Instance;
            if (_coffeeFailed || _coffeePot != null || c == null || session == null || Time.unscaledTime < _coffeeProbeAt) return;
            _coffeeProbeAt = Time.unscaledTime + 2;
            try
            {
                PlayMakerFSM? pot = null, cup = null;
                foreach (var obj in ScenePath.ScanFsms())
                {
                    var f = obj as PlayMakerFSM; if (f == null) continue;
                    int kind = HomeCoffeeKind(f, c);
                    if (kind == 0) { if (pot != null && pot != f) throw new InvalidOperationException("Duplicate home coffee pot save tag."); pot = f; }
                    if (kind == 1) { if (cup != null && cup != f) throw new InvalidOperationException("Duplicate home coffee cup save tag."); cup = f; }
                }
                if (pot == null || cup == null || !pot.Fsm.Started || !cup.Fsm.Started) return;
                _coffeePot = BindCoffee(pot, StableHash.Fnv1a32("item:" + c["pot"]), 0, false);
                _coffeeCup = BindCoffee(cup, StableHash.Fnv1a32("item:" + c["cup"]), 1, false);
                _coffeeCap = CoffeeFsm(pot.transform.Find(c["cap"]), c["use"]);
                _coffeePour = CoffeeFsm(pot.transform.Find(c["target"]), c["pour"]);
                _coffeeCupTarget = cup.transform.Find(c["cupTarget"]);
                if (_coffeeCupTarget == null || _coffeePot.Fsm.FsmVariables.FindFsmFloat(c["groundMaxVar"])?.Value != 26)
                    throw new InvalidOperationException("Home coffee geometry or capacity changed.");
                ValidateCoffee(c);
                RememberCoffeeMaterial(_coffeePot, c["surface"], out _coffeePotMaterial);
                RememberCoffeeMaterial(_coffeeCup, c["cupSurface"], out _coffeeCupMaterial);
                InstallCoffeeInput(c);
                PauseCoffee(_coffeePour);
                if (!session.IsHost)
                {
                    foreach (var f in new[] { pot, CoffeeFsm(pot.transform, c["empty"]), CoffeeFsm(pot.transform, c["fire"]),
                        CoffeeFsm(pot.transform.Find(c["grounds"]), c["groundFsm"]), CoffeeFsm(pot.transform.Find(c["water"]), c["waterFsm"]) }) PauseCoffee(f);
                    foreach (string path in new[] { c["capMesh"], c["functions"], c["sound"] })
                    { var go = pot.transform.Find(path).gameObject; bool active = go.activeSelf; _coffeeRestore.Add(() => { if (go != null) go.SetActive(active); }); }
                }
                SyncEventLog.Record("coffee-bound", "native home pot/cup " + (session.IsHost ? "authority" : "replica"));
            }
            catch (Exception e) { FailCoffee(e); }
        }

        private static PlayMakerFSM CoffeeFsm(Transform transform, string name)
        {
            if (transform == null) throw new InvalidOperationException("Missing coffee transform.");
            var f = SausageUse(transform.gameObject, name) ?? throw new InvalidOperationException("Missing coffee FSM " + name);
            if (!f.Fsm.Initialized) f.Fsm.Init(f);
            return f;
        }
        private CoffeeBinding BindCoffee(PlayMakerFSM f, uint id, byte kind, bool replica)
        {
            var c = SyncCatalog.Coffee!;
            var b = new CoffeeBinding { Id = id, Kind = kind, Fsm = f, Body = f.GetComponent<Rigidbody>(), Replica = replica };
            if (b.Body == null) throw new InvalidOperationException("Coffee body missing.");
            b.Water = kind == 0 ? RequiredCoffeeFloat(f, c["waterVar"]) : null;
            b.Ground = kind != 1 ? RequiredCoffeeFloat(f, c["groundVar"]) : null;
            b.Coffee = kind != 2 ? RequiredCoffeeFloat(f, c["coffeeVar"]) : null;
            b.Caffeine = kind != 2 ? RequiredCoffeeFloat(f, c["caffeineVar"]) : null;
            b.Volume = kind == 0 ? RequiredCoffeeFloat(f, c["volumeVar"]) : null;
            b.Cap = kind == 0 ? f.FsmVariables.FindFsmBool(c["capVar"]) : null;
            if (kind == 0 && b.Cap == null) throw new InvalidOperationException("Coffee lid variable missing.");
            if (SessionManager.Instance?.IsHost == false && !replica)
            {
                foreach (var v in f.FsmVariables.FloatVariables) { float old = v.Value; _coffeeRestore.Add(() => v.Value = old); }
                foreach (var v in f.FsmVariables.BoolVariables) { bool old = v.Value; _coffeeRestore.Add(() => v.Value = old); }
            }
            if (_items.TryGetValue(id, out var item) && item.Body != b.Body) throw new InvalidOperationException("Coffee identity collision.");
            if (item == null) _items[id] = new SyncedItem { Id = id, Body = b.Body, Path = ScenePath.Of(b.Body.transform), LastPosition = b.Body.position };
            _trackedBodies[b.Body] = true; _coffee[id] = b;
            return b;
        }
        private static FsmFloat RequiredCoffeeFloat(PlayMakerFSM f, string name) => f.FsmVariables.FindFsmFloat(name) ?? throw new InvalidOperationException("Coffee variable missing: " + name);
        private void PauseCoffee(PlayMakerFSM f)
        {
            var pause = new FsmSuppressor();
            if (!pause.Suppress(f)) throw new InvalidOperationException("Cannot suppress guest coffee simulation.");
            _coffeeRestore.Add(pause.Restore);
        }
        private void RememberCoffeeMaterial(CoffeeBinding b, string path, out Material material)
        {
            var renderer = b.Body.transform.Find(path).GetComponent<Renderer>(); var original = renderer.sharedMaterial;
            var copy = renderer.material; material = copy;
            _coffeeRestore.Add(() => { if (renderer != null) renderer.sharedMaterial = original; if (copy != null) UnityEngine.Object.Destroy(copy); });
        }
        private void ProcessCoffee(SessionManager session)
        {
            if (_coffeeFailed) return;
            try
            {
                RefreshCoffee();
                if (_coffeePot == null || _coffeeCup == null) return;
                if (_coffeePot.Body == null || _coffeeCup.Body == null) throw new InvalidOperationException("Home coffee object removed.");
                if (session.IsHost) TickCoffeePour(session);
                if (_coffeePendingDrink != 0 && Time.unscaledTime > _coffeeDrinkDeadline)
                { _coffeePendingDrink = 0; FsmHook.FireRemoteEntry(_coffeeCup.Fsm, SyncCatalog.Coffee!["delay"]); }
                if (Time.unscaledTime < _coffeeSendAt) return;
                _coffeeSendAt = Time.unscaledTime + .15f;
                ApplyPendingCoffee();
                foreach (var b in _coffee.Values)
                {
                    if (b.Body == null || _spawnLifecycle.IsRetired(b.Id)) continue;
                    if (session.IsHost)
                    {
                        var state = CaptureCoffee(b);
                        if (b.Sent == null || !CoffeePolicy.SameContents(state, b.Sent) || Time.unscaledTime > b.NextSend)
                        { b.Sent = state; b.NextSend = Time.unscaledTime + 2; session.SendWorldMessage(state, Channel.ReliableOrdered); }
                    }
                    PresentCoffee(b);
                }
            }
            catch (Exception e) { FailCoffee(e); }
        }
        private void FailCoffee(Exception e)
        {
            if (_coffeeFailed) return;
            WinterMPPlugin.Log.LogError("Home coffee sync disabled: " + e); SyncEventLog.Record("coffee-disabled", e.Message);
            ClearCoffee(); _coffeeFailed = true;
        }
        private void ClearCoffee()
        {
            if (_coffeeCup?.Fsm != null && (_coffeeCup.Fsm.ActiveStateName == CoffeeRequestState || _coffeeCup.Fsm.ActiveStateName == SyncCatalog.Coffee?["fill"]))
                FsmHook.FireRemoteEntry(_coffeeCup.Fsm, SyncCatalog.Coffee!["delay"]);
            for (int i = _coffeeRestore.Count - 1; i >= 0; i--)
                try { _coffeeRestore[i](); } catch (Exception e) { WinterMPPlugin.Log.LogWarning("Coffee restore: " + e.Message); }
            _coffeeRestore.Clear();
            ClearCoffeePackets();
            _coffee.Clear(); _pendingCoffee.Clear(); _coffeeSequences.Clear(); _coffeePot = _coffeeCup = null;
            _coffeeCap = _coffeePour = null; _coffeeCupTarget = null; _coffeePotMaterial = _coffeeCupMaterial = null;
            _coffeeHostDrinkUntil = _coffeePourUntil = _coffeeSendAt = _coffeeProbeAt = 0; _coffeePendingDrink = _coffeeOutSequence = 0; _coffeeFailed = false;
        }
    }
}
