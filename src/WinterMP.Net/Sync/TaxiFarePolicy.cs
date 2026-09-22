using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class TaxiFarePolicy
    {
        public static readonly uint ReceiptItemId = StableHash.Fnv1a32("taxi:receipt");
        public static bool Valid(TaxiFareState s)
        {
            if (s == null || !Amount(s.QuotedCost) || !Amount(s.OfferedCost) || !Text(s.TerminalDisplay) || !Text(s.OfferLabel)) return false;
            if (s.FareId == 0 && (s.Flags & ~TaxiFareState.Active) != 0) return false;
            if ((s.Flags & TaxiFareState.CanCharge) != 0 && ((s.Flags & (TaxiFareState.Active | TaxiFareState.Arrived)) != (TaxiFareState.Active | TaxiFareState.Arrived)
                || (s.Flags & TaxiFareState.Charged) != 0)) return false;
            if ((s.Flags & TaxiFareState.CanCollect) != 0 && ((s.Flags & (TaxiFareState.Active | TaxiFareState.CashVisible | TaxiFareState.Charged)) != (TaxiFareState.Active | TaxiFareState.CashVisible | TaxiFareState.Charged)
                || (s.Flags & TaxiFareState.Paid) != 0)) return false;
            if ((byte)s.ReceiptStage > 5 || (s.ReceiptFlags & ~31) != 0 || !Pose(s)) return false;
            if (s.FareId == 0 && (s.ReceiptFlags != 0 || s.ReceiptStage != TaxiReceiptStage.Hidden)) return false;
            if ((s.ReceiptFlags & TaxiFareState.CanPrint) != 0 && ((s.Flags & (TaxiFareState.Active | TaxiFareState.Charged)) != (TaxiFareState.Active | TaxiFareState.Charged) || s.ReceiptStage != TaxiReceiptStage.Hidden)) return false;
            if ((s.ReceiptFlags & TaxiFareState.CanTake) != 0 && ((s.Flags & TaxiFareState.Active) == 0 || s.ReceiptStage != TaxiReceiptStage.Ready)) return false;
            if ((s.ReceiptFlags & TaxiFareState.CanGive) != 0 && ((s.Flags & (TaxiFareState.Active | TaxiFareState.Paid | TaxiFareState.ReceiptRequested)) != (TaxiFareState.Active | TaxiFareState.Paid | TaxiFareState.ReceiptRequested) || s.ReceiptStage != TaxiReceiptStage.Loose)) return false;
            return true;
        }
        private static bool Pose(TaxiFareState s)
        {
            var p = s.ReceiptPosition; var q = s.ReceiptRotation;
            if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z) || !Finite(q.X) || !Finite(q.Y) || !Finite(q.Z) || !Finite(q.W)) return false;
            float length = q.X*q.X + q.Y*q.Y + q.Z*q.Z + q.W*q.W;
            return length > .5f && length < 1.5f;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Amount(float value) => value >= 0 && value <= 10000000;
        private static bool Text(string value) => value != null && value.Length <= 128 && value.IndexOf('\0') < 0;
        public static bool CanAct(TaxiFareState s, TaxiFareIntent intent, byte actor, bool near)
        {
            if (!Valid(s) || intent == null || actor == byte.MaxValue || actor != intent.PlayerId || !near
                || intent.Sequence == 0 || intent.FareId == 0 || intent.FareId != s.FareId || intent.ExpectedControlRevision != s.ControlRevision) return false;
            return intent.Action == TaxiFareAction.Charge ? (s.Flags & TaxiFareState.CanCharge) != 0
                : intent.Action == TaxiFareAction.Collect ? (s.Flags & TaxiFareState.CanCollect) != 0
                : intent.Action == TaxiFareAction.PrintReceipt ? (s.ReceiptFlags & TaxiFareState.CanPrint) != 0
                : intent.Action == TaxiFareAction.TakeReceipt ? (s.ReceiptFlags & TaxiFareState.CanTake) != 0
                : intent.Action == TaxiFareAction.GiveReceipt && (s.ReceiptFlags & TaxiFareState.CanGive) != 0;
        }
    }
}
