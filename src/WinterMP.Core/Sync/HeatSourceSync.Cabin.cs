using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class HeatSourceSync
    {
        private Source? _cabin;
        private CabinFuelHost? _cabinHost;
        private WoodstoveFuelAuthority? _fuelAuthority;
        private WoodstoveFuelClient? _fuelClient;
        private uint _fuelEpoch, _fuelSequence;
        private bool _cabinFailed, _cabinPublishing;
        private float _nextCabinState;
        private readonly Queue<uint> _localFeeds = new Queue<uint>();
        private readonly Dictionary<uint, CabinWood> _cabinWood = new Dictionary<uint, CabinWood>();
        private readonly Dictionary<uint, WoodstoveFuelUpdate> _pendingWood = new Dictionary<uint, WoodstoveFuelUpdate>();
        private readonly Dictionary<FsmState, FsmStateAction[]> _cabinActions = new Dictionary<FsmState, FsmStateAction[]>();
        private readonly Dictionary<FsmStateAction, bool> _cabinEnabled = new Dictionary<FsmStateAction, bool>();
        private GameObject? _logPrefab;
        private float _nextWoodScan;
        private sealed class CabinWood
        {
            public uint Id;
            public byte Shape;
            public GameObject Piece = null!;
            public SyncedItem Item = null!;
            public bool Replica;
            public float NextRequest;
        }
        private static readonly RNGCryptoServiceProvider CabinRandom = new RNGCryptoServiceProvider();
        private static uint NewCabinId()
        {
            var bytes = new byte[4];
            do { CabinRandom.GetBytes(bytes); } while (BitConverter.ToUInt32(bytes, 0) == 0);
            return BitConverter.ToUInt32(bytes, 0);
        }
        private void BindCabin(Source source)
        {
            if (source.Id != WoodstoveFuelAuthority.CabinSourceId || _cabin != null || _cabinFailed) return;
            var session = SessionManager.Instance;
            var c = SyncCatalog.WoodstoveFuel;
            if (session == null || c == null || source.WoodTrigger == null || source.SetFire == null) return;
            var trigger = source.WoodTrigger;
            if (!trigger.Fsm.Started || !trigger.Fsm.Initialized) return;
            // SetFire is asset-inactive. Init only deserializes its action table, not its simulation.
            if (!source.SetFire.Fsm.Initialized) source.SetFire.Fsm.Init(source.SetFire);
            try
            {
                source.Woods = trigger.FsmVariables.FindFsmInt("Woods");
                if (source.Woods == null || source.Hiillos == null || source.HeatingEfficiency == null)
                    throw new InvalidOperationException("Cabin canonical variables missing.");
                var entry = RequireCabinState(trigger, "State 1", "IntCompare");
                var destroy = RequireCabinState(trigger, "Destroy firewood", "GetRandomChild", "ActivateGameObject", "ActivateGameObject",
                    "DestroyObject", "IntAdd", "IntCompare", "IntCompare", "IntCompare", "IntCompare");
                ValidateCabinActions(trigger, entry, destroy);
                if (!FsmHook.EnsureRemoteEntry(trigger, "Wait wood") || !FsmHook.EnsureRemoteEntry(trigger, "Destroy firewood"))
                    throw new InvalidOperationException("Cabin native entry unavailable.");
                _logPrefab = FindCabinLogPrefab(c);
                if (_logPrefab == null) return; // No suppression until the resource replica route exists.
                _cabin = source;
                _cabinHost = new CabinFuelHost(this, source);
                _woodContact = trigger.gameObject.AddComponent<CabinWoodContact>();
                if (session.IsHost)
                {
                    _fuelEpoch = NewCabinId(); _fuelAuthority = new WoodstoveFuelAuthority(_fuelEpoch, _cabinHost);
                }
                _cabinActions.Add(entry, entry.Actions);
                entry.Actions = new FsmStateAction[] { new FsmHookAction(() =>
                {
                    var piece = trigger.FsmVariables.FindFsmGameObject("Collider")?.Value;
                    if (piece != null) QueueCabinContact(piece);
                    FsmHook.FireRemoteEntry(trigger, "Wait wood");
                }) };
                foreach (var action in destroy.Actions) DisableCabinAction(action);
                if (!session.IsHost)
                {
                    var remove = RequireCabinState(source.SetFire, "Remove wood", "IntAdd", "SetFsmInt");
                    foreach (var action in remove.Actions) DisableCabinAction(action);
                }
                WinterMPPlugin.Log.LogInfo("Cabin fuel authority bound; native runtime verification remains a separate gate.");
            }
            catch (Exception e) { FailCabin(e); }
        }
        private void DisableCabinAction(FsmStateAction action)
        {
            if (!_cabinEnabled.ContainsKey(action)) _cabinEnabled.Add(action, action.Enabled);
            action.Enabled = false;
        }
        private void QueueCabinContact(GameObject piece)
        {
            if (_cabinFailed) return;
            foreach (var wood in _cabinWood.Values)
            {
                if (wood.Piece != piece || Time.unscaledTime < wood.NextRequest) continue;
                wood.NextRequest = Time.unscaledTime + .75f;
                _localFeeds.Enqueue(wood.Id); return;
            }
        }
        private void UpdateCabin(SessionManager session)
        {
            if (_cabin == null || _cabinFailed || _cabinPublishing) return;
            try
            {
                if (Time.unscaledTime >= _nextWoodScan)
                {
                    _nextWoodScan = Time.unscaledTime + .5f;
                    if (session.IsHost) ScanCabinWood(); else MaterializeCabinWood();
                }
                if (session.IsHost)
                {
                    var final = _fuelAuthority!.Poll();
                    if (final != null) PublishCabinDecision(final);
                    if (!_fuelAuthority.Pending && !_fuelAuthority.Faulted && Time.unscaledTime >= _nextCabinState)
                    {
                        _nextCabinState = Time.unscaledTime + 1f;
                        PublishCabinState();
                    }
                }
                while (_localFeeds.Count > 0)
                {
                    uint id = _localFeeds.Dequeue();
                    if (_fuelEpoch == 0 || _fuelSequence == uint.MaxValue) continue;
                    // A host standing elsewhere must not impersonate the guest whose release made contact.
                    if (session.IsHost)
                    {
                        var observation = _cabinHost!.Observe(session.LocalPlayerId, id);
                        if (!observation.ResourceAvailableToActor || !observation.ActorPresent
                            || observation.DistanceSquared > WoodstoveFuelAuthority.MaxActorDistanceSquared) continue;
                    }
                    var intent = session.IsHost ? new WoodstoveFeedIntent { SourceId = WoodstoveFuelAuthority.CabinSourceId,
                        Epoch = _fuelEpoch, Actor = session.LocalPlayerId, Sequence = ++_fuelSequence, ResourceId = id }
                        : _fuelClient?.Create(id);
                    if (intent == null) continue;
                    if (session.IsHost) OnCabinIntent(intent, session.LocalPlayerId);
                    else session.SendWorldMessage(intent, Channel.ReliableOrdered);
                }
                if (!session.IsHost && _fuelClient?.Current != null) ApplyCabinValues(_fuelClient.Current);
            }
            catch (Exception e) { FailCabin(e); }
        }
        internal void OnCabinIntent(WoodstoveFeedIntent message, byte authenticatedActor)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || _fuelAuthority == null || _cabinFailed) return;
            if (_cabinPublishing)
            {
                // Publication callbacks cannot start another feed or recursively
                // publish a denial. Retain its sequence so retry cannot spend it.
                _fuelAuthority.RejectBusy(authenticatedActor, message.Request());
                return;
            }
            PublishCabinDecision(_fuelAuthority.Decide(authenticatedActor, message.Request()));
        }
        private void PublishCabinDecision(WoodstoveFeedDecision decision)
        {
            _cabinPublishing = true;
            try
            {
                if (decision.Actor == SessionManager.Instance!.LocalPlayerId)
                    _fuelSequence = Math.Max(_fuelSequence, decision.HighWater);
                SessionManager.Instance.SendWorldMessage(new WoodstoveFuelUpdate { Epoch = _fuelEpoch,
                    Snapshot = decision.Snapshot, Actor = decision.Actor, Sequence = decision.Sequence,
                    HighWater = decision.HighWater, IsDecision = true, Status = decision.Status }, Channel.ReliableOrdered);
                if (decision.Status == WoodstoveFeedStatus.Accepted && decision.Snapshot != null)
                    RetireCabinPieces(decision.Snapshot);
            }
            finally { _cabinPublishing = false; }
        }
        private void PublishCabinState()
        {
            _cabinPublishing = true;
            try
            {
                var session = SessionManager.Instance!; var snapshot = _fuelAuthority!.Capture();
                // Per-actor admission restores high-water on a new client without clearing the host ledger.
                foreach (var player in session.Players)
                    session.SendWorldMessage(new WoodstoveFuelUpdate { Snapshot = snapshot, Actor = player.PlayerId,
                        HighWater = _fuelAuthority.Seen(player.PlayerId) }, Channel.ReliableOrdered);
                foreach (var wood in _cabinWood.Values)
                {
                    if (wood.Piece == null) continue;
                    session.SendWorldMessage(new WoodstoveFuelUpdate { Snapshot = snapshot, ResourceId = wood.Id,
                        Shape = wood.Shape, Position = wood.Piece.transform.position.ToNet(),
                        Rotation = wood.Piece.transform.rotation.ToNet() }, Channel.ReliableOrdered);
                }
            }
            finally { _cabinPublishing = false; }
        }
        internal void OnCabinUpdate(WoodstoveFuelUpdate message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return; // SessionManager authenticated the selected host.
            if (_fuelClient == null) _fuelClient = new WoodstoveFuelClient(session.LocalPlayerId);
            bool accepted = _fuelClient.Receive(true, message);
            _fuelEpoch = _fuelClient.Epoch;
            if (!accepted || message.Snapshot == null) return;
            if (message.ResourceId != 0) _pendingWood[message.ResourceId] = message;
            if (_cabin == null) return;
            ApplyCabinValues(message.Snapshot);
        }
        private void ApplyCabinValues(WoodstoveFuelSnapshot snapshot)
        {
            if (_cabin == null) return;
            WriteInt(_cabin.Woods, snapshot.Values.Fuel);
            WriteInt(_cabin.SetFire!.FsmVariables.FindFsmInt("Woods"), snapshot.Values.Fuel);
            WriteFloat(_cabin.HeatingEfficiency, snapshot.Values.Heat); WriteBool(_cabin.Hiillos, snapshot.Values.Lit);
            if (snapshot.Values.Fuel > 0 && !_cabin.SetFire.gameObject.activeSelf) _cabin.SetFire.gameObject.SetActive(true);
            for (int i = 1; i <= 4; i++)
            {
                var visual = _cabin.Anchor!.Find("Woods/log" + i);
                if (visual != null) visual.gameObject.SetActive(i <= snapshot.Values.Fuel);
            }
            RetireCabinPieces(snapshot);
        }
        private void RetireCabinPieces(WoodstoveFuelSnapshot snapshot)
        {
            foreach (uint id in snapshot.ConsumedResources)
            {
                _pendingWood.Remove(id);
                if (!_cabinWood.TryGetValue(id, out var wood)) continue;
                WorldSyncManager.Instance!.ItemSync.UnbindCabinWood(id);
                if (!SessionManager.Instance!.IsHost && wood.Piece != null) UnityEngine.Object.Destroy(wood.Piece);
            }
        }
        private void FailCabin(Exception error)
        {
            _cabinFailed = true;
            WinterMPPlugin.Log.LogError("Cabin fuel disabled without native retry: " + error);
        }
        private void ClearCabin()
        {
            if (_woodContact != null) UnityEngine.Object.Destroy(_woodContact);
            _woodContact = null;
            foreach (var pair in _cabinActions) pair.Key.Actions = pair.Value;
            foreach (var pair in _cabinEnabled) pair.Key.Enabled = pair.Value;
            foreach (var wood in _cabinWood.Values)
            {
                WorldSyncManager.Instance?.ItemSync.UnbindCabinWood(wood.Id);
                if (wood.Replica && wood.Piece != null) UnityEngine.Object.Destroy(wood.Piece);
            }
            _cabinActions.Clear(); _cabinEnabled.Clear(); _cabinWood.Clear(); _pendingWood.Clear(); _localFeeds.Clear();
            _cabin = null; _cabinHost = null; _fuelAuthority = null; _fuelClient = null; _logPrefab = null;
            _fuelEpoch = 0; _fuelSequence = 0; _cabinFailed = false; _cabinPublishing = false; _nextCabinState = 0; _nextWoodScan = 0;
        }
    }
}
