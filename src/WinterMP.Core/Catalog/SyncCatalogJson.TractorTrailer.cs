using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static TractorTrailerData ParseTractorTrailer(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid tractor trailer catalog.");
            var data = new TractorTrailerData();
            foreach (string key in new[] { "tractor", "trailer", "bed", "support", "target", "hook", "remove", "hydraulics", "log",
                "hookFsm", "detachFsm", "useFsm", "attached", "attachState", "detachState", "releaseState", "idleState", "attachedState", "waitingState", "feelState" })
                data.Names.Add(key, EngineInputPath(fields, key));
            return data;
        }
    }
    internal sealed class TractorTrailerData
    {
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        internal string this[string key] => Names[key];
    }
}
