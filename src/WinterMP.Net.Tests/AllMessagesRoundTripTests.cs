using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    /// <summary>
    /// Reflection sweep over EVERY registered message: fill every field with a
    /// distinctive non-default value (lists get 2 elements so element ordering is
    /// exercised, nested structs are filled recursively), then assert the wire is
    /// symmetric via the double-encode invariant:
    ///
    ///     Encode(Decode(Encode(m))) == Encode(m)   (byte for byte)
    ///
    /// This catches Write/Read asymmetry — a field written but not read, a field
    /// read but not written, or a reordering across differing types — without
    /// needing a hand-written per-field assertion for all 60+ messages. It is
    /// robust to lossy quantization because an idempotent quantizer re-encodes a
    /// decoded value to the same bytes. Decode also runs RequireEnd internally, so
    /// any trailing/short read throws here.
    /// </summary>
    public class AllMessagesRoundTripTests
    {
        [Fact]
        public void EveryRegisteredMessage_HasSymmetricWireFormat()
        {
            var failures = new List<string>();

            foreach (var id in MessageRegistry.KnownIds)
            {
                IMessage template = MessageRegistry.Create(id);
                try
                {
                    Fill(template, 1);

                    byte[] first = PacketCodec.Encode(template);
                    IMessage decoded = PacketCodec.Decode(first);
                    byte[] second = PacketCodec.Encode(decoded);

                    if (decoded.GetType() != template.GetType())
                        failures.Add($"{id}: decoded as {decoded.GetType().Name}, expected {template.GetType().Name}");
                    else if (!first.SequenceEqual(second))
                        failures.Add($"{id} ({template.GetType().Name}): asymmetric wire — " +
                            $"encode={first.Length}B, re-encode={second.Length}B (Write/Read field mismatch)");
                }
                catch (Exception e)
                {
                    failures.Add($"{id} ({template.GetType().Name}): {e.GetType().Name}: {e.Message}");
                }
            }

            Assert.True(failures.Count == 0,
                "Message wire-format asymmetry detected:\n  " + string.Join("\n  ", failures));
        }

        /// <summary>Recursively assign every public instance field a distinctive non-default value.</summary>
        private static void Fill(object target, int seed)
        {
            foreach (var field in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsInitOnly) continue;
                field.SetValue(target, MakeValue(field.FieldType, seed));
                seed += 7;
            }
        }

        private static object MakeValue(Type t, int seed)
        {
            if (t == typeof(byte)) return (byte)(seed & 0x7F | 1);
            if (t == typeof(sbyte)) return (sbyte)(seed & 0x3F | 1);
            if (t == typeof(bool)) return true;
            if (t == typeof(ushort)) return (ushort)(seed * 131 + 7);
            if (t == typeof(short)) return (short)(seed * 131 + 7);
            if (t == typeof(uint)) return (uint)(seed * 2654435761u + 11u);
            if (t == typeof(int)) return seed * 40503 + 13;
            if (t == typeof(ulong)) return (ulong)seed * 0x9E3779B97F4A7C15UL + 17UL;
            if (t == typeof(long)) return (long)seed * 0x123456789L + 19L;
            if (t == typeof(float)) return seed * 1.5f + 0.25f;
            if (t == typeof(double)) return seed * 1.5 + 0.25;
            if (t == typeof(string)) return "str" + seed;
            if (t == typeof(byte[])) return new byte[] { (byte)seed, (byte)(seed + 1), (byte)(seed + 2) };
            if (t == typeof(NetVector3)) return new NetVector3(seed + 0.1f, seed + 0.2f, seed + 0.3f);
            if (t == typeof(NetQuaternion)) return new NetQuaternion(seed + 0.1f, seed + 0.2f, seed + 0.3f, seed + 0.4f);
            if (t.IsEnum)
            {
                var values = Enum.GetValues(t);
                // Prefer a non-zero defined value so a field that is written survives the trip.
                foreach (var v in values)
                    if (Convert.ToInt64(v) != 0) return v;
                return values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(t);
            }

            if (t.IsArray)
            {
                var elem = t.GetElementType()!;
                var arr = Array.CreateInstance(elem, 2);
                arr.SetValue(MakeValue(elem, seed), 0);
                arr.SetValue(MakeValue(elem, seed + 100), 1);
                return arr;
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elem = t.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(t)!;
                list.Add(MakeValue(elem, seed));
                list.Add(MakeValue(elem, seed + 100));
                return list;
            }

            // Nested struct/class (message Entry types): construct and fill recursively.
            object nested = Activator.CreateInstance(t)!;
            Fill(nested, seed);
            return nested;
        }
    }
}
