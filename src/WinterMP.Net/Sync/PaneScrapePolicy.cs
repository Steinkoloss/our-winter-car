using System;

namespace WinterMP.Net.Sync
{
    /// <summary>
    /// Decision input translated from the versioned ScraperAction by Core. No guest
    /// cutoff, contact result or "equipped" truth is accepted in this decision input.
    /// </summary>
    public sealed class PaneScrapeIntent
    {
        public const byte Windshield = 1;
        public readonly uint Epoch, Sequence, VehicleId, ToolId;
        public readonly byte Actor, Pane;
        public PaneScrapeIntent(uint epoch, byte actor, uint sequence, uint vehicleId, byte pane, uint toolId)
        { Epoch = epoch; Actor = actor; Sequence = sequence; VehicleId = vehicleId; Pane = pane; ToolId = toolId; }
    }

    /// <summary>
    /// Host-only observations, never deserialized from a scrape request. Equipment
    /// must be an exclusive host-established scraper lease, not motion ownership.
    /// Contact must be the host's first unobstructed ray hit from a fresh actor eye
    /// pose, against the discovered pane. Ages use the host's monotonic clock.
    /// </summary>
    public struct PaneScrapeHostContext
    {
        public bool ActorPresent, ActorAlive, ActorOutside, PaneAvailable, VehicleParked;
        public float PoseAgeSeconds;
        public byte EquipmentActor;
        public uint EquipmentEpoch, EquippedToolId;
        public bool IsIceScraper;
        public float EquipmentAgeSeconds;
        public uint ContactVehicleId;
        public byte ContactPane;
        public float ContactDistance, ContactAgeSeconds;
        public bool Unobstructed;
    }

    /// <summary>
    /// Unity adapter boundary. ReadContext must consult host-owned state. Apply
    /// executes ONLY the audited native windshield delta/material actions, never
    /// Freezing.Sound (which would heat the host for a guest stroke). No adapter
    /// may bypass the authoritative native equipment/contact bridge.
    /// </summary>
    public interface IPaneScrapeHost
    {
        PaneScrapeHostContext ReadContext(byte actor);
        float ReadWindshieldCutoff();
        void ApplyWindshieldDelta();
    }

    public enum PaneScrapeStatus
    {
        Accepted, InvalidActor, StaleEpoch, ReplayedSequence, WrongPane,
        ActorUnavailable, PaneUnavailable, InvalidEquipment, InvalidContact,
        Busy, NativeFailure
    }

    /// <summary>Absolute observed native value; no persisted or calculated scrape delta.</summary>
    public sealed class PaneScrapeSnapshot
    {
        public readonly uint VehicleId, Epoch, Revision;
        public readonly float Cutoff;
        public byte Pane => PaneScrapeIntent.Windshield;
        public PaneScrapeSnapshot(uint vehicleId, uint epoch, uint revision, float cutoff)
        { VehicleId = vehicleId; Epoch = epoch; Revision = revision; Cutoff = cutoff; }
    }

    public sealed class PaneScrapeDecision
    {
        public readonly PaneScrapeStatus Status;
        public readonly byte Actor;
        public readonly uint Sequence;
        public readonly PaneScrapeSnapshot? Snapshot;
        internal PaneScrapeDecision(PaneScrapeStatus status, byte actor, uint sequence, PaneScrapeSnapshot? snapshot)
        { Status = status; Actor = actor; Sequence = sequence; Snapshot = snapshot; }
    }

    public static class PaneScrapePolicy
    {
        public const float ContactMetres = .8f, MaximumObservationAgeSeconds = .6f;
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Fresh(float age) => Finite(age) && age >= 0 && age <= MaximumObservationAgeSeconds;

        public static PaneScrapeStatus Validate(PaneScrapeIntent intent, PaneScrapeHostContext host)
        {
            if (!host.ActorPresent || !host.ActorAlive) return PaneScrapeStatus.ActorUnavailable;
            if (!host.PaneAvailable || !host.VehicleParked) return PaneScrapeStatus.PaneUnavailable;
            if (intent.ToolId == 0 || !host.IsIceScraper || host.EquippedToolId != intent.ToolId
                || host.EquipmentActor != intent.Actor || host.EquipmentEpoch != intent.Epoch
                || !Fresh(host.EquipmentAgeSeconds)) return PaneScrapeStatus.InvalidEquipment;
            if (!host.ActorOutside || !Fresh(host.PoseAgeSeconds) || !Fresh(host.ContactAgeSeconds)
                || !host.Unobstructed || host.ContactVehicleId != intent.VehicleId || host.ContactPane != intent.Pane
                || !Finite(host.ContactDistance) || host.ContactDistance < 0 || host.ContactDistance > ContactMetres)
                return PaneScrapeStatus.InvalidContact;
            // Vehicle motion ownership is deliberately not equipment authority.
            return PaneScrapeStatus.Accepted;
        }
    }
}
