using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class ReplacementPartRule
    {
        public readonly uint FactoryId;
        public readonly string Prefix;
        public readonly int ScalarCount, TightnessIndex, InertiaIndex;
        public readonly bool SupportsBeltVisual, SupportsCamProfile, SupportsAlternatorDamage;
        public ReplacementPartRule(uint factoryId, string prefix, int scalarCount, int tightnessIndex, bool supportsBeltVisual = false, bool supportsCamProfile = false, bool supportsAlternatorDamage = false, int inertiaIndex = -1)
        {
            if (!FactoryItemIdentity.IsNativeId(prefix + "1", prefix) || !PartIdentity.TryItemId(prefix + "0", out _)
                || scalarCount < 1 || scalarCount > ReplacementPartState.MaxScalars
                || tightnessIndex < 0 || tightnessIndex >= scalarCount || inertiaIndex < -1 || inertiaIndex >= scalarCount
                || inertiaIndex == tightnessIndex) throw new ArgumentException("Invalid replacement rule.");
            FactoryId = factoryId; Prefix = prefix; ScalarCount = scalarCount; TightnessIndex = tightnessIndex;
            InertiaIndex = inertiaIndex;
            SupportsBeltVisual = supportsBeltVisual;
            SupportsCamProfile = supportsCamProfile;
            SupportsAlternatorDamage = supportsAlternatorDamage;
        }
        public bool TryId(string nativeId, out uint id)
        {
            id = 0;
            return (nativeId == Prefix + "0" || FactoryItemIdentity.IsNativeId(nativeId, Prefix))
                && PartIdentity.TryItemId(nativeId, out id);
        }
        public bool CanCreate(ReplacementPartState state) => state.AssemblyId == 0 && !state.Installed
            && state.Scalars.Length == ScalarCount && state.Scalars[TightnessIndex] == 0 && state.BeltVisual == null && ValidCamProfile(state) && ValidAlternatorDamage(state) && ValidInertia(state);
        public bool CanCreateFitted(ReplacementPartState state) => state.Scalars.Length == ScalarCount
            && TryId(state.NativeId, out uint id) && PartAttachmentPolicy.Valid(state, id) && PartAttachmentPolicy.HasAttachment(state)
            && PartBeltVisualPolicy.Valid(state, SupportsBeltVisual) && ValidCamProfile(state) && ValidAlternatorDamage(state) && ValidInertia(state);
        // Native Flywheel divides by this value and assigns it to Drivetrain.
        public bool ValidInertia(ReplacementPartState state) => InertiaIndex < 0
            || (state.Scalars != null && state.Scalars.Length > InertiaIndex && state.Scalars[InertiaIndex] > 0
                && !float.IsInfinity(state.Scalars[InertiaIndex]) && !float.IsInfinity(.04f / state.Scalars[InertiaIndex]));
        public bool ValidCamProfile(ReplacementPartState state) => SupportsCamProfile
            ? PartCamshaftPolicy.ValidProfile(state.CamProfile) : state.CamProfile == string.Empty;
        public bool ValidAlternatorDamage(ReplacementPartState state) => state.AlternatorDamaged.HasValue
            == (SupportsAlternatorDamage && state.Installed && PartAttachmentPolicy.HasAttachment(state));
    }

    public sealed class ReplacementPartReplica
    {
        private readonly Dictionary<uint, ReplacementPartRule> _rules = new Dictionary<uint, ReplacementPartRule>();
        private readonly Dictionary<uint, ReplacementPartState> _states = new Dictionary<uint, ReplacementPartState>();
        private readonly ItemSpawnLifecycle _lifecycle;
        public ReplacementPartReplica(IEnumerable<ReplacementPartRule> rules, ItemSpawnLifecycle lifecycle)
        {
            _lifecycle = lifecycle;
            var prefixes = new HashSet<string>();
            foreach (var rule in rules)
            {
                if (_rules.ContainsKey(rule.FactoryId) || !prefixes.Add(rule.Prefix)) throw new ArgumentException("Duplicate replacement rule.");
                _rules.Add(rule.FactoryId, rule);
            }
        }
        public bool Receive(ReplacementPartState state, out uint id)
        {
            id = 0;
            if (state == null || !_rules.TryGetValue(state.FactoryId, out var rule) || !rule.TryId(state.NativeId, out id)
                || _lifecycle.IsRetired(id) || state.AssemblyId < 0 || state.Scalars == null
                || state.Installed != (state.AssemblyId > 0)
                || state.Scalars.Length != rule.ScalarCount || !ValidPose(state) || !PartAttachmentPolicy.Valid(state, id)
                || !PartBeltVisualPolicy.Valid(state, rule.SupportsBeltVisual)
                || !rule.ValidCamProfile(state)
                || !rule.ValidAlternatorDamage(state)
                || !rule.ValidInertia(state)
                || HasAttachmentCycle(id, state) || (state.RemovalAllowed && !PartRemovalPolicy.ValidAllowance(state, rule.TightnessIndex))) return false;
            foreach (float value in state.Scalars) if (!Finite(value)) return false;
            if (_states.TryGetValue(id, out var previous))
            {
                uint difference = unchecked(state.Revision - previous.Revision);
                if (difference > int.MaxValue || previous.FactoryId != state.FactoryId
                    || previous.NativeId != state.NativeId) return false;
                if (difference == 0)
                {
                    uint presentationDifference = unchecked(state.PresentationRevision - previous.PresentationRevision);
                    if (!SameGameplayValues(previous, state) || presentationDifference > int.MaxValue
                        || (presentationDifference == 0 && !SameValues(previous, state))) return false;
                }
            }
            _states[id] = Copy(state); return true;
        }
        public ReplacementPartState? Get(uint id) => !_lifecycle.IsRetired(id) && _states.TryGetValue(id, out var state) ? Copy(state) : null;
        // Engine readers examine many candidates per mount. Reject other mounts
        // before copying their arrays, while still returning an owned snapshot.
        public ReplacementPartState? GetAttached(uint id, PartParentKind kind, uint parentId, string path)
        {
            if (_lifecycle.IsRetired(id) || !_states.TryGetValue(id, out var state)
                || !PartAttachmentPolicy.HasAttachment(state) || state.ParentKind != kind
                || state.ParentId != parentId || state.ParentPath != path) return null;
            return Copy(state);
        }
        public bool AllowsLooseMotion(uint id) => !_lifecycle.IsRetired(id)
            && (!_states.TryGetValue(id, out var state) || _rules[state.FactoryId].CanCreate(state));
        public bool Occupies(PartParentKind kind, uint parentId, string path)
        {
            if (kind == PartParentKind.None || !PartAttachmentPolicy.ValidPath(path)) return false;
            foreach (var pair in _states)
            {
                var state = pair.Value;
                if (!_lifecycle.IsRetired(pair.Key) && PartAttachmentPolicy.HasAttachment(state)
                    && state.ParentKind == kind && state.ParentId == parentId && state.ParentPath == path) return true;
            }
            return false;
        }
        public void Clear() => _states.Clear();
        private bool HasAttachmentCycle(uint id, ReplacementPartState state)
        {
            var seen = new HashSet<uint> { id };
            while (state.ParentKind == PartParentKind.NativePart)
            {
                if (!seen.Add(state.ParentId)) return true;
                if (!_states.TryGetValue(state.ParentId, out var parent)) return false;
                state = parent;
            }
            return false;
        }
        public static bool SameGameplayValues(ReplacementPartState a, ReplacementPartState b)
        {
            if (a.FactoryId != b.FactoryId || a.NativeId != b.NativeId || a.AssemblyId != b.AssemblyId
                || a.RemovalAllowed != b.RemovalAllowed || a.Installed != b.Installed || a.Scalars.Length != b.Scalars.Length
                || a.CamProfile != b.CamProfile || a.AlternatorDamaged != b.AlternatorDamaged || !PartAttachmentPolicy.Same(a, b)) return false;
            for (int i = 0; i < a.Scalars.Length; i++) if (a.Scalars[i] != b.Scalars[i]) return false;
            return true;
        }
        public static bool SameValues(ReplacementPartState a, ReplacementPartState b) =>
            SameGameplayValues(a, b) && PartBeltVisualPolicy.Same(a.BeltVisual, b.BeltVisual);
        public static ReplacementPartState Copy(ReplacementPartState s) => new ReplacementPartState {
            Revision = s.Revision, FactoryId = s.FactoryId, NativeId = s.NativeId, AssemblyId = s.AssemblyId,
            Installed = s.Installed, Scalars = (float[])s.Scalars.Clone(), Position = s.Position, Rotation = s.Rotation,
            ParentKind = s.ParentKind, ParentId = s.ParentId, ParentPath = s.ParentPath,
            LocalPosition = s.LocalPosition, LocalRotation = s.LocalRotation, LocalScale = s.LocalScale,
            RemovalAllowed = s.RemovalAllowed, PresentationRevision = s.PresentationRevision,
            BeltVisual = PartBeltVisualPolicy.Copy(s.BeltVisual), CamProfile = s.CamProfile, AlternatorDamaged = s.AlternatorDamaged };
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool ValidPose(ReplacementPartState s)
        {
            var p = s.Position; var q = s.Rotation;
            float norm = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
            return Finite(p.X) && Finite(p.Y) && Finite(p.Z) && Finite(norm) && norm >= .9f && norm <= 1.1f;
        }
    }

    public sealed class ReplacementPartPublication
    {
        private ReplacementPartState? _observed;
        private uint _sent, _sentPresentation;
        private bool _hasSent;
        public bool NeedsBroadcast => _observed != null && (!_hasSent || _sent != _observed.Revision
            || _sentPresentation != _observed.PresentationRevision);
        public uint Observe(ReplacementPartState state, bool bodyChanged = false)
        {
            uint revision = _observed == null ? 1 : _observed.Revision;
            uint presentationRevision = _observed == null ? 1 : _observed.PresentationRevision;
            if (_observed != null)
            {
                bool gameplayChanged = bodyChanged || !ReplacementPartReplica.SameGameplayValues(_observed, state);
                if (gameplayChanged) revision = unchecked(revision + 1);
                if (gameplayChanged || !PartBeltVisualPolicy.Same(_observed.BeltVisual, state.BeltVisual))
                    presentationRevision = unchecked(presentationRevision + 1);
            }
            state.PresentationRevision = presentationRevision;
            _observed = ReplacementPartReplica.Copy(state); _observed.Revision = revision;
            return revision;
        }
        public void MarkBroadcast(uint revision, uint presentationRevision)
        { _sent = revision; _sentPresentation = presentationRevision; _hasSent = true; }
    }
}
