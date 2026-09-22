using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static WheelRimData ParseWheelRim(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid wheel rim profile.");
            var rule = new WheelRimData {
                State = EngineProtectionName(obj, "state"), WheelObject = EngineProtectionName(obj, "wheelObject"),
                RadiusVariable = EngineProtectionName(obj, "radiusVariable"), SoundVariable = EngineProtectionName(obj, "soundVariable"),
                QuietState = EngineProtectionName(obj, "quietState"),
                RadiusIndex = SlotNumber(obj.TryGetValue("radiusIndex", out var radius) ? radius : null, "rim radius action", 0, 1),
                FrictionIndex = SlotNumber(obj.TryGetValue("frictionIndex", out var friction) ? friction : null, "rim friction action", 0, 1),
                QuietIndex = SlotNumber(obj.TryGetValue("quietIndex", out var quiet) ? quiet : null, "rim quiet action", 0, 255),
                Friction = VenttiNumber(obj, "friction") };
            if (rule.State == rule.QuietState || rule.RadiusIndex == rule.FrictionIndex
                || rule.WheelObject == rule.SoundVariable || float.IsNaN(rule.Friction) || float.IsInfinity(rule.Friction)
                || rule.Friction <= 0 || rule.Friction > 1)
                throw new FormatException("Ambiguous wheel rim profile.");
            return rule;
        }
    }

    internal sealed class WheelRimData
    {
        public string State = "", WheelObject = "", RadiusVariable = "", SoundVariable = "", QuietState = "";
        public int RadiusIndex, FrictionIndex, QuietIndex;
        public float Friction;
    }
}
