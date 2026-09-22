using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class PartToolScrewPolicy
    {
        public const float ToolSize = .55f, ToolTolerance = .02f, Cooldown = .08f;
        public static bool IsTurn(PartFitOperation operation) => operation == PartFitOperation.ToolTighten || operation == PartFitOperation.ToolLoosen;
        public static bool MatchesTool(float size) => System.Math.Abs(size - ToolSize) <= ToolTolerance;
        public static bool TryTurn(float current, PartFitOperation operation, out float result)
        {
            result = current;
            return IsTurn(operation) && PartHandScrewPolicy.TryTurn(current,
                operation == PartFitOperation.ToolTighten ? PartFitOperation.HandTighten : PartFitOperation.HandLoosen, out result);
        }
        public static PartFitStatus Check(PartFitRequest request, ReplacementPartState? state, int index,
            bool available, bool nearby, bool mountReady, bool busy)
        {
            if (request == null || !IsTurn(request.Operation)) return PartFitStatus.Unavailable;
            var turn = PartFitLedger.Copy(request);
            turn.Operation = request.Operation == PartFitOperation.ToolTighten ? PartFitOperation.HandTighten : PartFitOperation.HandLoosen;
            return PartHandScrewPolicy.Check(turn, state, index, available, nearby, mountReady, busy);
        }
    }
}
