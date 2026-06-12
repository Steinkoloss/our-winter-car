namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Bit flags streamed in <see cref="WinterMP.Net.Messages.PlayerTransform.MoveState"/>.
    /// Derived locally from CharacterController, velocity, and key PLAYER FSMs;
    /// replayed on remote <see cref="RemoteAvatar"/> rigs cloned from HUMANS walkers.
    /// </summary>
    public static class PlayerMoveState
    {
        public const byte Walking = 1;
        public const byte Running = 2;
        public const byte Crouch = 4;
        public const byte Carry = 8;
        public const byte Driving = 16;
        public const byte Passenger = 32;
        public const byte Swimming = 64;

        public static bool Has(byte moveState, byte flag) => (moveState & flag) != 0;
    }
}
