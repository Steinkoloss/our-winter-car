using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LivePerformanceProbe
    {
        private readonly Dictionary<int, byte> _discoveryFrames = new Dictionary<int, byte>();
        private int _droppedDiscoveryFrames;
        private bool _discoveryDiagnostics;

        private void InstallDiscoveryDiagnostics()
        {
            var core = typeof(WinterMP.Core.Session.SessionManager).Assembly;
            var world = core.GetType("WinterMP.Core.Sync.WorldSyncManager", true);
            var scan = world.GetMethod("ScanWorldCore", Members) ?? world.GetMethod("ScanWorld", Members);
            _harmony!.Patch(scan, new HarmonyLib.HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("ObserveWorldDiscovery", Members)));
            _harmony.Patch(core.GetType("WinterMP.Core.Sync.ItemWorldSync", true).GetMethod("ScanItems", Members),
                new HarmonyLib.HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("ObserveItemDiscovery", Members)));
            _harmony.Patch(core.GetType("WinterMP.Core.Sync.NpcTrafficSync", true).GetMethod("Scan", Members),
                new HarmonyLib.HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("ObserveNpcDiscovery", Members)));
            _discoveryDiagnostics = true;
        }

        private static void ObserveWorldDiscovery() => RecordDiscoveryFrame(1);
        private static void ObserveItemDiscovery() => RecordDiscoveryFrame(2);
        private static void ObserveNpcDiscovery() => RecordDiscoveryFrame(4);

        private static void RecordDiscoveryFrame(byte phase)
        {
            var probe = _instance;
            if (probe == null || !Sampling) return;
            int frame = UnityEngine.Time.frameCount;
            if (!probe._discoveryFrames.TryGetValue(frame, out byte prior) && probe._discoveryFrames.Count >= 1024)
            { probe._droppedDiscoveryFrames++; return; }
            probe._discoveryFrames[frame] = (byte)(prior | phase);
        }

        private void WriteDiscoveryFrames()
        {
            if (!_discoveryDiagnostics) return;
            var keys = new List<int>(_discoveryFrames.Keys); keys.Sort();
            var rows = new List<string> { "frame,phases" };
            foreach (int frame in keys) rows.Add(frame + "," + _discoveryFrames[frame]);
            File.WriteAllLines(Path.Combine(_output, _role + "-discovery-frames.csv"), rows.ToArray());
            _observations.Add("World discovery phases: " + keys.Count + " sampled frames; dropped=" + _droppedDiscoveryFrames
                + "; mask bits 1=controls/protection, 2=items, 4=NPCs.");
        }

        private struct CallSample { internal long Stamp; internal int Collections; }
        private struct SlowCall
        {
            internal MethodBase Method;
            internal long Started, Ended;
            internal int Collections;
        }
        private readonly List<SlowCall> _slowCalls = new List<SlowCall>(256);
        private long _sampleStamp;
        private int _droppedSlowCalls;

        private void RecordSlowCall(MethodBase method, CallSample before, long ended)
        {
            if (ended - before.Stamp < Stopwatch.Frequency / 200) return;
            if (_slowCalls.Count == 4096) { _droppedSlowCalls++; return; }
            _slowCalls.Add(new SlowCall { Method = method, Started = before.Stamp, Ended = ended,
                Collections = GC.CollectionCount(0) - before.Collections });
        }

        private void WriteSlowCalls()
        {
            var rows = new List<string> { "start_seconds,end_seconds,method,duration_ms,gen0_collections" };
            foreach (var call in _slowCalls)
                rows.Add(Number((call.Started - _sampleStamp) / (double)Stopwatch.Frequency) + ","
                    + Number((call.Ended - _sampleStamp) / (double)Stopwatch.Frequency) + ","
                    + call.Method.DeclaringType.Name + "." + call.Method.Name + ","
                    + Number((call.Ended - call.Started) * 1000.0 / Stopwatch.Frequency) + "," + call.Collections);
            File.WriteAllLines(Path.Combine(_output, _role + "-slow-calls.csv"), rows.ToArray());
            if (_slowCalls.Count != 0 || _droppedSlowCalls != 0)
                _observations.Add("Slow-call timeline: " + _slowCalls.Count + " calls >= 5 ms; dropped=" + _droppedSlowCalls
                    + ". Inclusive timings observe exceptions without suppressing them.");
        }
    }
}
