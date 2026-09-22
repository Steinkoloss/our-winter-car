namespace WinterMP.Net.Sync
{
    public static class PartCamshaftPolicy
    {
        // Native Valves parses two four-digit RPM values with GetSubstring.
        public static bool ValidProfile(string? value, bool allowEmpty = false)
        {
            if (value == null) return false;
            if (value.Length == 0) return allowEmpty;
            if (value.Length != 8) return false;
            foreach (char ch in value) if (ch < '0' || ch > '9') return false;
            return true;
        }
    }
}
