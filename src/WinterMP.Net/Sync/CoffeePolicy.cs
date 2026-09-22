using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class CoffeePolicy
    {
        public static uint PackageId(string nativeId)
        {
            if (!FactoryItemIdentity.IsNativeId(nativeId, "groundcoffee0")) throw new ArgumentException("Invalid native coffee packet ID.");
            return StableHash.Fnv1a32("coffee-package:" + nativeId);
        }
        private static bool Range(float value, float max) => value >= 0 && value <= max;
        public static bool Valid(CoffeeIntent i) => i.ItemId != 0 && i.Sequence != 0 && i.PlayerId > 0 && i.PlayerId < 255 && i.Action >= CoffeeAction.OpenLid && i.Action <= CoffeeAction.Drink;
        public static bool Valid(CoffeeDrinkResult r) => r.ItemId != 0 && r.Sequence != 0 && r.PlayerId < 255 && r.Amount > .01f && r.Amount <= .3f && Range(r.Caffeine, 1.5f);
        public static bool Valid(CoffeeState s)
        {
            if (s.ItemId == 0 || s.Revision == 0 || s.Kind > 2 || s.Flags > 3 || !Range(s.Water, 2) || !Range(s.Ground, s.Kind == 2 ? 100 : 26)
                || !Range(s.Coffee, s.Kind == 1 ? .3f : 2) || !Range(s.Caffeine, 1.5f) || !Range(s.BoilVolume, .6f)) return false;
            if (s.Kind != 0 && (s.Flags != 0 || s.Water != 0 || s.BoilVolume != 0) || s.Kind == 1 && s.Ground != 0
                || s.Kind == 2 && (s.Coffee != 0 || s.Caffeine != 0)) return false;
            var p = s.Position; var q = s.Rotation; float norm = q.X*q.X+q.Y*q.Y+q.Z*q.Z+q.W*q.W;
            return Math.Abs(p.X) <= 100000 && Math.Abs(p.Y) <= 100000 && Math.Abs(p.Z) <= 100000 && norm >= .9f && norm <= 1.1f;
        }
        public static float Transfer(float water, float cup, float seconds)
        {
            if (!Range(water, 2) || !Range(cup, .3f) || !(seconds > 0 && seconds <= .25f)) return 0;
            return Math.Max(0, Math.Min(.16f * seconds, Math.Min(water - .1f, .3f - cup)));
        }
        public static bool SameContents(CoffeeState a, CoffeeState b) => a.Kind == b.Kind && a.Flags == b.Flags && a.Water == b.Water
            && a.Ground == b.Ground && a.Coffee == b.Coffee && a.Caffeine == b.Caffeine && a.BoilVolume == b.BoilVolume;
    }
}
