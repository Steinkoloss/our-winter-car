using UnityEngine;

namespace WinterMP.Core.Sync
{
    internal static class WorldSyncIds
    {
        public const byte NoOwner = 255;
    }

    internal sealed class SyncedItem
{
    public Rigidbody Body = null!;
    public string Path = string.Empty;
    public uint Id;
    public bool IsVehicle;

    public bool KinematicSaved;
    public bool OriginalKinematic;

    public byte RemoteOwner = WorldSyncIds.NoOwner;
    public bool RemoteIsDriver;
    public bool RemoteVehicleStream;
    public float LastRemoteAt = -999f;
    public ushort LastRemoteSequence;
    // The sender id that established the current LastRemoteSequence baseline. Unlike
    // RemoteOwner (scrubbed to NoOwner by a final packet), this is NOT cleared on
    // final, so a post-final straggler from the same owner is still recognised as
    // stale and dropped instead of re-acquiring the item (#7).
    public byte LastRemoteSequenceOwner = WorldSyncIds.NoOwner;
    public Vector3 TargetPosition;
    public Quaternion TargetRotation = Quaternion.identity;
    // Wire-fed dead reckoning (v27): the owner's rigidbody velocity rides along on
    // vehicle packets, so ApplyRemoteSmoothing eases toward an extrapolated point
    // instead of trailing the last received pose (#14), and mid-drive releases can
    // seed physics instead of dead-stopping the body.
    public Vector3 RemoteVelocity;
    public bool HasRemoteVelocity;

    public bool LocallyOwned;
    public bool DespawnSent;
    public ushort OutSequence;
    public float NextSendAt;

    // Fuel/liquid containers (M8). These are deliberately separate from vehicle
    // fuel: a jerrycan can be held by a different delegated owner than the car.
    public float NextFluidProbeAt;
    public HutongGames.PlayMaker.FsmFloat? FluidLevelVar;
    public HutongGames.PlayMaker.FsmFloat? FluidCapacityVar;
    public HutongGames.PlayMaker.FsmBool? FluidPouringVar;
    public ushort OutFluidSequence;
    public ushort LastRemoteFluidSequence;
    public byte LastRemoteFluidOwner = WorldSyncIds.NoOwner;
    public float NextFluidSendAt;
    public float LastSentFluidLevel = float.NaN;
    public bool LastSentFluidPouring;

    public Vector3 LastPosition;
    public float LastMovedAt = -999f;

    // Cargo pose streaming (v27). On the machine streaming a vehicle, the game's own
    // physics simulates the items riding it and their vehicle-local poses go out in
    // VehicleCargo packets; observers pin listed items kinematically and compose the
    // streamed local pose against their own smoothed vehicle transform. Cargo keeps
    // the authority's live sliding/tumbling instead of freezing to the floor.

    // Sender side: id of the locally-streamed vehicle whose cargo set carries this
    // item (0 = none). While set, the item's own claim/stream pipeline is bypassed.
    public uint LocalCargoVehicleId;
    // Cap-overflow demotions sit out membership for a moment so they don't flap
    // between cargo entry and world-space claim every frame.
    public float LocalCargoBlockedUntil = -999f;

    // Observer side: which remote cargo stream pins this item, and the smoothed
    // vehicle-local pose being composed each frame.
    public uint RemoteCargoVehicleId;
    public byte RemoteCargoOwner = WorldSyncIds.NoOwner;
    public float RemoteCargoAt = -999f;
    public Vector3 RemoteCargoTargetPos;
    public Quaternion RemoteCargoTargetRot = Quaternion.identity;
    public Vector3 RemoteCargoPos;
    public Quaternion RemoteCargoRot = Quaternion.identity;
    public bool RemoteCargoSmoothingInit;
    // Apparent world motion of the composed pin, sampled each compose: the release
    // seed. A rider reads ~the car's velocity; an item the car merely drove past
    // (enter-radius physics shield) reads ~zero and stays put on release.
    public Vector3 RemoteCargoWorldPos;
    public float RemoteCargoWorldAt = -999f;
    public Vector3 RemoteCargoObservedVelocity;

    // Cargo physics hardening on the authority: members ride with continuous
    // collision detection + a depenetration clamp (a bump squeezing a small item
    // against the thin floor colliders must nudge it out, not pop it through), and
    // the streaming vehicle body goes Continuous so member sweeps test against it.
    // Saved values restored when the item exits the set / the stream ends.
    public bool CargoPhysicsSaved;
    public CollisionDetectionMode CargoSavedDetectionMode;
    public float CargoSavedMaxDepenetration;

