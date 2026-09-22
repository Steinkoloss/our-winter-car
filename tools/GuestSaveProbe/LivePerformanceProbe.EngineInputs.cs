using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LivePerformanceProbe
    {
        private sealed class EngineConsumer
        {
            internal readonly PlayMakerFSM? Fsm;
            internal readonly int Id;
            internal readonly Cost[] Buckets = new Cost[96];

            internal EngineConsumer(PlayMakerFSM? fsm, int id)
            {
                Fsm = fsm; Id = id;
                // Preallocate activity/origin combinations during warmup. Changing
                // native activity must not allocate a new bucket inside sampling.
                for (int i = 0; i < Buckets.Length; i++) Buckets[i] = new Cost();
            }
        }

        private sealed class EngineConsumerIdentity : IEqualityComparer<PlayMakerFSM>
        {
            public bool Equals(PlayMakerFSM? x, PlayMakerFSM? y) => ReferenceEquals(x, y);
            public int GetHashCode(PlayMakerFSM obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private static readonly Dictionary<PlayMakerFSM, EngineConsumer> EngineConsumers =
            new Dictionary<PlayMakerFSM, EngineConsumer>(new EngineConsumerIdentity());
        private static EngineConsumer _unlistedEngineConsumer = new EngineConsumer(null, 0);
        private static MethodInfo? _engineReassertMethod, _engineStateEntryMethod;
        [ThreadStatic] private static int _engineOrigin;
        private static int _lateEngineConsumers;
        private static long _unlistedEngineCalls;

        private static void InitializeEngineInputDiagnostics()
        {
            EngineConsumers.Clear(); _unlistedEngineConsumer = new EngineConsumer(null, 0);
            _engineOrigin = _lateEngineConsumers = 0; _unlistedEngineCalls = 0;
            var guard = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
            _engineReassertMethod = guard.GetMethod("Reassert", Members)
                ?? throw new InvalidOperationException("Missing engine reassertion target.");
            _engineStateEntryMethod = guard.GetMethod("GuardStateEntry", Members)
                ?? throw new InvalidOperationException("Missing engine entry target.");
        }

        private static void BeginEngineInputHeap(MethodBase __originalMethod, PlayMakerFSM fsm, out HeapSample __state)
        {
            Cost? bucket = null;
            if (_instance != null && !_instance._finished)
            {
                EngineConsumer consumer;
                if (ReferenceEquals(fsm, null)) consumer = _unlistedEngineConsumer;
                else if (!EngineConsumers.TryGetValue(fsm, out consumer))
                {
                    if (EngineConsumers.Count < 256)
                    {
                        consumer = new EngineConsumer(fsm, EngineConsumers.Count + 1);
                        EngineConsumers.Add(fsm, consumer);
                        if (Sampling) _lateEngineConsumers++;
                    }
                    else consumer = _unlistedEngineConsumer;
                }
                if (Sampling)
                {
                    int activity = EngineActivity(fsm);
                    bucket = consumer.Buckets[_engineOrigin * 32 + activity];
                    if (ReferenceEquals(consumer, _unlistedEngineConsumer)) _unlistedEngineCalls++;
                }
            }
            // Registration and activity reads precede the existing interval. These
            // observer costs remain visible in enclosing methods and frame totals.
            BeginHeap(__originalMethod, out __state);
            __state.EngineConsumer = bucket;
        }

        private static int EngineActivity(PlayMakerFSM? fsm)
        {
            if (fsm == null) return 0;
            int flags = 16;
            if (fsm.enabled) flags |= 1;
            if (fsm.gameObject.activeInHierarchy) flags |= 2;
            var native = fsm.Fsm;
            if (native != null && native.Initialized) flags |= 4;
            if (native != null && native.Started) flags |= 8;
            return flags;
        }

        private void CheckEngineInputScopes()
        {
            if (Sampling || _engineOrigin != 0 || _heapParent != null
                || _engineReassertMethod == null || _engineStateEntryMethod == null)
                throw new InvalidOperationException("Engine consumer calibration must precede sampling.");
            BeginHeap(_engineReassertMethod, out var outer);
            try
            {
                if (_engineOrigin != 1) throw new InvalidOperationException("Engine recurring origin missing.");
                BeginHeap(_engineStateEntryMethod, out var entry);
                try
                {
                    if (_engineOrigin != 2) throw new InvalidOperationException("Engine entry origin missing.");
                    BeginHeap(_engineStateEntryMethod, out var recursive);
                    try
                    {
                        if (_engineOrigin != 2) throw new InvalidOperationException("Engine recursive origin missing.");
                        throw new ApplicationException("Expected probe scope calibration.");
                    }
                    catch (ApplicationException) { }
                    finally { EndHeap(_engineStateEntryMethod, recursive); }
                    if (_engineOrigin != 2) throw new InvalidOperationException("Engine recursive restoration failed.");
                }
                finally { EndHeap(_engineStateEntryMethod, entry); }
                if (_engineOrigin != 1) throw new InvalidOperationException("Engine entry restoration failed.");
            }
            finally { EndHeap(_engineReassertMethod, outer); }
            if (_engineOrigin != 0 || _heapParent != null)
                throw new InvalidOperationException("Engine origin root restoration failed.");
            if (EngineCsv("a,b\"c\nd") != "\"a,b\"\"c\nd\"")
                throw new InvalidOperationException("Engine label CSV escaping failed.");
            _observations.Add("Engine consumer scope calibration passed: recurring, entry, recursive, throwing and unsampled scopes restore their origin; CSV labels escaped.");
        }

        private void WriteEngineInputCosts()
        {
            var rows = new List<string> { "consumer_id,path_at_end,fsm_at_end,state_at_end,origin,live,enabled,object_active,initialized,started,calls,eligible_calls,heap_growth_bytes,mean_growth_bytes,max_growth_bytes,calls_with_collection,calls_with_decrease,eligible_total_ms,eligible_mean_ms" };
            AppendEngineConsumer(rows, _unlistedEngineConsumer);
            foreach (var consumer in EngineConsumers.Values) AppendEngineConsumer(rows, consumer);
            File.WriteAllLines(Path.Combine(_output, _role + "-engine-inputs.csv"), rows.ToArray());
            _observations.Add("Engine input consumers: registered=" + EngineConsumers.Count + "; registered during sample=" + _lateEngineConsumers
                + "; unlisted calls=" + _unlistedEngineCalls + "; final origin=" + EngineOriginName(_engineOrigin));
            _observations.Add("Engine input rows partition PrepareGuestEngineInputFsm intervals by managed consumer identity, nearest recurring/state-entry origin, and activity at call entry. Labels describe the end of sampling, not cached identities used by the mod. Activity does not establish that an engine is running.");
        }

        private static void AppendEngineConsumer(List<string> rows, EngineConsumer consumer)
        {
            string path = "unlisted", name = "", state = "";
            if (!ReferenceEquals(consumer.Fsm, null))
            {
                if (consumer.Fsm == null) path = "destroyed";
                else { path = PathOf(consumer.Fsm.transform); name = consumer.Fsm.FsmName; state = consumer.Fsm.ActiveStateName; }
            }
            for (int i = 0; i < consumer.Buckets.Length; i++)
            {
                var cost = consumer.Buckets[i];
                if (cost.Calls == 0) continue;
                int activity = i % 32;
                AppendHeap(rows, consumer.Id + "," + EngineCsv(path) + "," + EngineCsv(name) + "," + EngineCsv(state)
                    + "," + EngineOriginName(i / 32) + "," + ((activity & 16) != 0) + "," + ((activity & 1) != 0)
                    + "," + ((activity & 2) != 0) + "," + ((activity & 4) != 0) + "," + ((activity & 8) != 0), cost);
            }
        }

        private static string EngineOriginName(int origin) => origin == 1 ? "recurring" : origin == 2 ? "state-entry" : "other";
        private static string EngineCsv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
