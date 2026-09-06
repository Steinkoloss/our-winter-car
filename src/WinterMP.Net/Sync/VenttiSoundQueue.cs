using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Bounded live cues: binding delays may catch up briefly, snapshots never replay speech.</summary>
    public sealed class VenttiSoundQueue
    {
        public const int Capacity = 32;
        public const float LifetimeSeconds = 2f;
        private readonly List<PendingSound> _pending = new List<PendingSound>();
        private uint _sequence;
        private bool _received;
        public int Count => _pending.Count;
        private sealed class PendingSound
        {
            public VenttiSoundCue Cue = new VenttiSoundCue();
            public float Due;
        }

        public bool Receive(uint table, uint layout, int soundCount, VenttiSoundCue cue, float now)
        {
            if (cue.TableId != table || cue.LayoutId != layout || soundCount < 1 || soundCount > 256 || cue.Sound >= soundCount
                || !VenttiSceneReplica.PositionValid(cue.Position, 100000) || !BankTransferPolicy.IsFinite(cue.Delay)
                || cue.Delay < 0 || cue.Delay > 5 || !BankTransferPolicy.IsFinite(now) || now < 0) return false;
            if (_received && !VenttiSceneReplica.Forward(_sequence, cue.Sequence)) return false;
            Expire(now);
            if (_pending.Count == Capacity) _pending.RemoveAt(0);
            _pending.Add(new PendingSound
            {
                Due = now + cue.Delay,
                Cue = new VenttiSoundCue { TableId = cue.TableId, LayoutId = cue.LayoutId, Sequence = cue.Sequence,
                    Sound = cue.Sound, Position = cue.Position, Delay = cue.Delay },
            });
            _received = true; _sequence = cue.Sequence;
            return true;
        }

        public void Expire(float now)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
                if (now - _pending[i].Due >= LifetimeSeconds) _pending.RemoveAt(i);
        }

        public bool TryTake(float now, out VenttiSoundCue? cue, out float elapsed)
        {
            cue = null; elapsed = 0;
            if (!BankTransferPolicy.IsFinite(now) || now < 0) return false;
            Expire(now);
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Due > now) continue;
                cue = _pending[i].Cue; elapsed = now - _pending[i].Due;
                _pending.RemoveAt(i); return true;
            }
            return false;
        }
        public void Clear() { _pending.Clear(); _received = false; _sequence = 0; }
    }
}
