using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class PaneScrapeSync
    {
        private PaneScrapeData? _c;
        private string _bindingWait = "world catalog";
        private SyncedItem? _vehicle, _tool;
        private PlayMakerFSM? _hand, _freezing, _scrape;
        private Collider? _pane, _insideCollider;
        private Transform? _eye;
        private FsmGameObject? _picked;
        private FsmBool? _inside;
        private FsmFloat? _cutoff;
        private Material? _material;
        private FsmStateAction[]? _glassActions, _soundActions;
        private PickupGate? _pickupGate;
        private readonly Dictionary<FsmState, FsmStateAction[]> _originals = new Dictionary<FsmState, FsmStateAction[]>();
        private readonly Dictionary<FsmState, FsmStateAction[]> _installed = new Dictionary<FsmState, FsmStateAction[]>();
        private readonly Dictionary<FsmStateAction, FsmStateAction?> _replaced = new Dictionary<FsmStateAction, FsmStateAction?>();
        private readonly List<Action> _restoreEntries = new List<Action>();

        private sealed class StrokeAction : FsmStateAction
        {
            private readonly PaneScrapeSync _owner;
            private readonly FsmStateAction _native;
            public StrokeAction(PaneScrapeSync owner, FsmStateAction native) { _owner = owner; _native = native; }
            public override void OnEnter()
            {
                try
                {
                    if (_owner.Active) _owner.Send(ScraperOperation.Stroke);
                    else _native.OnEnter();
                }
                catch (Exception e) { _owner.Fail(e); }
                Finish();
            }
        }
        private sealed class PickupGate : FsmStateAction
        {
            private readonly PaneScrapeSync _owner;
            private readonly FsmStateAction[] _native;
            private readonly bool[] _enabled;
            public bool AllowOnce;
            public PickupGate(PaneScrapeSync owner, FsmStateAction[] native)
            {
                _owner = owner; _native = native; _enabled = new bool[native.Length];
                for (int i = 0; i < native.Length; i++) _enabled[i] = native[i].Enabled;
            }
            public void Restore() { for (int i = 0; i < _native.Length; i++) _native[i].Enabled = _enabled[i]; }
            public override void OnEnter()
            {
                bool gated = _owner.Active && _owner.LocalHasTool() && !AllowOnce;
                AllowOnce = false;
                for (int i = 0; i < _native.Length; i++) _native[i].Enabled = !gated && _enabled[i];
                if (!gated) { Finish(); return; }
                try
                {
                    if (_owner.Send(ScraperOperation.Pickup) == 0) _owner.CancelPickup();
                }
                catch (Exception e) { _owner.Fail(e); _owner.CancelPickup(); }
                // No Finish while waiting: native parent/joint actions remain disabled.
            }
        }
        private static T? Field<T>(FsmStateAction action, string name) where T : class
            => action.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(action) as T;
        private static bool BoolField(FsmStateAction action, string name)
            => action.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(action) is bool value && value;
        private static PlayMakerFSM? FindFsm(string path, string name)
        {
            var obj = GameObject.Find(path);
            if (obj == null) return null;
            PlayMakerFSM? found = null;
            foreach (var fsm in obj.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == name) { if (found != null) return null; found = fsm; }
            return found != null && found.Fsm.Initialized && found.Fsm.Started ? found : null;
        }
        private bool Bind()
        {
            var world = WorldSyncManager.Instance;
            _c = SyncCatalog.PaneScrape;
            if (world == null || _c == null || world.ItemSync == null) return false;
            SyncedItem? car = null, tool = null;
            foreach (var item in world.ItemSync.Items.Values)
            {
                if (item.Body == null) continue;
                if (item.Path == _c["vehicle"] && item.IsVehicle)
                {
                    if (car != null) throw new InvalidOperationException("Ambiguous Corris identity.");
                    car = item;
                }
                if (item.Body.name == _c["toolName"])
                {
                    if (tool != null) throw new InvalidOperationException("Ambiguous shared scraper identity.");
                    tool = item;
                }
            }
            _bindingWait = "car=" + (car != null) + " tool=" + (tool != null);
            if (car == null || tool == null) return false;
            var scrape = FindFsm(_c["panePath"], _c["paneFsm"]);
            var freezing = FindFsm(_c["freezingPath"], _c["freezingFsm"]);
            var hand = FindFsm(_c["handPath"], _c["handFsm"]);
            var inside = FindFsm(_c["insidePath"], "PlayerTrigger");
            var eye = GameObject.Find(_c["eyePath"]);
            _bindingWait = "scrape=" + (scrape != null) + " freezing=" + (freezing != null)
                + " hand=" + (hand != null) + " inside=" + (inside != null) + " eye=" + (eye != null);
            if (scrape == null || freezing == null || hand == null || inside == null || eye == null) return false;
            var pane = scrape.GetComponent<Collider>();
            var cutoff = freezing.FsmVariables.FindFsmFloat(_c["cutoff"]);
            var picked = hand.FsmVariables.FindFsmGameObject(_c["picked"]);
            var material = freezing.FsmVariables.FindFsmMaterial("6");
            var delta = FsmHook.FindState(freezing, _c["deltaState"]);
            var stroke = FsmHook.FindState(scrape, _c["strokeState"]);
            var pickup = FsmHook.FindState(hand, _c["pickup"]);
            var sound = FsmHook.FindState(freezing, "Sound");
            _bindingWait = "states delta=" + (delta?.IsInitialized ?? false) + " stroke=" + (stroke?.IsInitialized ?? false)
                + " pickup=" + (pickup?.IsInitialized ?? false) + " sound=" + (sound?.IsInitialized ?? false);
            if (delta == null || stroke == null || pickup == null || sound == null
                || !delta.IsInitialized || !stroke.IsInitialized || !pickup.IsInitialized || !sound.IsInitialized) return false;
            if (pane == null || cutoff == null || picked == null || material?.Value == null
                || inside.FsmVariables.FindFsmBool("PlayerInside") == null || inside.GetComponent<Collider>() == null
                || freezing.FsmVariables.FindFsmGameObject("GlassPos") == null
                || delta.Actions.Length != 2 || delta.Actions[0].GetType().Name != "FloatAdd"
                || Field<FsmFloat>(delta.Actions[0], "floatVariable") != cutoff
                || Field<FsmFloat>(delta.Actions[0], "add")?.Name != "ScrapeEfficiency"
                || BoolField(delta.Actions[0], "everyFrame") || BoolField(delta.Actions[0], "perSecond")
                || delta.Actions[1].GetType().Name != "SetMaterialFloat"
                || Field<FsmFloat>(delta.Actions[1], "floatValue") != cutoff
                || Field<FsmMaterial>(delta.Actions[1], "material")?.Value != material.Value
                || Field<FsmString>(delta.Actions[1], "namedFloat")?.Value != "_Cutoff"
                || BoolField(delta.Actions[1], "everyFrame")
                || sound.Actions.Length != 4 || sound.Actions[0].GetType().Name != "RandomFloat"
                || sound.Actions[1].GetType().Name != "ArrayListGetRandom"
                || sound.Actions[2].GetType().Name != "MasterAudioPlaySound"
                || sound.Actions[3].GetType().Name != "FloatAdd"
                || Field<FsmFloat>(sound.Actions[3], "floatVariable")?.Name != "PlayerTemp"
                || Field<FsmFloat>(sound.Actions[3], "add")?.Name != "BodyTempAdd"
                || BoolField(sound.Actions[3], "everyFrame") || BoolField(sound.Actions[3], "perSecond"))
                throw new InvalidOperationException("Native windshield glass/effect signature changed.");
            int eventIndex = -1;
            for (int i = 0; i < stroke.Actions.Length; i++)
                if (stroke.Actions[i].GetType().Name == "SendEventByName"
                    && Field<FsmString>(stroke.Actions[i], "sendEvent")?.Value == _c["strokeEvent"])
                { if (eventIndex >= 0) throw new InvalidOperationException("Ambiguous stroke event."); eventIndex = i; }
            if (eventIndex < 0 || scrape.FsmVariables.FindFsmFloat("Distance")?.Value != .8f)
                throw new InvalidOperationException("Native contact/stroke signature changed.");
            foreach (string key in new[] { "equip", "off", "drop", "throw" })
                if (FsmHook.FindState(hand, _c[key])?.IsInitialized != true)
                    throw new InvalidOperationException("Native scraper equipment path changed.");
            if (FsmHook.FindState(hand, "Look for object")?.IsInitialized != true)
                throw new InvalidOperationException("Native scraper cancellation path changed.");
            PrepareHandEntry(hand, _c["pickup"]); PrepareHandEntry(hand, "Look for object");
            _vehicle = car; _tool = tool; _pane = pane; _scrape = scrape; _freezing = freezing; _hand = hand;
            _eye = eye.transform; _picked = picked; _cutoff = cutoff; _material = material.Value;
            _inside = inside.FsmVariables.FindFsmBool("PlayerInside"); _insideCollider = inside.GetComponent<Collider>();
            _glassActions = delta.Actions; _soundActions = sound.Actions;
            _originals[stroke] = stroke.Actions;
            var replacement = (FsmStateAction[])stroke.Actions.Clone();
            replacement[eventIndex] = new StrokeAction(this, replacement[eventIndex]);
            _replaced.Add(replacement[eventIndex], stroke.Actions[eventIndex]);
            replacement[eventIndex].Init(stroke); stroke.Actions = replacement;
            _installed[stroke] = replacement;
            _originals[pickup] = pickup.Actions;
            _pickupGate = new PickupGate(this, pickup.Actions);
            _pickupGate.Init(pickup);
            var actions = new List<FsmStateAction>(pickup.Actions); actions.Insert(0, _pickupGate); pickup.Actions = actions.ToArray();
            _replaced.Add(_pickupGate, null); _installed[pickup] = pickup.Actions;
            HookHand("equip", ScraperOperation.Equip); HookHand("off", ScraperOperation.Off);
            HookHand("drop", ScraperOperation.Drop); HookHand("throw", ScraperOperation.Drop);
            WinterMPPlugin.Log.LogInfo("PaneScrape: bound windshield=" + car.Id + " shared scraper=" + tool.Id);
            return true;
        }
        private void HookHand(string key, ScraperOperation operation)
        {
            var state = FsmHook.FindState(_hand!, _c![key])!;
            _originals[state] = state.Actions;
            if (!FsmHook.OnStateEnter(_hand!, _c[key], () => {
                try { Send(operation); } catch (Exception e) { Fail(e); }
            }, out var hook)) throw new InvalidOperationException("Cannot hook scraper hand path.");
            _replaced.Add(hook!, null); _installed[state] = state.Actions;
            hook!.Init(state);
        }
        private void PrepareHandEntry(PlayMakerFSM hand, string state)
        {
            var globals = hand.Fsm.GlobalTransitions; var events = hand.Fsm.Events;
            if (!FsmHook.EnsureRemoteEntry(hand, state)) throw new InvalidOperationException("Missing scraper hand entry.");
            var installedGlobals = hand.Fsm.GlobalTransitions; var installedEvents = hand.Fsm.Events;
            _restoreEntries.Add(() => {
                if (hand == null) return;
                hand.Fsm.GlobalTransitions = RemoveAdded(hand.Fsm.GlobalTransitions, globals, installedGlobals);
                hand.Fsm.Events = RemoveAdded(hand.Fsm.Events, events, installedEvents);
            });
        }
        private static T[] RemoveAdded<T>(T[] current, T[] original, T[] installed) where T : class
        {
            if (ReferenceEquals(current, installed)) return original;
            var retained = new List<T>(current);
            foreach (var item in installed)
                if (Array.IndexOf(original, item) < 0) retained.Remove(item);
            return retained.ToArray();
        }
        private void CancelPickup()
        {
            _pickupSequence = 0;
            if (!WaitingForPickup) return;
            _picked!.Value = null;
            FsmHook.FireRemoteEntry(_hand!, "Look for object");
        }
        private void ApplyPersonalEffects()
        {
            // Native sound/heat actions run only on the accepted actor's process.
            _freezing!.FsmVariables.FindFsmGameObject("GlassPos")!.Value = _pane!.gameObject;
            foreach (var action in _soundActions!) action.OnEnter();
        }
        private void RestoreBindings()
        {
            _pickupGate?.Restore();
            foreach (var pair in _originals)
            {
                if (_installed.TryGetValue(pair.Key, out var installed) && ReferenceEquals(pair.Key.Actions, installed))
                    pair.Key.Actions = pair.Value;
                else
                {
                    var actions = new List<FsmStateAction>();
                    foreach (var action in pair.Key.Actions)
                    {
                        if (!_replaced.TryGetValue(action, out var native)) actions.Add(action);
                        else if (native != null) actions.Add(native);
                    }
                    pair.Key.Actions = actions.ToArray();
                }
            }
            for (int i = _restoreEntries.Count - 1; i >= 0; i--) _restoreEntries[i]();
            _restoreEntries.Clear();
            _originals.Clear(); _installed.Clear(); _replaced.Clear(); _pickupGate = null; _pane = null; _vehicle = null; _tool = null;
            _hand = null; _scrape = null; _freezing = null; _eye = null; _picked = null;
            _cutoff = null; _material = null; _glassActions = null; _soundActions = null;
            _inside = null; _insideCollider = null; _c = null;
        }
    }
}
