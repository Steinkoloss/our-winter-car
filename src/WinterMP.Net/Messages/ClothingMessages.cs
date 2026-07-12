namespace WinterMP.Net.Messages
{
    /// <summary>
    /// A player's worn clothing changed. Owner-authoritative and relayed:
    /// guest -> host -> other guests (the host also broadcasts its own change).
    /// Drives the remote-avatar visual and informs local warmth math
    /// (ClothingStage on PLAYER/BodyTemp). See ClothingSync in WinterMP.Core.
    /// </summary>
    public sealed class PlayerClothingState : IMessage
    {
        /// <summary>Session player id whose clothing this describes.</summary>
        public byte PlayerId;

        /// <summary>Warmth tier (FsmInt ClothingStage on PLAYER/BodyTemp), clamped to a byte.</summary>
        public byte ClothingStage;

        /// <summary>Outfit variant (FsmInt ClothingType on the Piss FSM), clamped to a byte.</summary>
        public byte ClothingType;

        public MessageId Id => MessageId.PlayerClothingState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteByte(ClothingStage);
            writer.WriteByte(ClothingType);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            ClothingStage = reader.ReadByte();
            ClothingType = reader.ReadByte();
        }
    }
}
