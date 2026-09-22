using System;

namespace WinterMP.Net.Sync
{
    /// <summary>Native phone sheet inputs. UnpaidBills is a separate meter accumulator.</summary>
    public sealed class PhoneBillQuote
    {
        public float Minutes, MinutesLong, Connects, ConnectsLong;
        public float Base, ConnectionRate, MinuteRate, LongMinuteRate;

        public float LocalCharge => Connects * ConnectionRate + Minutes * MinuteRate;
        public float LongCharge => ConnectsLong * ConnectionRate + MinutesLong * LongMinuteRate;
        public float Total => Math.Min(999999f, (LocalCharge + LongCharge) + Base);

        public bool Valid => Nonnegative(Minutes) && Nonnegative(MinutesLong)
            && Nonnegative(Connects) && Nonnegative(ConnectsLong) && Nonnegative(Base)
            && Nonnegative(ConnectionRate) && Nonnegative(MinuteRate) && Nonnegative(LongMinuteRate)
            && Nonnegative(LocalCharge) && Nonnegative(LongCharge)
            && Nonnegative(LocalCharge + LongCharge + Base);

        private static bool Nonnegative(float value) => value >= 0 && !float.IsInfinity(value);

        public bool Same(PhoneBillQuote? other) => other != null && Minutes == other.Minutes
            && MinutesLong == other.MinutesLong && Connects == other.Connects && ConnectsLong == other.ConnectsLong
            && Base == other.Base && ConnectionRate == other.ConnectionRate
            && MinuteRate == other.MinuteRate && LongMinuteRate == other.LongMinuteRate;

        public PhoneBillQuote Copy() => new PhoneBillQuote { Minutes = Minutes, MinutesLong = MinutesLong,
            Connects = Connects, ConnectsLong = ConnectsLong, Base = Base, ConnectionRate = ConnectionRate,
            MinuteRate = MinuteRate, LongMinuteRate = LongMinuteRate };

        internal void Write(NetWriter w)
        {
            w.WriteSingle(Minutes); w.WriteSingle(MinutesLong); w.WriteSingle(Connects); w.WriteSingle(ConnectsLong);
            w.WriteSingle(Base); w.WriteSingle(ConnectionRate); w.WriteSingle(MinuteRate); w.WriteSingle(LongMinuteRate);
        }

        internal static PhoneBillQuote Read(NetReader r) => new PhoneBillQuote { Minutes = r.ReadSingle(),
            MinutesLong = r.ReadSingle(), Connects = r.ReadSingle(), ConnectsLong = r.ReadSingle(),
            Base = r.ReadSingle(), ConnectionRate = r.ReadSingle(), MinuteRate = r.ReadSingle(), LongMinuteRate = r.ReadSingle() };
    }
}
