using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Concrete current part condition; random trigger events are never durable state.</summary>
    public static class VehicleDamagePolicy
    {
        public static uint Reconcile(uint previous, uint known, uint broken)
        {
            known &= VehicleDamage.ConcretePartsMask;
            return ((previous & ~known) | (broken & known)) & VehicleDamage.ConcretePartsMask;
        }

        public static bool IsValid(VehicleDamage state)
        {
            if (((state.DamageMask | state.KnownPartsMask) & ~VehicleDamage.ConcretePartsMask) != 0
                || state.Wear == null || state.Wear.Length != VehicleDamage.PartSlots) return false;
            for (int i = 0; i < VehicleDamage.PartSlots; i++)
            {
                uint bit = 1u << i;
                if ((state.KnownPartsMask & bit) == 0) continue;
                float wear = state.Wear[i];
                if (float.IsNaN(wear) || float.IsInfinity(wear)
                    || ((state.DamageMask & bit) != 0) != (wear <= 0f)) return false;
            }
            return true;
        }

        public static bool SameCondition(VehicleDamage left, VehicleDamage right)
        {
            if (left.DamageMask != right.DamageMask || left.KnownPartsMask != right.KnownPartsMask) return false;
            for (int i = 0; i < VehicleDamage.PartSlots; i++)
                if ((left.KnownPartsMask & (1u << i)) != 0 && Math.Abs(left.Wear[i] - right.Wear[i]) >= 0.01f)
                    return false;
            return true;
        }
    }
}
