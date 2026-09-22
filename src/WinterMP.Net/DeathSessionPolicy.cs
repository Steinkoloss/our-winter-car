namespace WinterMP.Net
{
    /// <summary>A permadeath wipe ends the run until the transport session is reset.</summary>
    public sealed class DeathSessionPolicy
    {
        public bool Wiped { get; private set; }
        public bool CanJoin => !Wiped;

        public bool CanRespawn(bool permanentDeath) => !permanentDeath && !Wiped;

        public bool TryWipe(bool permanentDeath)
        {
            if (!permanentDeath || Wiped) return false;
            Wiped = true;
            return true;
        }

        public void Reset() { Wiped = false; }
    }
}
