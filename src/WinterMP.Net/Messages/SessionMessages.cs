namespace WinterMP.Net.Messages
{
    /// <summary>First message a client sends after the transport connects. Host validates and replies.</summary>
    public sealed class HandshakeRequest : IMessage
    {
        public ushort ProtocolVersion = ProtocolInfo.Version;
        public string ModVersion = string.Empty;
        public string GameVersion = string.Empty;
        /// <summary>Hash of the generated sync catalog; both sides must run identical game builds.</summary>
        public uint CatalogHash;
        public string PlayerName = string.Empty;

        public MessageId Id => MessageId.HandshakeRequest;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(ProtocolVersion);
            writer.WriteString(ModVersion);
            writer.WriteString(GameVersion);
            writer.WriteUInt32(CatalogHash);
            writer.WriteString(PlayerName);
        }

        public void Read(NetReader reader)
        {
            ProtocolVersion = reader.ReadUInt16();
            ModVersion = reader.ReadString();
            GameVersion = reader.ReadString();
            CatalogHash = reader.ReadUInt32();
            PlayerName = reader.ReadString();
        }
    }

    public sealed class HandshakeResponse : IMessage
    {
        public bool Accepted;
        /// <summary>Human-readable refusal reason shown to the joining player.</summary>
        public string Reason = string.Empty;
        /// <summary>Session-scoped small id assigned by the host (host is always 0).</summary>
        public byte PlayerId;
        public string HostPlayerName = string.Empty;

        public MessageId Id => MessageId.HandshakeResponse;

        public void Write(NetWriter writer)
        {
            writer.WriteBool(Accepted);
            writer.WriteString(Reason);
            writer.WriteByte(PlayerId);
            writer.WriteString(HostPlayerName);
        }

        public void Read(NetReader reader)
        {
            Accepted = reader.ReadBool();
            Reason = reader.ReadString();
            PlayerId = reader.ReadByte();
            HostPlayerName = reader.ReadString();
        }
    }

    public sealed class PingMessage : IMessage
    {
        public uint Nonce;
        public long SenderTimeMs;

        public MessageId Id => MessageId.Ping;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(Nonce);
            writer.WriteInt64(SenderTimeMs);
        }

        public void Read(NetReader reader)
        {
            Nonce = reader.ReadUInt32();
            SenderTimeMs = reader.ReadInt64();
        }
    }

    public sealed class PongMessage : IMessage
    {
        public uint Nonce;
        public long SenderTimeMs;

        public MessageId Id => MessageId.Pong;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(Nonce);
            writer.WriteInt64(SenderTimeMs);
        }

        public void Read(NetReader reader)
        {
            Nonce = reader.ReadUInt32();
            SenderTimeMs = reader.ReadInt64();
        }
    }

    public sealed class DisconnectMessage : IMessage
    {
        public string Reason = string.Empty;

        public MessageId Id => MessageId.Disconnect;

        public void Write(NetWriter writer) => writer.WriteString(Reason);

        public void Read(NetReader reader) => Reason = reader.ReadString();
    }
}
