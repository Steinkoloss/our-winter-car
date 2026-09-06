namespace WinterMP.Net
{
    /// <summary>Guest world data remains unsafe to persist after transport or scene teardown.</summary>
    public sealed class GuestSavePolicy
    {
        public bool ProtectWorld { get; private set; }
        public bool CanHost { get { return !ProtectWorld; } }

        public bool TryBeginGuest(bool persistenceGuardReady)
        {
            if (!persistenceGuardReady) return false;
            ProtectWorld = true;
            return true;
        }

        // There is deliberately no session-reset operation. PlayMaker globals and
        // native save callbacks can outlive both a connection and the GAME scene.
        public bool CanPersist(bool memoryOnly)
        {
            return memoryOnly || !ProtectWorld;
        }
    }
}
