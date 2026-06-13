namespace WinterMP.Launcher.Services
{
    /// <summary>Compares mod/launcher version strings without tripping on 0.1.23 vs 0.1.23.0.</summary>
    internal static class ModVersionHelper
    {
        public static bool TryParse(string? text, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();
            if (string.Equals(trimmed, "unknown version", StringComparison.OrdinalIgnoreCase))
                return false;

            if (Version.TryParse(trimmed, out Version? parsed) && parsed != null)
            {
                version = parsed;
                return true;
            }

            int dash = trimmed.IndexOf('-');
            if (dash > 0 && Version.TryParse(trimmed[..dash], out parsed) && parsed != null)
            {
                version = parsed;
                return true;
            }

            return false;
        }

        public static Version Normalize(Version version) =>
            new(version.Major, version.Minor, Math.Max(version.Build, 0));

        public static bool IsNewerThan(Version candidate, Version baseline) =>
            Normalize(candidate).CompareTo(Normalize(baseline)) > 0;

        public static bool IsNewerThan(string? candidateText, Version baseline)
        {
            if (!TryParse(candidateText, out Version candidate)) return false;
            return IsNewerThan(candidate, baseline);
        }

        public static bool VersionsMatch(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;

            if (!TryParse(a, out Version va) || !TryParse(b, out Version vb))
                return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

            return Normalize(va).CompareTo(Normalize(vb)) == 0;
        }
    }
}
