namespace WinterMP.Net.Messages
{
    public sealed class ChatMessage : IMessage
    {
        /// <summary>0 = host. Guests get ids assigned during handshake.</summary>
        public byte SenderPlayerId;
        public string Text = string.Empty;

        public MessageId Id => MessageId.Chat;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(SenderPlayerId);
            writer.WriteString(Text);
        }

        public void Read(NetReader reader)
        {
            SenderPlayerId = reader.ReadByte();
            Text = reader.ReadString();
        }
    }
}
