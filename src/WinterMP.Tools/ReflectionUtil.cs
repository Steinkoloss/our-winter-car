using System;
using System.Reflection;

namespace WinterMP.Tools
{
    /// <summary>
    /// Defensive reflection helpers. The dumper accesses PlayMaker purely via reflection
    /// so this project compiles without game assemblies and tolerates PlayMaker version
    /// differences: every accessor returns null instead of throwing.
    /// </summary>
    internal static class ReflectionUtil
    {
        public static Type? FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch
                {
                    // Some dynamic assemblies throw on GetType; skip them.
                }
            }

            return null;
        }

        public static object? GetMember(object? target, string name)
        {
            if (target == null) return null;
            try
            {
                var type = target.GetType();
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                var property = type.GetProperty(name, flags);
                if (property != null) return property.GetValue(target, null);

                var field = type.GetField(name, flags);
                if (field != null) return field.GetValue(target);
            }
            catch
            {
                // Property getters can throw inside PlayMaker on partially initialized FSMs.
            }

            return null;
        }

        public static string? GetString(object? target, string name) => GetMember(target, name) as string;

        public static object? GetStaticMember(Type? type, string name)
        {
            if (type == null) return null;
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

                var property = type.GetProperty(name, flags);
                if (property != null) return property.GetValue(null, null);

                var field = type.GetField(name, flags);
                if (field != null) return field.GetValue(null);
            }
            catch
            {
                // Same defensive contract as GetMember.
            }

            return null;
        }
    }
}