    // Vehicles only: cargo stream bookkeeping (out on the authority, in on observers).
    public ushort OutCargoSequence;
    public float NextCargoSendAt;
    public bool LocalCargoWasStreaming;
    public ushort LastRemoteCargoSequence;
    public byte LastRemoteCargoOwner = WorldSyncIds.NoOwner;
    public bool RemoteCargoAnnounced;

    // Vehicles only: the game's drive trigger (seat). Blocked while a
    // remote driver holds the vehicle; also anchors the driver's avatar.
    public bool SeatSearched;
    public Transform? SeatTransform;
    public Collider? SeatCollider;
    public bool SeatBlocked;

    // Vehicles only: observer-side visual wheel roll (VehicleWorldSync.Wheels). The tire
    // meshes to spin, their shared radius, and the last body pose used for the frame delta.
    public bool WheelsSearched;
    public Transform[]? WheelSpinners;
    public float WheelRadius;
    public Vector3 WheelLastPos;
    public bool WheelLastPosSet;

    // Vehicles only: stays true from claim/drive until rest final — MWC often
    // leaves PLAYER at the door while the rigidbody moves away.
    public bool LocalDriveActive;

    // Vehicles only: engine / ignition sync (M4).
    public bool SystemsReady;
    public float NextSystemsProbeAt;
    public GameObject? AudioEngine;
    public HutongGames.PlayMaker.FsmFloat? EngineRevsVar;
    public HutongGames.PlayMaker.FsmFloat? GaugeSpeedVar;
    public HutongGames.PlayMaker.FsmFloat? GaugeSpeedAngleVar;
    public HutongGames.PlayMaker.FsmFloat? GaugeRpmVar;
    public HutongGames.PlayMaker.FsmFloat? GaugeTachRevsVar;
    public HutongGames.PlayMaker.FsmFloat? GaugeTachRotationVar;
    public HutongGames.PlayMaker.FsmFloat? GaugeFuelLevelVar;
    public HutongGames.PlayMaker.FsmFloat? GaugeFuelAngleVar;
    public HutongGames.PlayMaker.FsmFloat? FuelTankLevelVar;
    public HutongGames.PlayMaker.FsmFloat? FuelTankCapacityVar;
    public PlayMakerFSM? TurnSignalStalkFsm;
    public PlayMakerFSM? TurnSignalsFsm;
    public PlayMakerFSM? HazardButtonFsm;
    public HutongGames.PlayMaker.FsmBool? BlinkerLeftVar;
    public HutongGames.PlayMaker.FsmBool? BlinkerRightVar;
    public HutongGames.PlayMaker.FsmBool? BlinkerHazardsVar;
    public HutongGames.PlayMaker.FsmFloat? GaugeCoolantVar;
    public HutongGames.PlayMaker.FsmFloat? GaugeCoolantAngleVar;
    public bool RemoteHazard;
    public byte RemoteCoolantTemp;
    public GameObject? GaugeTachNeedle;
    public PlayMakerFSM? ElectricityPowerFsm;
    public PlayMakerFSM? GaugeTachDataFsm;
    public ushort OutVehicleStateSequence;
    public float NextVehicleStateAt;
    public ushort LastVehicleStateSequence;
    public bool RemoteEngineOn;
    public bool RemoteAccOn;
    public bool RemoteElectricsApplied;
    // Set when a new VehicleState packet or an electrics toggle changes the dash; the per-frame
    // remote-presentation path re-applies gauges/lights only when this is set, then clears it.
    public bool RemoteDashDirty;
    public float RemoteRpm;
    public float RemoteSpeedKmh;
    public byte RemoteFuelLevel;
    public bool RemoteBlinkerLeft;
    public bool RemoteBlinkerRight;
    public float RemoteEngineUntil = -999f;
    public AudioSource? RemoteEngineAudio;
    public bool RemoteEngineAudioSearched;
    public bool LoggedEngineSend;

    // Guest fuel-station reconciliation (v39). A vehicle normally gets fuel from
    // its transform-owner VehicleState; these fields cover the separate case where
    // a player stands beside a parked car with a live station nozzle.
    public float LastObservedFuelTransferLevel = float.NaN;
    public float NextFuelTransferAt;
    public ushort OutFuelTransferSequence;
    public byte LastFuelTransferPlayer = WorldSyncIds.NoOwner;
    public ushort LastFuelTransferSequence;
    public float LastFuelTransferAt = -999f;

