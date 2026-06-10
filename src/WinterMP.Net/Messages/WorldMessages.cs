namespace WinterMP.Net.Messages
{
    /// <summary>
    /// A cataloged PlayMaker FSM entered a synced state on the sender's machine
    /// (e.g. a door entered "Open door"). Receivers replay the entry through an
    /// injected global transition; see WorldSyncManager in WinterMP.Core.
    /// </summary>
    public sealed class FsmStateEnter : IMessage
    {
        /// <summary>Stable id of the FSM (scene-path hash, see PLAN §4.1).</summary>
        public uint NetId;
        public string StateName = string.Empty;

        public MessageId Id => MessageId.FsmStateEnter;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(NetId);
            writer.WriteString(StateName);
        }

        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32();
            StateName = reader.ReadString();
        }
    }

    /// <summary>
    /// A raw PlayMaker event to deliver to a cataloged FSM on the receiver
    /// (e.g. TIGHTEN/UNTIGHTEN on a bolt's Screw FSM). Receivers whitelist the
    /// event names they are willing to replay.
    /// </summary>
    public sealed class FsmRawEvent : IMessage
    {
        public uint NetId;
        public string EventName = string.Empty;

        public MessageId Id => MessageId.FsmRawEvent;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(NetId);
            writer.WriteString(EventName);
        }

        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32();
            EventName = reader.ReadString();
        }
    }

    /// <summary>
    /// Pose of a world object (pickable item or vehicle root rigidbody) streamed by
    /// its current owner. Streamed on <see cref="Channel.UnreliableSequenced"/>
    /// while moving; the final at-rest pose is sent once with <see cref="FlagFinal"/>
    /// on <see cref="Channel.ReliableOrdered"/> so resting positions converge.
    /// </summary>
    public sealed class ItemTransform : IMessage
    {
        /// <summary>Set on the last packet of a stream: object came to rest at this pose.</summary>
        public const byte FlagFinal = 1;

        /// <summary>
        /// The sender's player is *driving* this vehicle. Driver claims beat
        /// proximity claims regardless of player id (see ownership rules).
        /// </summary>
        public const byte FlagDriver = 2;

        public uint ItemId;
        /// <summary>Session player id of the peer simulating this item right now.</summary>
        public byte OwnerPlayerId;
        public ushort Sequence;
        public byte Flags;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;

        public bool IsFinal => (Flags & FlagFinal) != 0;
        public bool IsDriver => (Flags & FlagDriver) != 0;

        public MessageId Id => MessageId.ItemTransform;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(ItemId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Flags);
            writer.WriteVector3(Position);
            writer.WriteQuaternion(Rotation);
        }

        public void Read(NetReader reader)
        {
            ItemId = reader.ReadUInt32();
            OwnerPlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Flags = reader.ReadByte();
            Position = reader.ReadVector3();
            Rotation = reader.ReadQuaternion();
        }
    }

    /// <summary>
    /// Host-authoritative game clock and weather, broadcast periodically and on
    /// join. Guests jump their sun/clock FSMs when drift exceeds a threshold and
    /// overwrite the weather forecast variables (host's forecast wins).
    /// </summary>
    public sealed class TimeSync : IMessage
    {
        /// <summary>Game hour, 1-24 (the SUN clock FSM's own convention).</summary>
        public byte Hour;
        public float Minutes;
        public float TempOld;
        public float TempNew;
        public bool Snowing;
        public int ForecastIndex;

        public MessageId Id => MessageId.TimeSync;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(Hour);
            writer.WriteSingle(Minutes);
            writer.WriteSingle(TempOld);
            writer.WriteSingle(TempNew);
            writer.WriteBool(Snowing);
            writer.WriteInt32(ForecastIndex);
        }

        public void Read(NetReader reader)
        {
            Hour = reader.ReadByte();
            Minutes = reader.ReadSingle();
            TempOld = reader.ReadSingle();
            TempNew = reader.ReadSingle();
            Snowing = reader.ReadBool();
            ForecastIndex = reader.ReadInt32();
        }
    }
}
