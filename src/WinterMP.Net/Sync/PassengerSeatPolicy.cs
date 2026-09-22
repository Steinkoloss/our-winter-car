namespace WinterMP.Net.Sync
{
    public static class PassengerSeatPolicy
    {
        public const byte AllSeats = 7;
        // The taxi's rear-right seat (1) belongs to its native fare customer.
        public const byte TaxiSeats = 5;

        public static bool Available(byte seats, int seat, bool vehicleActive, bool tutorialActive) =>
            vehicleActive && !tutorialActive && seat >= 0 && seat < 3 && (seats & (1 << seat)) != 0;
    }
}