    // Vehicles only: frost + heater (M4).
    public bool ClimateReady;
    public PlayMakerFSM? GlassFrostingFsm;
    public PlayMakerFSM? FreezingFsm;
    public PlayMakerFSM? CarTempDataFsm;
    public HutongGames.PlayMaker.FsmFloat? FrostVar;
    public HutongGames.PlayMaker.FsmFloat? SweatRateVar;
    public HutongGames.PlayMaker.FsmFloat? GlassTempVar;
    public HutongGames.PlayMaker.FsmBool? PlayerInVar;
    public HutongGames.PlayMaker.FsmFloat? InteriorTempVar;
    public HutongGames.PlayMaker.FsmColor? FrostColorVar;
    public HutongGames.PlayMaker.FsmMaterial? FrostGlassMat;
    public HutongGames.PlayMaker.FsmFloat? CutoffWindshieldVar;
    public HutongGames.PlayMaker.FsmFloat? CutoffSideLeftVar;
    public HutongGames.PlayMaker.FsmFloat? CutoffSideRightVar;
    public HutongGames.PlayMaker.FsmFloat? CutoffDoorLeftVar;
    public HutongGames.PlayMaker.FsmFloat? CutoffDoorRightVar;
    public HutongGames.PlayMaker.FsmFloat? CutoffRearVar;
    public PlayMakerFSM? HeaterUnitFsm;
    public PlayMakerFSM? WindowHeaterButtonFsm;
    public HutongGames.PlayMaker.FsmFloat? HeaterSettingTemp;
    public HutongGames.PlayMaker.FsmFloat? HeaterSettingBlower;
    public HutongGames.PlayMaker.FsmFloat? HeaterSettingDirection;
    public HutongGames.PlayMaker.FsmBool? GlassDefrostingVar;
    public PlayMakerFSM? KnobTempFsm;
    public PlayMakerFSM? KnobBlowerFsm;
    public PlayMakerFSM? KnobDirectionFsm;
    public HutongGames.PlayMaker.FsmFloat? KnobTempSetting;
    public HutongGames.PlayMaker.FsmFloat? KnobBlowerSetting;
    public HutongGames.PlayMaker.FsmFloat? KnobDirectionSetting;
    public HutongGames.PlayMaker.FsmFloat? KnobTempAngle;
    public HutongGames.PlayMaker.FsmFloat? KnobBlowerAngle;
    public HutongGames.PlayMaker.FsmFloat? KnobDirectionAngle;
    public string KnobBlowerCommitState = "Set";
    public HutongGames.PlayMaker.FsmBool? WindowHeaterOnVar;
    public byte RemoteHeaterTemp;
    public byte RemoteHeaterBlower;
    public byte RemoteHeaterDirection;
    public byte PresentedHeaterTemp;
    public byte PresentedHeaterBlower;
    public byte PresentedHeaterDirection;
    public ushort OutClimateSequence;
    public float NextClimateAt;
    public ushort LastClimateSequence;
    // Sender of the climate sequence baseline above. Lets a second nearby observer reset the
    // baseline instead of being permanently locked out by the first sender's higher sequence.
    public byte LastClimateSequenceOwner = WorldSyncIds.NoOwner;
    public byte RemoteFrost;
    public byte RemoteIce;
    public byte RemoteFog;
    public byte RemoteCabinTemp;
    public bool RemotePlayerIn;
    public bool RemoteWindowHeater;
    public bool RemoteGlassDefrosting;
    public float RemoteClimateUntil = -999f;
    public float NextClimateProbeAt;
    public float NextDefrostPulseAt;
    public bool LoggedClimateSend;
    public bool LoggedClimateApply;
    public float NextClimateDiagAt;

    public float ClaimRadius => IsVehicle ? 7f : 4f;
    public float SendRateHz => IsVehicle ? 15f : 10f;
    public float StillSeconds => IsVehicle ? 3f : 1.5f;
    public float MoveThresholdSqr => IsVehicle ? 2.5e-3f : 1e-6f;
    }

