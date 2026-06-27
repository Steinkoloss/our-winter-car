using System;
using WinterMP.Core.Sync;

namespace WinterMP.Core.Catalog
{
    internal readonly struct CatalogPathMatch
    {
        private readonly string _pathPrefix;
        private readonly string? _pathContains;
        private readonly string? _objectName;
        private readonly string? _objectNameContains;
        private readonly string _fsmName;
        private readonly string[] _excludePathPrefixes;

        internal CatalogPathMatch(
            string pathPrefix,
            string? pathContains,
            string? objectName,
            string? objectNameContains,
            string fsmName,
            string[] excludePathPrefixes)
        {
            _pathPrefix = pathPrefix;
            _pathContains = pathContains;
            _objectName = objectName;
            _objectNameContains = objectNameContains;
            _fsmName = fsmName;
            _excludePathPrefixes = excludePathPrefixes;
        }

        internal bool Matches(PlayMakerFSM fsm)
        {
            if (fsm.FsmName != _fsmName) return false;

            string scenePath = ScenePath.Of(fsm.transform);
            string objectName = fsm.gameObject.name;

            foreach (string excluded in _excludePathPrefixes)
            {
                if (scenePath.StartsWith(excluded, StringComparison.Ordinal))
                    return false;
            }

            if (_pathPrefix.Length > 0 && !scenePath.StartsWith(_pathPrefix, StringComparison.Ordinal))
                return false;

            if (_pathContains != null && scenePath.IndexOf(_pathContains, StringComparison.Ordinal) < 0)
                return false;

            if (_objectName != null && objectName != _objectName)
                return false;

            if (_objectNameContains != null
                && objectName.IndexOf(_objectNameContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return true;
        }
    }
}
