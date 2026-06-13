using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WinterMP.FastBoot
{
    internal static class Es2GenericLoadPatcher
    {
        private static readonly Type[] ValueTypes =
        {
            typeof(float),
            typeof(int),
            typeof(bool),
            typeof(string),
            typeof(long),
            typeof(double),
            typeof(byte),
            typeof(Vector3),
            typeof(Quaternion),
            typeof(Color),
        };

        public static int Patch(Harmony harmony, Type es2, ManualLogSource log)
        {
            int count = 0;
            count += PatchFamily(harmony, es2, "Load", 1, ValueTypes);
            count += PatchFamily(harmony, es2, "Load", 2, ValueTypes);
            count += PatchFamily(harmony, es2, "LoadArray", 1, ValueTypes);
            count += PatchFamily(harmony, es2, "LoadList", 1, ValueTypes);
            count += PatchFamily(harmony, es2, "LoadHashSet", 1, ValueTypes);
            count += PatchFamily(harmony, es2, "LoadQueue", 1, ValueTypes);
            count += PatchFamily(harmony, es2, "LoadStack", 1, ValueTypes);
            count += PatchFamily(harmony, es2, "Load2DArray", 1, ValueTypes);
            return count;
        }

        private static int PatchFamily(
            Harmony harmony,
            Type es2,
            string methodName,
            int stringParamCount,
            Type[] typeArgs)
        {
            int patched = 0;

            foreach (MethodInfo definition in es2.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(definition.Name, methodName, StringComparison.Ordinal))
                    continue;
                if (!definition.IsGenericMethodDefinition)
                    continue;

                ParameterInfo[] ps = definition.GetParameters();
                if (ps.Length != stringParamCount || ps[0].ParameterType != typeof(string))
                    continue;

                if (stringParamCount == 2 && !ps[1].ParameterType.IsGenericParameter)
                    continue;

                Type[] genericParams = definition.GetGenericArguments();
                if (genericParams.Length != 1)
                    continue;

                for (int i = 0; i < typeArgs.Length; i++)
                {
                    try
                    {
                        MethodInfo closed = definition.MakeGenericMethod(typeArgs[i]);
                        harmony.Patch(
                            closed,
                            prefix: new HarmonyMethod(
                                typeof(Es2StaticLoadSkipPatch),
                                nameof(Es2StaticLoadSkipPatch.Prefix)));
                        patched++;
                    }
                    catch
                    {
                        // type unsupported for this generic Load overload
                    }
                }
            }

            return patched;
        }
    }

    internal static class Es2StaticLoadSkipPatch
    {
        public static bool Prefix(string identifier)
        {
            if (!Es2HydratePolicy.ShouldApply || !Es2HydratePolicy.ShouldSkipTag(identifier))
                return true;

            Es2HydratePolicy.NoteSkippedRead(identifier);
            return false;
        }
    }
}