    internal struct PendingPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float ExpiresAt;
    }

    internal sealed class SyncedNpc
    {
        public Rigidbody Body = null!;
        public string Path = string.Empty;
        public uint NetId;

        public bool KinematicSaved;
        public bool OriginalKinematic;

        public ushort LastRemoteSequence;
        public float LastRemoteAt = -999f;
        public Vector3 TargetPosition;
        public Quaternion TargetRotation = Quaternion.identity;

        public ushort OutSequence;
        public float NextSendAt;
        public bool HostStreaming;

        public Vector3 LastPosition;
        public float LastMovedAt = -999f;
        public bool GuestRemoteActive;

        // Race opponents run several local driving FSMs with per-peer behavior. A guest
        // freezes exactly those FSMs while receiving the host's rigidbody stream.
        public bool IsIceRaceOpponent;
        public PlayMakerFSM[]? GuestAiFsms;
        public bool[]? GuestAiWasEnabled;
        public bool GuestAiFrozen;
    }

    public struct VehicleInfo
    {
        public uint Id;
        public Rigidbody Body;
    }

    /// <summary>A grocery-bag-style Use FSM whose "Spawn one/all" states instantiate
    /// product clones. The peer whose player opens the bag captures the spilled
    /// clones; the host mints net ids and broadcasts an ItemSpawn manifest; other
    /// peers materialize from it (see ItemWorldSync.Spawn — manifest epochs are
    /// minted there, one counter for host spills and guest offers alike).
    /// Deliberately stays out of the world checksum — a bag spilled on only one
    /// peer must not read as a desync.</summary>
    internal sealed class SyncedSpawnContainer
{
    public PlayMakerFSM Fsm = null!;
    public string Path = string.Empty;
    public string[] SyncedStates = null!;
}

    internal sealed class SyncedDoor
{
    public PlayMakerFSM Fsm = null!;
    public string Path = string.Empty;
    public string[] SyncedStates = null!;
    /// <summary>Last synced state seen locally or applied remotely — the
    /// join snapshot sends this so late joiners converge.</summary>
    public string? LastSyncedState;
}

    internal sealed class SyncedPart
{
    public PlayMakerFSM Fsm = null!;
    public string Path = string.Empty;
    public string[] SyncedStates = null!;
    public string? LastSyncedState;
    public HutongGames.PlayMaker.FsmBool? InstalledVar;
    public HutongGames.PlayMaker.FsmFloat? TightnessVar;
    public HutongGames.PlayMaker.FsmFloat? WearVar;
}

    internal struct PendingPartState
{
    public byte Flags;
    public byte Tightness;
    public byte Wear;
    public float ExpiresAt;
}

    internal struct BuyEntryGuard
{
    public string StateName;
    public string TriggerEvent;
}

    internal struct BuyProfile
{
    public BuyEntryGuard[] EntryGuards;
    public string[] ResultStates;
}

    internal sealed class SyncedBuy
{
    public PlayMakerFSM Fsm = null!;
    public string Path = string.Empty;
    public BuyEntryGuard[] EntryGuards = null!;
    public string[] ResultStates = null!;
    public string? LastSyncedState;
}

    internal struct PendingPurchaseIntent
{
    public uint NetId;
    public string EventName;
    public float ExpiresAt;
}

    internal sealed class SyncedBolt
{
    public PlayMakerFSM Fsm = null!;
    public string Path = string.Empty;
    public HutongGames.PlayMaker.FsmInt? BoltTightnessVar;
    public HutongGames.PlayMaker.FsmInt? ScrewIntVar;
    public HutongGames.PlayMaker.FsmFloat? TightnessFVar;
    public HutongGames.PlayMaker.FsmFloat? ScrewFloatVar;
}

    internal struct PendingBoltState
{
    public ushort BoltTightness;
    public ushort ScrewInt;
    public float ExpiresAt;
}

    internal sealed class SyncedIgnition
{
    public PlayMakerFSM Fsm = null!;
    public string Path = string.Empty;
    public string[] SyncedStates = null!;
    public string? LastSyncedState;
}

    internal sealed class SyncedControl
{
    public PlayMakerFSM Fsm = null!;
    public string Path = string.Empty;
    public string[] SyncedStates = null!;
    public string? LastSyncedState;
}

    internal sealed class SyncedStarter
{
    public PlayMakerFSM Fsm = null!;
    public string Path = string.Empty;
    public string[] SyncedStates = null!;
    public string? LastSyncedState;
}

    internal struct PendingFsmApply
{
    public uint NetId;
    public bool IsRawEvent;
    public string Name;
    public float ExpiresAt;
}


}
