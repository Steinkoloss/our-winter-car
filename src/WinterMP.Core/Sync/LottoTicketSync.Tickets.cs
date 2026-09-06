using System;
using System.Collections;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class LottoTicketSync
    {
        private void Discover(SessionManager session)
        {
            if (_c == null || _prefab == null || _saveId == null || _counter == null) return;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var use = obj as PlayMakerFSM;
                if (use == null || use.FsmName != _c["useFsm"] || use.gameObject.name != _c["ticketName"]
                    || use.GetComponent<Rigidbody>() == null || !use.Fsm.Started || use.ActiveStateName != _c["useIdle"]) continue;
                bool known = false;
                foreach (var t in _tickets.Values) if (t.Object == use.gameObject) { known = true; break; }
                if (known || _hiddenLocalTickets.Contains(use.gameObject)) continue;
                string id = String(use, _c["ticketId"]).Value;
                if (!id.StartsWith(_prefab.name, StringComparison.Ordinal)
                    || !int.TryParse(id.Substring(_prefab.name.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                    || number < 1 || number > _counter.Value) continue;
                if (!session.IsHost)
                {
                    if (use.gameObject.activeSelf) { _hiddenLocalTickets.Add(use.gameObject); use.gameObject.SetActive(false); }
                    continue;
                }
                if (_tickets.ContainsKey(id)) throw new InvalidOperationException("Duplicate persistent Lotto ID: " + id);
                var ticket = Bind(use.gameObject, id, false);
                if (ticket == null || ticket.Round.Value == 8888) continue;
                var state = Read(ticket);
                if (state == null) continue;
                ticket.State = state; _tickets.Add(id, ticket); Register(ticket);
            }
        }

        private Ticket? Bind(GameObject obj, string id, bool guest)
        {
            if (_c == null) return null;
            var use = Fsm(obj, _c["useFsm"]); var data = Fsm(obj, _c["dataFsm"]);
            var lines = Lines(use);
            if (lines == null) return null;
            foreach (string key in new[] { "destroyState", "useIdle", "loadState", "saveState", "deleteState" }) State(use, _c[key]);
            State(data, _c["dataIdle"]);
            return new Ticket
            {
                Object = obj, Use = use, Data = data, Lines = lines, GuestClone = guest,
                Round = Int(use, _c["ticketRound"]), Winnings = Float(data.FsmVariables, _c["winnings"]),
                UseWinnings = Float(use.FsmVariables, _c["winnings"]), State = new LottoTicketState { TicketId = id },
            };
        }

        private string? CreateHostTicket(LottoTicketRequest request)
        {
            if (_c == null || _spawner == null || _prefab == null || _spawn == null || _counter == null || _saveId == null
                || !_spawner.Fsm.Started || _spawner.ActiveStateName != _c["spawnerIdle"]
                || _saveId.Value != _prefab.name || _counter.Value < 0 || _counter.Value == int.MaxValue) return null;
            string id = _saveId.Value + (_counter.Value + 1).ToString(CultureInfo.InvariantCulture);
            if (!LottoTicketLedger.ValidId(id) || _tickets.ContainsKey(id) || _ledger.IsRetired(id)) return null;
            GameObject? obj = null;
            try
            {
                // The native factory copies from the single shared form. Instantiate its
                // exact prefab with captured rows instead, leaving an open host form intact.
                obj = (GameObject)UnityEngine.Object.Instantiate(_prefab, _spawn.transform.position, _spawn.transform.rotation);
                obj.name = id;
                var ticket = Bind(obj, id, false);
                if (ticket == null) { UnityEngine.Object.Destroy(obj); return null; }
                Configure(ticket, request.Round, request.Numbers, 0);
                if (!FsmHook.EnsureRemoteEntry(ticket.Use, _c["destroyState"])) throw new InvalidOperationException("Missing ticket retirement.");
                ticket.State = new LottoTicketState { TicketId = id, Round = request.Round, Numbers = (byte[])request.Numbers.Clone(),
                    Position = obj.transform.position.ToNet(), Rotation = obj.transform.rotation.ToNet() };
                if (!LottoTicketReplica.Valid(ticket.State)) throw new InvalidOperationException("Invalid native Lotto spawn pose.");
                _tickets.Add(id, ticket);
                _counter.Value++;
                obj.SetActive(true);
                return id;
            }
            catch
            {
                _tickets.Remove(id);
                if (obj != null) UnityEngine.Object.Destroy(obj);
                throw;
            }
        }

        private void Configure(Ticket ticket, int round, byte[] numbers, float winnings)
        {
            if (_c == null) return;
            ticket.Round.Value = round;
            ticket.Winnings.Value = ticket.UseWinnings.Value = winnings;
            ObjectVar(ticket.Data, _c["ticketDatabase"]).Value = _database;
            ObjectVar(ticket.Use, _c["ticketPaper"]).Value = _paper;
            for (int line = 0; line < 3; line++)
            {
                ticket.Lines[line].Clear();
                for (int i = 0; i < 7; i++) ticket.Lines[line].Add((int)numbers[line * 7 + i]);
            }
        }

        private LottoTicketState? Read(Ticket ticket)
        {
            var state = LottoTicketReplica.Copy(ticket.State);
            if (ticket.Object == null || ticket.Round == null || ticket.Round.Value == 8888 || _ledger.IsRetired(state.TicketId))
            {
                state.Retired = true; _ledger.Retire(state.TicketId);
                ticket.State.Retired = true; _items.UnregisterTicket(state.TicketId); ticket.Registered = false;
                return state;
            }
            if (_c == null || ticket.Data.ActiveStateName != _c["dataIdle"]) return null;
            state.Round = ticket.Round.Value; state.Winnings = ticket.Winnings.Value;
            for (int line = 0; line < 3; line++)
            {
                if (ticket.Lines[line].Count != 7) return null;
                for (int i = 0; i < 7; i++)
                {
                    if (!(ticket.Lines[line][i] is int n) || n < 0 || n > 39) return null;
                    state.Numbers[line * 7 + i] = (byte)n;
                }
            }
            state.Position = ticket.Object.transform.position.ToNet(); state.Rotation = ticket.Object.transform.rotation.ToNet();
            if (!LottoTicketReplica.Valid(state)) return null;
            ticket.State = LottoTicketReplica.Copy(state);
            return state;
        }

        private void Register(Ticket ticket)
        {
            if (ticket.Registered || ticket.State.Retired || ticket.Object == null || _c == null
                || ticket.Use.ActiveStateName != _c["useIdle"]) return;
            var body = ticket.Object.GetComponent<Rigidbody>();
            if (body == null) return;
            _items.RegisterTicket(ticket.State.TicketId, body); ticket.Registered = true;
        }

        private void ApplyPending()
        {
            if (_c == null || _prefab == null) return;
            foreach (var state in _replica.States)
            {
                _tickets.TryGetValue(state.TicketId, out var ticket);
                if (ticket != null && (ticket.Object == null
                    || (!state.Retired && ticket.Object.GetComponent<Rigidbody>() == null)))
                {
                    _items.UnregisterTicket(state.TicketId);
                    if (ticket.Object != null) UnityEngine.Object.Destroy(ticket.Object);
                    _tickets.Remove(state.TicketId); ticket = null;
                }
                if (ticket == null)
                {
                    if (state.Retired) continue;
                    var obj = (GameObject)UnityEngine.Object.Instantiate(_prefab, state.Position.ToUnity(), state.Rotation.ToUnity());
                    obj.name = state.TicketId;
                    try
                    {
                        ticket = Bind(obj, state.TicketId, true);
                        if (ticket == null) { UnityEngine.Object.Destroy(obj); continue; }
                        // A guest replica must never read a same-ID ticket from its own save,
                        // write host ticket keys to that save, or calculate prizes locally.
                        State(ticket.Use, _c["loadState"]).Actions = new FsmStateAction[] { new FsmHookAction(() => { }) };
                        State(ticket.Use, _c["saveState"]).Actions = new FsmStateAction[0];
                        State(ticket.Use, _c["deleteState"]).Actions = new FsmStateAction[0];
                        ticket.Data.enabled = false;
                        Configure(ticket, state.Round, state.Numbers, state.Winnings);
                        _tickets.Add(state.TicketId, ticket); obj.SetActive(true);
                    }
                    catch { UnityEngine.Object.Destroy(obj); throw; }
                }
                if (ticket.Object == null) continue;
                if (ticket.HasApplied && ticket.AppliedSequence == state.Sequence) continue;
                ticket.State = LottoTicketReplica.Copy(state);
                if (state.Retired)
                {
                    _items.UnregisterTicket(state.TicketId); ticket.Registered = false;
                    ticket.Object.SetActive(false);
                }
                else
                {
                    ticket.Data.enabled = false;
                    ticket.Winnings.Value = ticket.UseWinnings.Value = state.Winnings;
                }
                ticket.HasApplied = true; ticket.AppliedSequence = state.Sequence;
            }
        }

        private void RetireNative(Ticket ticket)
        {
            _items.UnregisterTicket(ticket.State.TicketId); ticket.Registered = false;
            if (_c == null || ticket.Use == null) return;
            if (!FsmHook.EnsureRemoteEntry(ticket.Use, _c["destroyState"])) throw new InvalidOperationException("Missing Lotto garbage state.");
            if (ticket.Use.gameObject.activeInHierarchy) FsmHook.FireRemoteEntry(ticket.Use, _c["destroyState"]);
        }

        private bool OnItemDiscard(uint itemId)
        {
            foreach (var ticket in _tickets.Values)
            {
                if (LottoTicketReplica.ItemId(ticket.State.TicketId) != itemId) continue;
                try
                {
                    var session = SessionManager.Instance;
                    if (session == null) return false;
                    ticket.State.Retired = true;
                    _items.UnregisterTicket(ticket.State.TicketId); ticket.Registered = false;
                    if (session.IsHost)
                    {
                        // Keep Use alive for SAVEGAME's native key deletion. A generic
                        // Destroy(gameObject) would resurrect a saved ticket next load.
                        _ledger.Retire(ticket.State.TicketId);
                        ticket.Round.Value = 8888; ticket.Winnings.Value = ticket.UseWinnings.Value = 0;
                        RetireNative(ticket); _sendAt = 0;
                    }
                    else if (ticket.Object != null) ticket.Object.SetActive(false);
                }
                catch (Exception e) { Disable(e); }
                return true;
            }
            return false;
        }
    }
}
