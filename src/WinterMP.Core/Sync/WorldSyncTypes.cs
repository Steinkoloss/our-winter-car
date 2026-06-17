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
    // Receiver-side dead reckoning: implied velocity from the last two accepted poses
    // lets ApplyRemoteSmoothing ease toward a predicted point instead of trailing the
    // last received pose, removing the steady-state lag behind a fast remote car (#14).
    public float PrevRemoteAt = -999f;
    public Vector3 RemoteVelocity;
    public bool HasRemoteVelocity;

    public bool LocallyOwned;
    public bool DespawnSent;
    public ushort OutSequence;
    public float NextSendAt;

    public Vector3 LastPosition;
    public float LastMovedAt = -999f;

    // Guests parent cargo to a remote-driven vehicle instead of lerping independent
    // world-space item streams (which desync from the vehicle and fight physics).
    public bool CargoFollowActive;
    public uint CargoFollowVehicleId;
    public byte CargoFollowDriverId = WorldSyncIds.NoOwner;
    public Vector3 CargoFollowLocalPos;
    public Quaternion CargoFollowLocalRot = Quaternion.identity;

    // While welded as cargo the item is kinematic (infinite mass) and we drive its
    // pose directly, so its solid colliders have nothing to do but fight the car they
    // ride in — pinning the dynamic chassis (the car "can't move") and slamming the
    // hinged door rigidbodies (doors glitch / become unclickable). Disable them for
    // the duration of the weld and restore exactly the ones we turned off.
    public bool CargoCollidersDisabled;
    public Collider[]? CargoDisabledColliders;

    // Vehicles only: the game's drive trigger (seat). Blocked while a
    // remote driver holds the vehicle; also anchors the driver's avatar.
    public bool SeatSearched;
    public Transform? SeatTransform;
    public Transform? DriverAnchorTransform;
    public Collider? SeatCollider;
    public bool SeatBlocked;

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
    public float RemoteRpm;
    public float RemoteSpeedKmh;
    public byte RemoteFuelLevel;
    public bool RemoteBlinkerLeft;
    public bool RemoteBlinkerRight;
    public float RemoteEngineUntil = -999f;
    public AudioSource? RemoteEngineAudio;
    public bool RemoteEngineAudioSearched;
    public bool LoggedEngineSend;

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
    public byte RemoteFrost;
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
    }

    public struct VehicleInfo
    {
        public uint Id;
        public Rigidbody Body;
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
