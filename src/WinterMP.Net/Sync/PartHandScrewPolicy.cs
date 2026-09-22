using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class PartHandScrewPolicy
    {
        public const float MinimumTightness = 0, MaximumTightness = 8, TightnessStep = 1;

        public static bool IsTurn(PartFitOperation operation) => operation == PartFitOperation.HandTighten
            || operation == PartFitOperation.HandLoosen;

        public static bool ValidTightness(float value) => !float.IsNaN(value) && !float.IsInfinity(value)
            && value >= MinimumTightness && value <= MaximumTightness && value == Math.Floor(value);

        public static bool TryTurn(float current, PartFitOperation operation, out float result)
        {
            result = current;
            if (!IsTurn(operation) || !ValidTightness(current)) return false;
            float next = current + (operation == PartFitOperation.HandTighten ? TightnessStep : -TightnessStep);
            if (!ValidTightness(next)) return false;
            result = next;
            return true;
        }

        public static PartFitStatus Check(PartFitRequest request, ReplacementPartState? state, int scalarIndex,
            bool available, bool nearby, bool mountReady, bool busy)
        {
            if (request == null || !IsTurn(request.Operation) || request.SlotIndex != 0 || !available || state == null
                || !PartIdentity.TryItemId(state.NativeId, out uint id) || id != request.ItemId
                || state.Scalars == null || scalarIndex < 0 || scalarIndex >= state.Scalars.Length
                || !ValidTightness(state.Scalars[scalarIndex])) return PartFitStatus.Unavailable;
            if (state.Revision != request.ExpectedRevision) return PartFitStatus.Stale;
            if (!PartAttachmentPolicy.HasAttachment(state)) return PartFitStatus.NotFitted;
            if (!nearby) return PartFitStatus.TooFar;
            if (!mountReady) return PartFitStatus.Blocked;
            if (busy) return PartFitStatus.Busy;
            return TryTurn(state.Scalars[scalarIndex], request.Operation, out _) ? PartFitStatus.Pending : PartFitStatus.Blocked;
        }
    }
}
