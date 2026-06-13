using System;
using System.Collections.Generic;

namespace WinterMP.FastBoot
{
    /// <summary>
    /// Treat nonessential ES2 tags as missing during Continue→GAME hydrate only.
    /// Must NOT run during MainMenu save-check (Check Save FSM) — that slowed splash→menu.
    /// </summary>
    internal static class Es2HydratePolicy
    {
        private static readonly string[] DefaultSkipPrefixes =
        {
            "Rally", "Stats", "StatSlot",
            "ListReg", "ListRand", "ListPic",
            "Phone1", "Phone2", "Fleamarket", "SpeedCam",
            "ShitWell", "ShitCrime", "Wanted",
            "RepairShop", "FactoryWorker", "AdJob",
            "Lotto", "Kela2", "PlayerLeaderboard",
            "Prime", "ScrapPrice",
            "Wood1Order", "Wood2Order", "Wood3Order", "Wood4Order",
            "TaxiJob", "JobPaychecks", "JobMin", "JobProgress", "JobStage", "JobUOLetter",
            "PlayerDayFines", "PlayerNetIncome", "PlayerWork",
            "BBSConline", "Classifieds", "StoreWindowsTimer",
            "Ventti", "MOil", "386Memory", "386Purchased",
            "fuseholder", "floppy", "helmet", "alarm", "woodcarrier",
            "CD1", "CD2", "CD3",
        };

        private static string[] _skipPrefixes = DefaultSkipPrefixes;
        private static bool _skipTagsEnabled;
        private static bool _aggressiveSkip;
        private static bool _whitelistMode;
        private static bool _continueHydrateActive;

        /// <summary>Only hydrate tags needed to boot; skip everything else in whitelist mode.</summary>
        private static readonly string[] BootKeepPrefixes =
        {
            "WorldTime", "WorldDay", "WorldDaysPassed", "WorldWeeksPassed", "Weather",
            "PlayerTransform", "PlayerMoney", "PlayerName", "PlayerHunger", "PlayerFatigue",
            "GameStartDate", "Satsuma", "Gifu", "Kekmet", "SOORBT", "CarBattery",
            "CarBuyStage", "House", "Flat", "Mail", "Fuel",
        };

        public static int SkippedTagChecks { get; private set; }
        public static int SkippedTagReads { get; private set; }

        public static bool ShouldApply => _skipTagsEnabled && _continueHydrateActive;

        public static void Configure(bool skipTagsEnabled, bool aggressiveSkip, bool whitelistMode, string? extraPrefixesCsv)
        {
            _skipTagsEnabled = skipTagsEnabled;
            _aggressiveSkip = aggressiveSkip;
            _whitelistMode = whitelistMode;
            if (string.IsNullOrEmpty(extraPrefixesCsv))
            {
                _skipPrefixes = DefaultSkipPrefixes;
                return;
            }

            string[] extra = extraPrefixesCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var merged = new List<string>(DefaultSkipPrefixes.Length + extra.Length);
            merged.AddRange(DefaultSkipPrefixes);
            for (int i = 0; i < extra.Length; i++)
            {
                string p = extra[i].Trim();
                if (p.Length > 0) merged.Add(p);
            }

            _skipPrefixes = merged.ToArray();
        }

        public static void BeginContinueHydrate()
        {
            if (_continueHydrateActive) return;
            _continueHydrateActive = true;
            SkippedTagChecks = 0;
            SkippedTagReads = 0;
        }

        public static void EndContinueHydrate()
        {
            _continueHydrateActive = false;
        }

        public static void ResetBoot()
        {
            _continueHydrateActive = false;
            SkippedTagChecks = 0;
            SkippedTagReads = 0;
        }

        public static bool TryShortCircuitTag(string? tag, out bool exists)
        {
            exists = false;
            if (!ShouldApply || !ShouldSkipTag(tag))
                return false;

            NoteSkippedCheck();
            return true;
        }

        public static bool ShouldSkipTag(string? tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;

            if (_whitelistMode && _continueHydrateActive)
            {
                for (int i = 0; i < BootKeepPrefixes.Length; i++)
                {
                    if (tag.StartsWith(BootKeepPrefixes[i], StringComparison.Ordinal))
                        return false;
                }

                return true;
            }

            if (tag.IndexOf("(item", StringComparison.OrdinalIgnoreCase) >= 0
                || tag.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (_aggressiveSkip)
            {
                if (tag.EndsWith("Pos4", StringComparison.Ordinal)
                    || tag.EndsWith("Pos8", StringComparison.Ordinal)
                    || tag.EndsWith("Pos24", StringComparison.Ordinal)
                    || tag.IndexOf("Transform4", StringComparison.Ordinal) >= 0
                    || tag.IndexOf("Transform8", StringComparison.Ordinal) >= 0
                    || tag.IndexOf("Transform18", StringComparison.Ordinal) >= 0
                    || tag.IndexOf("Transform6", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            for (int i = 0; i < _skipPrefixes.Length; i++)
            {
                if (tag.StartsWith(_skipPrefixes[i], StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public static string[] FilterTags(string[] tags)
        {
            if (!ShouldApply || tags == null || tags.Length == 0)
                return tags ?? new string[0];

            var kept = new List<string>(tags.Length);
            for (int i = 0; i < tags.Length; i++)
            {
                string tag = tags[i];
                if (ShouldSkipTag(tag))
                {
                    NoteSkippedCheck();
                    continue;
                }

                kept.Add(tag);
            }

            return kept.ToArray();
        }

        public static void NoteSkippedCheck()
        {
            SkippedTagChecks++;
        }

        public static void NoteSkippedRead(string? tag)
        {
            if (ShouldSkipTag(tag))
                SkippedTagReads++;
        }
    }
}
