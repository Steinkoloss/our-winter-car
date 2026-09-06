using System;
using System.Collections.Generic;

namespace WinterMP.Net.Sync
{
    public sealed class PackageIdentityRule
    {
        public readonly uint FactoryId;
        public readonly string Prefix, ContentsPath;
        public readonly ushort Capacity;
        public PackageIdentityRule(uint factoryId, string prefix, string contentsPath, ushort capacity = 1)
        { FactoryId = factoryId; Prefix = prefix; ContentsPath = contentsPath; Capacity = capacity; }
    }

    /// <summary>Boxes share a display name; their persistent ID and contents factory
    /// identify the native package independently of position or scan order.</summary>
    public sealed class PackageIdentityResolver
    {
        private readonly List<PackageIdentityRule> _rules = new List<PackageIdentityRule>();

        public PackageIdentityResolver(IEnumerable<PackageIdentityRule> rules)
        {
            foreach (var rule in rules)
            {
                if (rule == null || string.IsNullOrEmpty(rule.Prefix) || rule.Prefix.Length > 100
                    || string.IsNullOrEmpty(rule.ContentsPath) || rule.Capacity == 0 || rule.Capacity > 64)
                    throw new ArgumentException("Invalid package identity rule.");
                foreach (char c in rule.Prefix)
                    if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                        throw new ArgumentException("Package prefixes must be ASCII letters or digits.");
                foreach (var previous in _rules)
                    if (previous.FactoryId == rule.FactoryId || Overlaps(previous.Prefix, rule.Prefix)
                        || Overlaps(rule.Prefix, previous.Prefix))
                        throw new ArgumentException("Ambiguous package factory identity.");
                _rules.Add(rule);
            }
        }

        public bool TryResolve(string nativeId, string contentsPath, out uint itemId)
        {
            itemId = 0;
            foreach (var rule in _rules)
            {
                if (!FactoryItemIdentity.IsNativeId(nativeId, rule.Prefix)) continue;
                // A matching save ID with another box's spawner is not a usable
                // substitute. Never choose by the common package(Clone) name.
                if (!string.Equals(contentsPath, rule.ContentsPath, StringComparison.Ordinal)) return false;
                itemId = FactoryItemIdentity.ItemId(rule.FactoryId, nativeId);
                return true;
            }
            return false;
        }

        public bool TryResolve(uint factoryId, string nativeId, ushort quantity, out uint itemId)
        {
            itemId = 0;
            foreach (var rule in _rules)
                if (rule.FactoryId == factoryId && quantity <= rule.Capacity
                    && FactoryItemIdentity.IsNativeId(nativeId, rule.Prefix))
                { itemId = FactoryItemIdentity.ItemId(factoryId, nativeId); return true; }
            return false;
        }

        private static bool Overlaps(string prefix, string longer)
        {
            if (!longer.StartsWith(prefix, StringComparison.Ordinal)) return false;
            for (int i = prefix.Length; i < longer.Length; i++)
                if (longer[i] < '0' || longer[i] > '9') return false;
            return true;
        }
    }
}
