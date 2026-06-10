namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Wire format per packet: [ushort messageId][payload]. Transports are datagram-based,
    /// so no outer length prefix is needed.
    /// </summary>
    public static class PacketCodec
    {
        public static byte[] Encode(IMessage message)
        {
            var writer = new NetWriter();
            writer.WriteUInt16((ushort)message.Id);
            message.Write(writer);
            return writer.ToArray();
        }

        /// <summary>Encode into a reusable writer; send writer.Buffer[0..writer.Length] for zero-copy paths.</summary>
        public static void Encode(IMessage message, NetWriter writer)
        {
            writer.Reset();
            writer.WriteUInt16((ushort)message.Id);
            message.Write(writer);
        }

        /// <summary>Throws <see cref="ProtocolException"/> on unknown ids or truncated payloads.</summary>
        public static IMessage Decode(byte[] payload)
        {
            var reader = new NetReader(payload);
            var id = (MessageId)reader.ReadUInt16();
            var message = MessageRegistry.Create(id);
            message.Read(reader);
            return message;
        }
    }
}
