using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private static readonly Dictionary<FsmStateAction, NativePassengerCondensation> PassengerCondensationReaders
            = new Dictionary<FsmStateAction, NativePassengerCondensation>();
        private static readonly HashSet<Type> PassengerCondensationHookTypes = new HashSet<Type>();

        private static void EnsurePassengerCondensationHooks(NativePassengerCondensation b)
        {
            foreach (var action in new[] { b.Actions[2], b.Actions[3] })
            {
                var type = action.GetType(); if (PassengerCondensationHookTypes.Contains(type)) continue;
                bool entry = ReferenceEquals(action, b.Actions[2]);
                var method = type.GetMethod(entry ? "OnEnter" : "DoFloatOperator",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                    throw new InvalidOperationException("Native passenger condensation boundary changed.");
                new Harmony("com.ourwintercar.wintermp.passenger-condensation").Patch(method,
                    prefix: new HarmonyMethod(typeof(NativePassengerCondensationHooks), nameof(NativePassengerCondensationHooks.Before)),
                    finalizer: new HarmonyMethod(typeof(NativePassengerCondensationHooks), nameof(NativePassengerCondensationHooks.After)));
                PassengerCondensationHookTypes.Add(type);
            }
        }

        internal sealed class PassengerCondensationScope
        {
            internal NativePassengerCondensation Binding = null!;
            internal bool Entry, OldEntry;
            internal float OldSweat;
            internal object Original = null!;
        }

        private static class NativePassengerCondensationHooks
        {
            internal static void Before(FsmStateAction __instance, out PassengerCondensationScope? __state)
            {
                __state = null;
                if (!PassengerCondensationReaders.TryGetValue(__instance, out var b)) return;
                try
                {
                    ValidatePassengerCondensation(b);
                    if (!b.Owner.TryPassengerCondensation(b, out float sweat)) return;
                    bool entry = ReferenceEquals(__instance, b.Actions[2]);
                    var scope = new PassengerCondensationScope { Binding = b, Entry = entry,
                        Original = (entry ? b.EntryOperand : b.SweatOperand).GetValue(__instance),
                        OldEntry = b.EntryInput.Value, OldSweat = b.SweatInput.Value };
                    __state = scope;
                    if (entry) { b.EntryDepth++; b.EntryInput.Value = sweat > 0f; b.EntryOperand.SetValue(__instance, b.EntryInput); }
                    else { b.SweatDepth++; b.SweatInput.Value = sweat; b.SweatOperand.SetValue(__instance, b.SweatInput); }
                }
                catch (Exception error)
                {
                    if (__state != null) try { Restore(__state); } catch (Exception restore) { NotePassengerCondensationFailure(b.Item, restore); }
                    __state = null; ClearPassengerCondensation(b.Item); NotePassengerCondensationFailure(b.Item, error);
                }
            }

            internal static Exception? After(Exception? __exception, PassengerCondensationScope? __state)
            {
                if (__state != null)
                    try { Restore(__state); }
                    catch (Exception error) { ClearPassengerCondensation(__state.Binding.Item); NotePassengerCondensationFailure(__state.Binding.Item, error); }
                return __exception;
            }

            private static void Restore(PassengerCondensationScope scope)
            {
                var b = scope.Binding;
                if (scope.Entry)
                {
                    b.EntryOperand.SetValue(b.Actions[2], scope.Original); b.EntryDepth--;
                    b.EntryInput.Value = scope.OldEntry;
                }
                else
                {
                    b.SweatOperand.SetValue(b.Actions[3], scope.Original); b.SweatDepth--;
                    b.SweatInput.Value = scope.OldSweat;
                }
            }
        }
    }
}
