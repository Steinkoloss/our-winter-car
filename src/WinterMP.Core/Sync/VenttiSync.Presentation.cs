using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VenttiSync
    {
        private readonly List<CardMesh> _playerMeshes = new List<CardMesh>(), _houseMeshes = new List<CardMesh>();
        private readonly Dictionary<GameObject, bool> _sceneActive = new Dictionary<GameObject, bool>();
        private Texture[]? _cardTextures;
        private Transform? _playerCardsRoot, _houseCardsRoot;
        private FsmString? _interaction;
        private FsmBool? _use;
        private string _ownedInteraction = string.Empty;
        private bool _previousUse;

        private sealed class CardMesh
        {
            public Renderer Renderer = null!;
            public Material[] Original = new Material[0];
            public Material Face = null!;
            public bool Created;
            public byte Card;
        }

        private bool BindPresentation()
        {
            if (_gameConfig == null || _anchor == null) return false;
            if (_cardTextures != null) return true;
            _interaction = FsmVariables.GlobalVariables.FindFsmString(_gameConfig["interactionGlobal"]);
            _use = FsmVariables.GlobalVariables.FindFsmBool(_gameConfig["useGlobal"]);
            var textures = _anchor.Find(_gameConfig["texturesPath"]);
            _playerCardsRoot = _anchor.Find(_gameConfig["playerCardsPath"]);
            _houseCardsRoot = _anchor.Find(_gameConfig["houseCardsPath"]);
            if (textures == null || _playerCardsRoot == null || _houseCardsRoot == null || _interaction == null || _use == null) return false;
            IList? source = null;
            foreach (var component in textures.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().Name != "PlayMakerArrayListProxy") continue;
                var type = component.GetType();
                source = type.GetProperty("arrayList", BindingFlags.Public | BindingFlags.Instance)?.GetValue(component, null) as IList;
                if (source == null || source.Count == 0)
                    source = type.GetField("preFillTextureList", BindingFlags.Public | BindingFlags.Instance)?.GetValue(component) as IList;
                break;
            }
            if (source == null || source.Count != 53) return false;
            var faces = new Texture[53];
            for (int i = 1; i < faces.Length; i++)
            {
                var texture = source[i] as Texture;
                if (texture == null) return false;
                faces[i] = texture;
            }
            // Check the complete set before allocating materials or changing visibility.
            foreach (var root in new[] { _playerCardsRoot, _houseCardsRoot })
                for (int i = 1; i <= _gameConfig.CardSlots; i++)
                {
                    var card = root.Find(i.ToString(CultureInfo.InvariantCulture));
                    var renderer = card == null ? null : card.GetComponent<Renderer>();
                    if (renderer == null || renderer.sharedMaterials.Length <= _gameConfig.MaterialIndex
                        || renderer.sharedMaterials[_gameConfig.MaterialIndex] == null
                        || !renderer.sharedMaterials[_gameConfig.MaterialIndex].HasProperty(_gameConfig["textureProperty"])) return false;
                }
            foreach (var root in new[] { _playerCardsRoot, _houseCardsRoot })
            {
                CaptureActive(root.gameObject);
                var list = root == _playerCardsRoot ? _playerMeshes : _houseMeshes;
                for (int i = 1; i <= _gameConfig.CardSlots; i++)
                {
                    var card = root.Find(i.ToString(CultureInfo.InvariantCulture));
                    CaptureActive(card.gameObject);
                    list.Add(OwnCard(card.GetComponent<Renderer>(), false));
                }
            }
            foreach (var fsm in new[] { _bet, _hitPlayer, _hitHouse, _stand })
                if (fsm != null) CaptureActive(fsm.gameObject);
            _cardTextures = faces;
            return true;
        }

        private CardMesh OwnCard(Renderer renderer, bool created)
        {
            if (_gameConfig == null) throw new InvalidOperationException("Missing Ventti presentation bindings.");
            var original = renderer.sharedMaterials;
            var materials = (Material[])original.Clone();
            var face = new Material(materials[_gameConfig.MaterialIndex]);
            materials[_gameConfig.MaterialIndex] = face;
            renderer.sharedMaterials = materials;
            return new CardMesh { Renderer = renderer, Original = original, Face = face, Created = created };
        }

        private void CaptureActive(GameObject obj)
        {
            if (!_sceneActive.ContainsKey(obj)) _sceneActive.Add(obj, obj.activeSelf);
        }

        private void PresentGame()
        {
            var state = _gameReplica.Current;
            if (state == null || _gameConfig == null) { ClearInteraction(); return; }
            WriteProgress(state);
            if (_betValue != null) _betValue.Value = state.Stake;
            if (_playerHand != null) _playerHand.Value = state.PlayerTotal;
            if (_houseHand != null) _houseHand.Value = state.HouseTotal;
            if (_playerStage != null) _playerStage.Value = state.PlayerCards.Length;
            if (_houseStage != null) _houseStage.Value = state.HouseCards.Length;
            if (_resultStatus != null) _resultStatus.Value = state.Outcome == 0 ? string.Empty
                : _gameConfig[WinterMP.Core.Catalog.VenttiTableData.OutcomeBindings[state.Outcome - 1]];
            if (_gameHost)
            {
                if (_betCar != null) _betCar.Value = state.Wager == VenttiWager.Car;
                if (_betHouse != null) _betHouse.Value = state.Wager == VenttiWager.House;
            }
            bool closed = state.Phase == VenttiPhase.Closed;
            ShowControl(_bet, state.Phase == VenttiPhase.Betting);
            ShowControl(_hitPlayer, !closed);
            ShowControl(_stand, state.Phase == VenttiPhase.Playing);
            ShowCards(_playerCardsRoot, _playerMeshes, state.PlayerCards);
            ShowCards(_houseCardsRoot, _houseMeshes, state.HouseCards);
        }

        private static void ShowControl(PlayMakerFSM? fsm, bool active)
        {
            if (fsm != null && fsm.gameObject.activeSelf != active) fsm.gameObject.SetActive(active);
        }

        private void ShowCards(Transform? root, List<CardMesh> meshes, byte[] cards)
        {
            if (root == null || _cardTextures == null || _gameConfig == null) return;
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            while (meshes.Count < cards.Length)
            {
                // Native has nine meshes, but a legal low-card hand can be longer.
                // Extend only the mesh; no collider, FSM, or native deck object is cloned.
                var last = meshes[meshes.Count - 1].Renderer;
                var before = meshes[meshes.Count - 2].Renderer;
                var source = last.GetComponent<MeshFilter>();
                if (source == null) throw new InvalidOperationException("Missing Ventti card mesh.");
                var obj = new GameObject("WinterMP_Card_" + (meshes.Count + 1));
                obj.transform.parent = root;
                obj.transform.localPosition = last.transform.localPosition + (last.transform.localPosition - before.transform.localPosition);
                obj.transform.localRotation = last.transform.localRotation;
                obj.transform.localScale = last.transform.localScale;
                obj.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var renderer = obj.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = meshes[meshes.Count - 1].Original;
                meshes.Add(OwnCard(renderer, true));
            }
            for (int i = 0; i < meshes.Count; i++)
            {
                var mesh = meshes[i];
                if (mesh.Renderer == null) continue;
                bool visible = i < cards.Length;
                if (visible && mesh.Card != cards[i])
                {
                    mesh.Face.SetTexture(_gameConfig["textureProperty"], _cardTextures[cards[i]]);
                    mesh.Card = cards[i];
                }
                if (mesh.Renderer.gameObject.activeSelf != visible) mesh.Renderer.gameObject.SetActive(visible);
            }
        }

        private void ReadInput(SessionManager session)
        {
            ClearInteraction();
            var state = _gameReplica.Current;
            var camera = Camera.main;
            if (state == null || _gameConfig == null || _anchor == null || _interaction == null || _use == null
                || camera == null || Time.timeScale == 0
                || !GamblingSync.TryPlayerPosition(session, session.LocalPlayerId, out var position)
                || (position - _anchor.position).sqrMagnitude > 36f) return;
            if (!Physics.Raycast(camera.ScreenPointToRay(Input.mousePosition), out var hit, _gameConfig.PickDistance)) return;
            var target = hit.transform;
            bool bet = Under(target, _bet), draw = Under(target, _hitPlayer), stand = Under(target, _stand);
            if (!bet && !draw && !stand) return;
            string wager = state.Wager == VenttiWager.Car ? "SATSUMA" : state.Wager == VenttiWager.House ? "HOME"
                : state.Stake.ToString("0.##", CultureInfo.InvariantCulture) + " MK";
            string text = bet ? "CURRENT BET " + wager + " (LEFT + / RIGHT -)"
                : "YOU " + state.PlayerTotal + " / HOUSE " + state.HouseTotal + (stand ? " - STAND" : " - HIT");
            if (state.Phase == VenttiPhase.Resolved && _resultStatus != null) text = _resultStatus.Value;
            bool busy = state.PlayerId != VenttiLedgerState.NoPlayer && state.PlayerId != session.LocalPlayerId;
            if (busy) text += " - TABLE IN USE";
            if (_gameReplica.Pending != null) text += " - WAITING FOR HOST";
            _previousUse = _use.Value;
            _ownedInteraction = text; _interaction.Value = text; _use.Value = !busy;
            if (busy || _gameReplica.Pending != null) return;
            if (bet && Input.GetMouseButtonDown(1)) QueueGame(VenttiAction.Decrease);
            else if (Input.GetMouseButtonDown(0)) QueueGame(bet ? VenttiAction.Increase : stand ? VenttiAction.Stand
                : state.Phase == VenttiPhase.Resolved ? VenttiAction.NextHand : VenttiAction.Hit);
        }

        private static bool Under(Transform target, PlayMakerFSM? control)
        {
            if (control == null) return false;
            for (var current = target; current != null; current = current.parent)
                if (current == control.transform) return true;
            return false;
        }

        private void ClearInteraction()
        {
            if (_ownedInteraction.Length != 0 && _interaction != null && _interaction.Value == _ownedInteraction)
            {
                _interaction.Value = string.Empty;
                if (_use != null) _use.Value = _previousUse;
            }
            _ownedInteraction = string.Empty;
        }

        private void RestorePresentation()
        {
            ClearInteraction();
            foreach (var list in new[] { _playerMeshes, _houseMeshes })
            {
                foreach (var mesh in list)
                {
                    if (mesh.Renderer != null)
                    {
                        if (mesh.Created) UnityEngine.Object.Destroy(mesh.Renderer.gameObject);
                        else mesh.Renderer.sharedMaterials = mesh.Original;
                    }
                    if (mesh.Face != null) UnityEngine.Object.Destroy(mesh.Face);
                }
                list.Clear();
            }
            foreach (var pair in _sceneActive)
                if (pair.Key != null && pair.Key.activeSelf != pair.Value) pair.Key.SetActive(pair.Value);
            _sceneActive.Clear(); _cardTextures = null; _playerCardsRoot = _houseCardsRoot = null;
            _interaction = null; _use = null;
        }
    }
}
