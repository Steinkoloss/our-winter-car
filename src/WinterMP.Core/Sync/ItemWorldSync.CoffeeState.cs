using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private CoffeeState CaptureCoffee(CoffeeBinding b)
        {
            if (++b.Revision == 0) ++b.Revision;
            byte flags = b.Cap?.Value == true ? (byte)1 : (byte)0;
            if (b.Kind == 0 && b.Body.transform.Find(SyncCatalog.Coffee!["sound"]).gameObject.activeSelf) flags |= 2;
            return new CoffeeState { ItemId = b.Id, Revision = b.Revision, Kind = b.Kind, Flags = flags,
                Water = Mathf.Clamp(b.Water?.Value ?? 0, 0, 2), Ground = Mathf.Clamp(b.Ground?.Value ?? 0, 0, b.Kind == 2 ? 100 : 26),
                Coffee = Mathf.Clamp(b.Coffee?.Value ?? 0, 0, b.Kind == 1 ? .3f : 2), Caffeine = Mathf.Clamp(b.Caffeine?.Value ?? 0, 0, 1.5f),
                BoilVolume = Mathf.Clamp(b.Volume?.Value ?? 0, 0, .6f), Position = b.Body.transform.position.ToNet(), Rotation = b.Body.transform.rotation.ToNet() };
        }
        private void BroadcastCoffee(SessionManager session, CoffeeBinding b)
        {
            b.Sent = CaptureCoffee(b); b.NextSend = Time.unscaledTime + 2;
            session.SendWorldMessage(b.Sent, Channel.ReliableOrdered);
        }
        internal IEnumerable<CoffeeState> BuildCoffeeStates()
        {
            if (_coffeeFailed) yield break;
            foreach (var b in _coffee.Values)
                if (b.Body != null && !_spawnLifecycle.IsRetired(b.Id)) yield return CaptureCoffee(b);
        }
        internal CoffeeState? BuildCoffeeState(uint id) => !_coffeeFailed && _coffee.TryGetValue(id, out var b) && b.Body != null && !_spawnLifecycle.IsRetired(id) ? CaptureCoffee(b) : null;
        internal void OnCoffeeState(CoffeeState state)
        {
            if (_coffeeFailed || SessionManager.Instance?.IsHost != false || !CoffeePolicy.Valid(state) || _spawnLifecycle.IsRetired(state.ItemId)) return;
            if (_pendingCoffee.TryGetValue(state.ItemId, out var old) && !TractorTrailerPolicy.Newer(state.Revision, old.Revision)) return;
            _pendingCoffee[state.ItemId] = state;
        }
        private void ApplyPendingCoffee()
        {
            var done = new List<uint>();
            foreach (var pair in _pendingCoffee)
            {
                var state = pair.Value;
                if (_spawnLifecycle.IsRetired(pair.Key)) { done.Add(pair.Key); continue; }
                if (!_coffee.TryGetValue(pair.Key, out var b))
                {
                    if (state.Kind != 2 || !MaterializeCoffeePacket(state)) continue;
                    b = _coffee[pair.Key];
                }
                if (b.Body == null) { done.Add(pair.Key); continue; }
                if (b.Kind != state.Kind) throw new InvalidOperationException("Coffee state targets another item kind.");
                if (b.Received == null || TractorTrailerPolicy.Newer(state.Revision, b.Received.Revision))
                {
                    bool lidChanged = b.Received == null || b.Cap?.Value != ((state.Flags & 1) != 0);
                    b.Received = state;
                    if (b.Water != null) b.Water.Value = state.Water;
                    if (b.Ground != null) b.Ground.Value = state.Ground;
                    if (b.Coffee != null) b.Coffee.Value = state.Coffee;
                    if (b.Caffeine != null) b.Caffeine.Value = state.Caffeine;
                    if (b.Volume != null) b.Volume.Value = state.BoilVolume;
                    if (b.Kind == 0)
                    {
                        if (lidChanged) SetCoffeeLid((state.Flags & 1) != 0);
                        var sound = b.Body.transform.Find(SyncCatalog.Coffee!["sound"]).gameObject;
                        sound.SetActive((state.Flags & 2) != 0); sound.GetComponent<AudioSource>().volume = state.BoilVolume;
                    }
                    PresentCoffee(b);
                }
                done.Add(pair.Key);
            }
            foreach (uint id in done) _pendingCoffee.Remove(id);
        }
        private void PresentCoffee(CoffeeBinding b)
        {
            var c = SyncCatalog.Coffee!;
            if (b.Kind == 2) { b.Body.name = b.Ground!.Value < 1 ? "empty(itemx)" : c["packetName"]; return; }
            if (b.Kind == 0)
            {
                if (_coffeePotMaterial != null) _coffeePotMaterial.color = new Color(.463f, .105f, 0, Mathf.Clamp(b.Caffeine!.Value, 0, 1.5f));
            }
            else
            {
                float amount = Mathf.Clamp(b.Coffee!.Value, 0, .3f);
                var surface = b.Body.transform.Find(c["cupSurface"]); var pos = surface.localPosition; pos.z = amount / 3.75f; surface.localPosition = pos;
                b.Fsm.FsmVariables.FindFsmFloat("Pos").Value = pos.z;
                if (_coffeeCupMaterial != null) _coffeeCupMaterial.color = new Color(.463f, .105f, 0, Mathf.Clamp(b.Caffeine!.Value, 0, 1.5f));
            }
        }
    }
}
