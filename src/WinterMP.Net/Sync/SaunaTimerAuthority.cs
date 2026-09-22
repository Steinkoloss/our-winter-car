using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public struct SaunaTimerValues
    {
        public float Timer, Time, KnobAngle;
        public bool Valid => Finite(Timer) && Timer >= 0 && Timer <= 120 && Finite(Time) && Time >= 0 && Time <= 720
            && Finite(KnobAngle) && KnobAngle >= 0 && KnobAngle <= 360;
        public static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
    }
    public struct SaunaTimerContact
    {
        public bool Present, Alive, Available, Contact;
        public float PoseAge, DistanceSquared;
        public bool Valid => Present && Alive && Available && Contact && SaunaTimerValues.Finite(PoseAge)
            && PoseAge >= 0 && PoseAge <= 2 && SaunaTimerValues.Finite(DistanceSquared) && DistanceSquared >= 0 && DistanceSquared <= 9;
    }
    public interface ISaunaTimerNative
    {
        SaunaTimerValues Read();
        SaunaTimerContact Observe(byte actor, NetVector3 eye, NetVector3 direction);
        // Execute exactly one audited +/- step and its native Wait propagation, not an arbitrary setter.
        void Turn(bool increase);
    }
    public sealed class SaunaTimerAuthority
    {
        public const string SourcePath = "YARD/Building/SAUNA/Sauna";
        public const string ButtonPath = "Kiuas/ButtonTime";
        public const string SteamPath = "Kiuas/StoveTrigger";
        public static readonly uint SourceId = StableHash.Fnv1a32(SourcePath);
        private readonly ISaunaTimerNative _native;
        private readonly uint _epoch;
        private readonly uint[] _seen = new uint[255];
        private SaunaTimerValues _last;
        private uint _revision;
        private bool _busy;
        public bool Faulted { get; private set; }
        public SaunaTimerAuthority(uint epoch, ISaunaTimerNative native)
        { if (epoch == 0) throw new ArgumentException("epoch"); _epoch = epoch; _native = native; }
        public uint Seen(byte actor) => actor < 255 ? _seen[actor] : 0;
        public static float Step(float current, bool increase) => Math.Max(1, Math.Min(120, current + (increase ? 10 : -10)));
        private SaunaTimerState Capture(byte actor, uint sequence, byte status)
        {
            var value = _native.Read();
            if (!value.Valid) throw new InvalidOperationException("Invalid native sauna timer.");
            if (_revision == 0 || value.Timer != _last.Timer || value.Time != _last.Time || value.KnobAngle != _last.KnobAngle)
            { if (_revision == uint.MaxValue) throw new InvalidOperationException("Sauna revision exhausted."); _revision++; _last = value; }
            return new SaunaTimerState { SourceId = SourceId, Epoch = _epoch, Revision = _revision, Actor = actor,
                Sequence = sequence, HighWater = Seen(actor), Status = status, Timer = value.Timer, Time = value.Time, KnobAngle = value.KnobAngle };
        }
        public void Publish(byte actor, Action<SaunaTimerState> publish)
        {
            if (_busy || Faulted) return;
            _busy = true;
            try { publish(Capture(actor, 0, SaunaTimerState.Observation)); }
            catch { Faulted = true; }
            finally { _busy = false; }
        }
        public bool Execute(byte authenticatedActor, SaunaTimerIntent intent, Action<SaunaTimerState> publish)
        {
            if (authenticatedActor == 255 || intent.Actor != authenticatedActor || intent.SourceId != SourceId || intent.Epoch != _epoch
                || intent.Sequence == 0 || intent.Sequence <= _seen[authenticatedActor]) return false;
            uint sequence = intent.Sequence, revision = intent.ExpectedRevision;
            float timer = intent.Timer; NetVector3 eye = intent.Eye, direction = intent.Direction;
            _seen[authenticatedActor] = sequence; // Reserve before observation/native/publication callbacks.
            if (_busy || Faulted) return false;
            _busy = true;
            try
            {
                var before = Capture(authenticatedActor, sequence, SaunaTimerState.Rejected);
                // Cold zero + Unscrew clamps upward to 1; the result's sign is not the native direction.
                bool increase = timer == Step(before.Timer, true);
                if (revision != before.Revision || !SaunaTimerValues.Finite(timer) || timer < 1 || timer > 120
                    || timer == before.Timer || timer != Step(before.Timer, increase)
                    || !_native.Observe(authenticatedActor, eye, direction).Valid)
                { publish(before); return false; }
                _native.Turn(increase);
                var result = Capture(authenticatedActor, sequence, SaunaTimerState.Accepted);
                // Unity's quaternion/Euler conversion may round the native local Y readback slightly.
                if (result.Timer != timer || result.Time != timer * 6 || Math.Abs(result.KnobAngle - timer) > 0.01f)
                    throw new InvalidOperationException("Partial sauna native turn; no retry.");
                publish(result); return true;
            }
            catch { Faulted = true; return false; }
            finally { _busy = false; }
        }
    }
    public sealed class SaunaTimerClient
    {
        private readonly byte _actor;
        private uint _sequence;
        public SaunaTimerState? Current { get; private set; }
        public SaunaTimerClient(byte actor) { if (actor == 255) throw new ArgumentException("actor"); _actor = actor; }
        public bool Receive(bool selectedHost, SaunaTimerState state)
        {
            if (!selectedHost || !state.Valid || (Current != null && (state.Epoch != Current.Epoch || state.Revision < Current.Revision))) return false;
            if (state.Actor == _actor) _sequence = Math.Max(_sequence, state.HighWater);
            Current = state.Copy(); return true;
        }
        public SaunaTimerIntent? Create(bool increase, NetVector3 eye, NetVector3 direction)
        {
            if (Current == null || _sequence == uint.MaxValue) return null;
            return new SaunaTimerIntent { SourceId = SaunaTimerAuthority.SourceId, Epoch = Current.Epoch,
                Actor = _actor, Sequence = ++_sequence, ExpectedRevision = Current.Revision,
                Timer = SaunaTimerAuthority.Step(Current.Timer, increase), Eye = eye, Direction = direction };
        }
    }
}
