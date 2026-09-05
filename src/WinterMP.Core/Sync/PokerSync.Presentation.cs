using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class PokerSync
    {
        private readonly Dictionary<string, UnityEngine.Object> _assets = new Dictionary<string, UnityEngine.Object>();
        private readonly List<SceneObject> _originalScene = new List<SceneObject>();
        private readonly Dictionary<Renderer, OwnedMaterial> _materials = new Dictionary<Renderer, OwnedMaterial>();
        private PokerState? _presented;
        private float _revealUntil;

        private sealed class SceneObject
        {
            public Transform Transform = null!;
            public bool Active;
            public Vector3 Position;
            public Renderer? Renderer;
            public Material[] Materials = new Material[0];
            public TextMesh? Text;
            public string Content = "";
        }
        private sealed class OwnedMaterial
        {
            public Material Source = null!, Material = null!;
        }

        private bool BindAssets()
        {
            if (_config == null) return false;
            foreach (var source in _config.Assets.Values)
            {
                if (_assets.ContainsKey(source.Key)) continue;
                var fsm = FindFsm(source.Path, source.Fsm);
                if (fsm == null) return false;
                var state = FsmHook.FindState(fsm, source.State);
                if (state == null) throw new InvalidOperationException("Missing poker asset state: " + source.State);
                UnityEngine.Object? asset = null;
                foreach (var action in state.Actions)
                {
                    if (action.GetType().Name != source.Action) continue;
                    object? value = Member(action, source.Field);
                    if (value is FsmMaterial material) asset = material.Value;
                    else if (value is FsmTexture texture) asset = texture.Value;
                    else if (value != null && Member(value, "TargetObject") is FsmObject target) asset = target.Value;
                    break;
                }
                if (asset == null) throw new InvalidOperationException("Missing poker presentation asset: " + source.Key);
                _assets.Add(source.Key, asset);
            }
            ValidatePresentation();
            return true;
        }

        private void ValidatePresentation()
        {
            if (_config == null || _root == null || _logic == null) throw new InvalidOperationException("Missing poker root.");
            foreach (string key in new[] { "game", "camera", "menu", "credit", "winnings", "bet", "pendingMoney", "doubleBack", "doublePanel", "winMarker", "soundPath" })
                if (Node(key) == null) throw new InvalidOperationException("Missing poker object: " + key);
            foreach (string key in new[] { "game", "doublePanel", "winMarker" }) RequireRenderer(Node(key));
            RequireRenderer(_root.Find(_config["screen"]));
            foreach (string button in _config.ButtonMeshes) RequireRenderer(_root.Find(_config["meshPath"] + "/" + button));
            var cards = new List<string>(_config.Cards) { _config["doubleCard"] };
            foreach (string path in cards)
            {
                var card = _logic.Find(path);
                if (card == null) throw new InvalidOperationException("Missing poker card: " + path);
                RequireRenderer(card.Find(card.name + _config["rankSuffix"]));
                RequireRenderer(card.Find(card.name + _config["suitSuffix"]));
            }
            foreach (string path in _config.Backs) RequireRenderer(_logic.Find(path));
            foreach (var asset in _assets)
            {
                bool valid = asset.Key.EndsWith("Text", StringComparison.Ordinal) ? asset.Value is TextMesh
                    : asset.Key.StartsWith("suit", StringComparison.Ordinal) || asset.Key.StartsWith("button", StringComparison.Ordinal)
                        ? asset.Value is Material : asset.Value is Texture;
                if (!valid) throw new InvalidOperationException("Invalid poker asset type: " + asset.Key);
            }
            foreach (string key in new[] { "menuResetState", "menuIdleState", "menuBeginState" })
            {
                var menu = FindFsm(_config["menu"], _config["menuFsm"]);
                if (menu == null || FsmHook.FindState(menu, _config[key]) == null)
                    throw new InvalidOperationException("Missing poker menu state: " + key);
            }
            foreach (string key in new[] { "credit", "winnings" })
            {
                var fsm = FindFsm(_config[key], _config[key + "Fsm"]);
                if (fsm == null || FsmHook.FindState(fsm, _config["balanceState"]) == null)
                    throw new InvalidOperationException("Missing poker balance handoff state.");
            }
        }

        private static void RequireRenderer(Transform? transform)
        {
            if (transform == null || transform.GetComponent<Renderer>()?.sharedMaterial == null)
                throw new InvalidOperationException("Missing poker renderer/material: " + (transform != null ? transform.name : "unknown"));
        }

        private static object? Member(object owner, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var field = owner.GetType().GetField(name, flags);
            return field != null ? field.GetValue(owner) : owner.GetType().GetProperty(name, flags)?.GetValue(owner, null);
        }

        private void CaptureScene()
        {
            if (_root == null || _originalScene.Count > 0) return;
            foreach (var transform in _root.GetComponentsInChildren<Transform>(true))
            {
                var renderer = transform.GetComponent<Renderer>();
                var text = transform.GetComponent<TextMesh>();
                _originalScene.Add(new SceneObject
                {
                    Transform = transform, Active = transform.gameObject.activeSelf, Position = transform.localPosition,
                    Renderer = renderer, Materials = renderer != null ? renderer.sharedMaterials : new Material[0],
                    Text = text, Content = text != null ? text.text : "",
                });
            }
            var sounds = Node("soundPath");
            if (sounds != null)
                foreach (Transform sound in sounds) sound.gameObject.SetActive(false);
        }

        private void Present()
        {
            if (_config == null || _view == null || _logic == null || _root == null) return;
            foreach (var fsm in _suppressed.Keys)
                if (fsm != null && fsm.enabled) fsm.enabled = false;
            if (!_root.gameObject.activeInHierarchy) return;
            var state = _view;
            if (_presented != null && state.Revision != _presented.Revision
                && _presented.Phase == PokerState.Guessing && state.Phase == PokerState.Ready)
                _revealUntil = Time.unscaledTime + 1.5f;
            bool revealing = state.Phase == PokerState.Ready && Time.unscaledTime < _revealUntil;
            bool playing = state.Credit + state.Winnings > 0 || state.Phase != PokerState.Ready || revealing;
            Activate(_logic, true);
            Activate(Node("soundPath"), true);
            Activate(Node("camera"), playing);
            Activate(Node("menu"), false);
            Activate(Node("game"), playing);
            Activate(Node("credit"), playing);
            Activate(Node("winnings"), playing);
            Activate(Node("bet"), playing);
            Texture(_root.Find(_config["screen"]), playing ? "screenGame" : "screenIdle");
            Texture(Node("game"), "bet" + state.Bet);
            SetText("creditText", state.Credit);
            SetText("winsText", state.Winnings);
            SetText("pendingText", state.PendingWin);
            Activate(Node("pendingMoney"), state.PendingWin > 0);
            Activate((_assets["pendingText"] as TextMesh)?.transform, state.PendingWin > 0);
            for (int i = 0; i < 5; i++)
            {
                bool visible = playing && state.Phase != PokerState.Ready;
                ShowCard(_logic.Find(_config.Cards[i]), visible ? state.Cards[i] : (byte)0,
                    (state.HoldMask & (1 << i)) != 0);
                Activate(_logic.Find(_config.Backs[i]), playing && !visible);
            }
            ShowCard(Node("doubleCard"), state.Phase == PokerState.WinOffer || revealing ? state.DoubleCard : (byte)0, false, false);
            Activate(Node("doubleBack"), state.Phase == PokerState.Guessing);
            Activate(Node("doublePanel"), state.Phase == PokerState.WinOffer || state.Phase == PokerState.Guessing);
            Texture(Node("doublePanel"), state.Phase == PokerState.Guessing ? "doubleGuess" : "doubleOffer");
            var marker = Node("winMarker");
            Activate(marker, state.Hand > 0 && state.Phase == PokerState.WinOffer);
            if (marker != null)
            {
                var position = marker.localPosition;
                position.x = _config.MarkerX; position.z = _config.MarkerStartZ + (state.Hand - 1) * _config.MarkerStepZ;
                marker.localPosition = position;
            }
            for (byte action = PokerIntent.Bet; action <= PokerIntent.TakeWin; action++)
                Material(_root.Find(_config["meshPath"] + "/" + _config.ButtonMeshes[action - 1]), Available(state, action) ? "buttonOn" : "buttonOff");
            if (_presented != null && state.Revision != _presented.Revision)
            {
                if (state.Round != _presented.Round) Sound("cardSound");
                else if (_presented.Phase == PokerState.Holding && state.Phase != PokerState.Holding)
                    Sound(state.Hand > 0 ? "winSound" : "loseSound");
                else if (_presented.Phase == PokerState.Guessing && state.Phase != PokerState.Guessing)
                    Sound(state.PendingWin > 0 || state.Winnings > _presented.Winnings ? "winSound" : "loseSound");
                else if (state.Credit != _presented.Credit || state.Winnings != _presented.Winnings) Sound("coinSound");
            }
            _presented = state;
        }

        private Transform? Node(string key) => _logic != null && _config != null ? _logic.Find(_config[key]) : null;
        private static void Activate(Transform? transform, bool active)
        {
            if (transform != null && transform.gameObject.activeSelf != active) transform.gameObject.SetActive(active);
        }
        private void ShowCard(Transform? card, byte value, bool held, bool move = true)
        {
            if (card == null || _config == null) return;
            Activate(card, value != 0);
            if (value == 0) return;
            int suit = PokerLedger.Suit(value), rank = PokerLedger.Rank(value);
            var suitNode = card.Find(card.name + _config["suitSuffix"]);
            var rankNode = card.Find(card.name + _config["rankSuffix"]);
            Activate(suitNode, true); Activate(rankNode, true);
            Material(suitNode, "suit" + suit);
            Texture(rankNode, (suit < 2 ? "black" : "red") + rank);
            if (!move) return;
            var position = card.localPosition; position.z = held ? _config.HeldZ : _config.RestZ;
            card.localPosition = position;
        }
        private void SetText(string key, int value)
        {
            var text = _assets[key] as TextMesh;
            if (text == null) throw new InvalidOperationException("Poker text asset has the wrong type: " + key);
            text.text = value.ToString();
        }
        private void Material(Transform? node, string key)
        {
            var renderer = node != null ? node.GetComponent<Renderer>() : null;
            var source = _assets[key] as Material;
            if (renderer == null || source == null) throw new InvalidOperationException("Missing poker material binding: " + key);
            Own(renderer, source);
        }
        private void Texture(Transform? node, string key)
        {
            var renderer = node != null ? node.GetComponent<Renderer>() : null;
            var texture = _assets[key] as Texture;
            if (renderer == null || texture == null) throw new InvalidOperationException("Missing poker texture binding: " + key);
            Own(renderer, null).mainTexture = texture;
        }
        private Material Own(Renderer renderer, Material? source)
        {
            if (_materials.TryGetValue(renderer, out var owned))
            {
                if (source == null || owned.Source == source) return owned.Material;
                UnityEngine.Object.Destroy(owned.Material);
            }
            source = source ?? renderer.sharedMaterial;
            if (source == null) throw new InvalidOperationException("Poker renderer has no material.");
            owned = new OwnedMaterial { Source = source, Material = new Material(source) };
            _materials[renderer] = owned; renderer.sharedMaterial = owned.Material;
            return owned.Material;
        }
        private void Sound(string key)
        {
            if (_logic == null || _config == null) return;
            var sound = _logic.Find(_config["soundPath"] + "/" + _config[key]);
            if (sound == null) return;
            sound.gameObject.SetActive(false); sound.gameObject.SetActive(true);
        }
        private void Award(byte flags)
        {
            if (_config == null || flags == 0) return;
            try
            {
                var steam = FsmVariables.GlobalVariables.FindFsmGameObject(_config["achievementGlobal"])?.Value;
                if (steam == null) return;
                foreach (var fsm in steam.GetComponents<PlayMakerFSM>())
                {
                    if (fsm.FsmName != _config["achievementFsm"]) continue;
                    if ((flags & PokerResult.RoyalAchievement) != 0) fsm.SendEvent(_config["royalAchievement"]);
                    if ((flags & PokerResult.CashoutAchievement) != 0) fsm.SendEvent(_config["cashoutAchievement"]);
                }
            }
            catch (Exception e) { WinterMPPlugin.Log.LogDebug("Poker achievement: " + e.Message); }
        }
        private static bool Available(PokerState s, byte action)
        {
            if (action == PokerIntent.Insert) return s.Credit < 500;
            if (s.Phase == PokerState.Holding) return action == PokerIntent.Deal || (action >= PokerIntent.Hold1 && action <= PokerIntent.Hold5);
            if (s.Phase == PokerState.Guessing) return action == PokerIntent.Low || action == PokerIntent.High;
            if (s.Phase == PokerState.WinOffer) return action == PokerIntent.Double || action == PokerIntent.Deal || action == PokerIntent.TakeWin;
            return s.Credit + s.Winnings > 0 && (action == PokerIntent.Bet || action == PokerIntent.TakeWin
                || (action == PokerIntent.Deal && s.Credit + s.Winnings >= s.Bet));
        }

        private void RestoreScene()
        {
            foreach (var original in _originalScene)
            {
                if (original.Transform == null) continue;
                original.Transform.localPosition = original.Position;
                original.Transform.gameObject.SetActive(original.Active);
                if (original.Renderer != null) original.Renderer.sharedMaterials = original.Materials;
                if (original.Text != null) original.Text.text = original.Content;
            }
            foreach (var owned in _materials.Values) UnityEngine.Object.Destroy(owned.Material);
            _originalScene.Clear(); _materials.Clear();
            _revealUntil = 0;
        }
        private void ResetNativeMenu(PokerState? remaining)
        {
            if (_config == null) return;
            try
            {
                Activate(Node("game"), false);
                var startup = FindFsm(".", _config["startupFsm"]);
                if (startup != null && startup.enabled && startup.gameObject.activeInHierarchy && !startup.Fsm.Started)
                    startup.Fsm.Start();
                var menu = FindFsm(_config["menu"], _config["menuFsm"]);
                if (menu == null) return;
                menu.enabled = true; menu.gameObject.SetActive(true);
                if (menu.gameObject.activeInHierarchy && !menu.Fsm.Started) menu.Fsm.Start();
                if (remaining != null) InstallNativeHandoff(remaining);
                if (FsmHook.EnsureRemoteEntry(menu, _config["menuResetState"]))
                    FsmHook.FireRemoteEntry(menu, _config["menuResetState"]);
            }
            catch (Exception e) { WinterMPPlugin.Log.LogDebug("Poker menu reset: " + e.Message); }
        }

        private void InstallNativeHandoff(PokerState remaining)
        {
            if (_config == null || remaining.Credit + remaining.Winnings == 0) return;
            var menu = FindFsm(_config["menu"], _config["menuFsm"]);
            var credit = FindFsm(_config["credit"], _config["creditFsm"]);
            var wins = FindFsm(_config["winnings"], _config["winningsFsm"]);
            var state = menu != null ? FsmHook.FindState(menu, _config["menuIdleState"]) : null;
            if (state == null || menu == null || credit == null || wins == null || _credit == null || _wins == null) return;
            string balanceState = _config["balanceState"], begin = _config["menuBeginState"];
            var creditValue = _credit; var winsValue = _wins;
            FsmStateAction? handoff = null;
            // Menu initialization zeros both banks. Restore once AFTER that reset,
            // even when the town is currently unloaded and wakes after this session.
            handoff = new FsmHookAction(() =>
            {
                var actions = new List<FsmStateAction>(state.Actions);
                if (handoff != null) actions.Remove(handoff);
                state.Actions = actions.ToArray();
                try
                {
                    if (!wins.Fsm.Started) wins.Fsm.Start();
                    if (!credit.Fsm.Started) credit.Fsm.Start();
                    creditValue.Value = remaining.Credit; winsValue.Value = remaining.Winnings;
                    if (FsmHook.EnsureRemoteEntry(wins, balanceState)) FsmHook.FireRemoteEntry(wins, balanceState);
                    if (FsmHook.EnsureRemoteEntry(credit, balanceState)) FsmHook.FireRemoteEntry(credit, balanceState);
                    if (FsmHook.EnsureRemoteEntry(menu, begin)) FsmHook.FireRemoteEntry(menu, begin);
                }
                catch (Exception e) { WinterMPPlugin.Log.LogError("VideoPoker balance handoff: " + e); }
            });
            var updated = new List<FsmStateAction>(state.Actions) { handoff };
            state.Actions = updated.ToArray();
        }
    }
}
