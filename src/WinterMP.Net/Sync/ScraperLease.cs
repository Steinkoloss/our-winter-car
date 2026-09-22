using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>One shared native scraper, independent of vehicle or motion ownership.</summary>
    public sealed class ScraperLease
    {
        private readonly uint _epoch, _tool;
        private readonly Dictionary<byte, uint> _seen = new Dictionary<byte, uint>();
        private float _touched;
        public byte Holder { get; private set; } = 255;
        public bool Equipped { get; private set; }
        public ScraperLease(uint epoch, uint tool)
        {
            if (epoch == 0 || tool == 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            _epoch = epoch; _tool = tool;
        }
        public uint Seen(byte actor) => _seen.TryGetValue(actor, out uint n) ? n : 0;
        public float Age(float now) => now - _touched;
        public void Revoke(byte actor) { if (Holder == actor) { Holder = 255; Equipped = false; } }
        public void Expire(float now)
        {
            if (!PaneScrapePolicy.Finite(now) || Age(now) < 0 || Age(now) > PaneScrapePolicy.MaximumObservationAgeSeconds)
                Revoke(Holder);
        }
        // Consume an authenticated denial without touching equipment (wrong pane
        // or a reentrant bridge callback). It cannot become valid on replay.
        public bool RejectAttempt(byte sender, ScraperAction request)
        {
            if (sender == 255 || sender != request.Actor || request.Epoch != _epoch
                || request.Sequence == 0 || request.Sequence <= Seen(sender)) return false;
            _seen[sender] = request.Sequence;
            return true;
        }
        public bool Apply(byte sender, ScraperAction request, bool livePose, bool pickupContact, float now)
        {
            if (!RejectAttempt(sender, request)) return false;
            Expire(now);
            if (request.ToolId != _tool) return false;
            if (request.Operation == ScraperOperation.Drop)
            {
                if (Holder != sender) return false;
                Revoke(sender); return true;
            }
            if (!livePose || !PaneScrapePolicy.Finite(now)) { Revoke(sender); return false; }
            if (request.Operation == ScraperOperation.Pickup)
            {
                if (Holder != 255 || !pickupContact) return false;
                Holder = sender; Equipped = false; _touched = now; return true;
            }
            if (Holder != sender) return false;
            switch (request.Operation)
            {
                case ScraperOperation.Equip: Equipped = true; break;
                case ScraperOperation.Off: Equipped = false; break;
                case ScraperOperation.KeepAlive: break;
                case ScraperOperation.Stroke: if (!Equipped) return false; break;
                default: return false;
            }
            _touched = now; return true;
        }
    }
}
