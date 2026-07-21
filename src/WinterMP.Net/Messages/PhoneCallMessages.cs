namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: an incoming phone call (COVERAGE-ROADMAP 6.3). Which topic rings and
    /// when is decided by per-client RNG/time, so the phones ring for different calls. The
    /// <b>host</b> decides the call and broadcasts a ring event (topic + a monotonic call id);
    /// guests ring the same call locally (audio/subtitle stay local). Discrete one-shot, not
    /// continuous state. See PhoneSync.
    /// </summary>
    public sealed class PhoneCallEvent : IMessage
    {
        public ushort CallId;
        public string Topic = string.Empty;

        public MessageId Id => MessageId.PhoneCallEvent;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(CallId);
            writer.WriteString(Topic);
        }

        public void Read(NetReader reader)
        {
            CallId = reader.ReadUInt16();
            Topic = reader.ReadString();
        }
    }
}
