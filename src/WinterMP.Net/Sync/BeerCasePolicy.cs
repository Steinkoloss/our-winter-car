using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class BeerCasePolicy
    {
        // Wire resource bound, NOT an assertion of native pack capacity.
        public const int MaximumCount = 65535;
        public const float MaximumContactMetres = 1.5f, MaximumPoseAgeSeconds = .6f;
        public static bool Player(byte id) => id > 0 && id < 255;
        public static bool NativeIdentity(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128) return false;
            for (int i = 0; i < id.Length; i++) if (id[i] < 33 || id[i] > 126) return false;
            return true;
        }
        public static uint CaseId(string nativeId) => StableHash.Fnv1a32("beercase:" + nativeId);
        public static bool Identity(uint id, string nativeId) => id != 0 && NativeIdentity(nativeId) && id == CaseId(nativeId);
        public static bool Count(int capacity, int remaining) => capacity > 0 && capacity <= MaximumCount && remaining >= 0 && remaining <= capacity;
        public static bool Valid(BeerCaseExtractIntent r) => r != null && Identity(r.CaseId, r.NativeId) && r.Epoch != 0
            && r.Connection != 0 && r.Sequence != 0 && r.ExpectedRevision != 0 && Player(r.PlayerId)
            && r.ExpectedRemaining > 0 && r.ExpectedRemaining <= MaximumCount;
        public static bool Valid(BeerCaseUpdate s) => s != null && Identity(s.CaseId, s.NativeId) && s.Epoch != 0 && s.Revision != 0
            && Count(s.Capacity, s.Remaining) && (s.PlayerId == 255 ? s.Connection == 0 && s.Sequence == 0
                : Player(s.PlayerId) && s.Connection != 0 && s.Sequence != 0 && s.Revision > 1 && s.Remaining < s.Capacity);
        public static bool SameCase(BeerCaseUpdate a, BeerCaseUpdate b) => a.CaseId == b.CaseId && a.NativeId == b.NativeId && a.Epoch == b.Epoch && a.Capacity == b.Capacity;
        public static bool Contact(BeerCaseContact c, BeerCaseUpdate s) => c.ActorPresent && c.ActorAlive && c.Unobstructed
            && c.CaseId == s.CaseId && c.NativeId == s.NativeId && c.PoseAgeSeconds >= 0 && c.PoseAgeSeconds <= MaximumPoseAgeSeconds
            && c.ContactAgeSeconds >= 0 && c.ContactAgeSeconds <= MaximumPoseAgeSeconds
            && c.Distance >= 0 && c.Distance <= MaximumContactMetres;
        public static bool QuarantineControl(string objectName, string fsm) => objectName == "beercase" && fsm == "Use";
    }

    // Host observations only. Ownership is deliberately absent: extracting from a
    // grounded case must not require taking its rigidbody/motion lease first.
    public struct BeerCaseContact
    {
        public bool ActorPresent, ActorAlive, Unobstructed;
        public uint CaseId;
        public string NativeId;
        public float PoseAgeSeconds, ContactAgeSeconds, Distance;
    }

    public interface IBeerCaseHost
    {
        // Read-only live observation: exact native ID, audited capacity, remaining
        // count, active/usable state. Revision/correlation belong to the authority.
        BeerCaseUpdate Read();
        BeerCaseContact ReadContact(byte authenticatedActor);
        // Atomic compare/extract: false MUST mean no native mutation. The adapter
        // must not replay Remove bottle on a peer or touch item/cargo ownership.
        // A real binding needs audited action/save/initialization fields first.
        bool TryExtractOne(BeerCaseUpdate expected, byte authenticatedActor);
    }

    public sealed class BeerCaseReplica
    {
        private readonly uint _case, _epoch;
        private readonly string _nativeId;
        private BeerCaseUpdate? _state;
        public BeerCaseReplica(uint caseId, string nativeId, uint epoch)
        {
            if (!BeerCasePolicy.Identity(caseId, nativeId) || epoch == 0) throw new ArgumentException("Exact beer-case identity and epoch required.");
            _case = caseId; _nativeId = nativeId; _epoch = epoch;
        }
        public BeerCaseUpdate? Snapshot() => _state?.Copy();
        public bool Apply(BeerCaseUpdate update)
        {
            if (!BeerCasePolicy.Valid(update) || update.CaseId != _case || update.NativeId != _nativeId || update.Epoch != _epoch) return false;
            if (_state != null && (update.Capacity != _state.Capacity || update.Revision <= _state.Revision)) return false;
            _state = update.Copy(); return true;
        }
    }
}
