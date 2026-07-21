namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: the shared repair-shop (Fleetari) order record — which jobs were
    /// ordered, the paint/tire/axle codes, colours and total cost. The <c>Work</c> FSMs that
    /// apply engine tune, gear ratios, paint and bodywork are unhooked, so a service one
    /// player pays for never shows on the shared car for the other. Peers write this record
    /// onto their local <c>OrderFleetari</c> and replay the game's own <c>WORK</c> step, so
    /// the same jobs apply to every copy of the car. Payment routes through the existing
    /// host purchase path (the OrderFleetari Pay is a catalogued buy). See RepairShopSync.
    /// </summary>
    public sealed class FleetariOrderState : IMessage
    {
        /// <summary>An order is active / pending work.</summary>
        public const byte FlagOrder = 1;

        public byte Flags;
        public ushort Sequence;
        public float JobTotalCost;
        public uint CarPaintColor;   // packed RGBA
        public uint RimPaintColor;   // packed RGBA
        public string Jobs = string.Empty;       // UTJobs
        public string OrderCode = string.Empty;  // UTOrder
        public string PaintCode = string.Empty;
        public string AxleCode = string.Empty;
        public string TireCode = string.Empty;

        public MessageId Id => MessageId.FleetariOrderState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(Flags);
            writer.WriteUInt16(Sequence);
            writer.WriteSingle(JobTotalCost);
            writer.WriteUInt32(CarPaintColor);
            writer.WriteUInt32(RimPaintColor);
            writer.WriteString(Jobs);
            writer.WriteString(OrderCode);
            writer.WriteString(PaintCode);
            writer.WriteString(AxleCode);
            writer.WriteString(TireCode);
        }

        public void Read(NetReader reader)
        {
            Flags = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            JobTotalCost = reader.ReadSingle();
            CarPaintColor = reader.ReadUInt32();
            RimPaintColor = reader.ReadUInt32();
            Jobs = reader.ReadString();
            OrderCode = reader.ReadString();
            PaintCode = reader.ReadString();
            AxleCode = reader.ReadString();
            TireCode = reader.ReadString();
        }
    }

    /// <summary>
    /// Guest -> host: the exact <c>OrderFleetari</c> record the guest configured in its local
    /// brochure, captured when it confirms so the host can pair it to that guest's next
    /// <c>PurchaseIntent(PAYMENT)</c> and apply the right service authoritatively. Mirrors
    /// <see cref="MailOrderIntent"/>.
    /// </summary>
    public sealed class FleetariOrderIntent : IMessage
    {
        public byte PlayerId;
        public byte Flags;
        public ushort Sequence;
        public float JobTotalCost;
        public uint CarPaintColor;
        public uint RimPaintColor;
        public string Jobs = string.Empty;
        public string OrderCode = string.Empty;
        public string PaintCode = string.Empty;
        public string AxleCode = string.Empty;
        public string TireCode = string.Empty;

        public MessageId Id => MessageId.FleetariOrderIntent;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteByte(Flags);
            writer.WriteUInt16(Sequence);
            writer.WriteSingle(JobTotalCost);
            writer.WriteUInt32(CarPaintColor);
            writer.WriteUInt32(RimPaintColor);
            writer.WriteString(Jobs);
            writer.WriteString(OrderCode);
            writer.WriteString(PaintCode);
            writer.WriteString(AxleCode);
            writer.WriteString(TireCode);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Flags = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            JobTotalCost = reader.ReadSingle();
            CarPaintColor = reader.ReadUInt32();
            RimPaintColor = reader.ReadUInt32();
            Jobs = reader.ReadString();
            OrderCode = reader.ReadString();
            PaintCode = reader.ReadString();
            AxleCode = reader.ReadString();
            TireCode = reader.ReadString();
        }
    }
}
