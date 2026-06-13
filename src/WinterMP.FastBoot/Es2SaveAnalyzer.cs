using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using UnityEngine;

namespace WinterMP.FastBoot
{
    /// <summary>
    /// Lists ES2 tag names from savefile.txt (offline scan + live GetTags during hydrate).
    /// </summary>
    internal static class Es2SaveAnalyzer
    {
        private const string SaveFileName = "savefile.txt";
        private static bool _wroteReport;

        public static void TryWriteOfflineReport(ManualLogSource log)
        {
            if (_wroteReport) return;

            try
            {
                string path = Path.Combine(Application.persistentDataPath, SaveFileName);
                if (!File.Exists(path)) return;

                string[] tags = ExtractTagsFromBytes(File.ReadAllBytes(path));
                WriteReport(log, tags, "offline scan of " + path);
                _wroteReport = true;
            }
            catch (Exception e)
            {
                log.LogWarning("FastBoot: ES2 save analyzer failed: " + e.Message);
            }
        }

        public static void RecordLiveTags(object es2Reader, ManualLogSource log)
        {
            if (_wroteReport || es2Reader == null) return;

            try
            {
                MethodInfo? getTags = es2Reader.GetType().GetMethod(
                    "GetTags",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);
                if (getTags == null) return;

                var tagsObj = getTags.Invoke(es2Reader, null) as string[];
                if (tagsObj == null) return;

                WriteReport(log, tagsObj, "live ES2Reader.GetTags during hydrate");
                _wroteReport = true;
            }
            catch (Exception e)
            {
                log.LogWarning("FastBoot: live ES2 tag dump failed: " + e.Message);
            }
        }

        public static void ResetBoot()
        {
            // keep _wroteReport false each boot so each session gets fresh report if hydrate runs
            _wroteReport = false;
        }

        private static string[] ExtractTagsFromBytes(byte[] data)
        {
            var tags = new HashSet<string>(StringComparer.Ordinal);
            string ascii = Encoding.ASCII.GetString(data);
            MatchCollection matches = Regex.Matches(ascii, @"[\x20-\x7E]{3,80}");
            for (int i = 0; i < matches.Count; i++)
            {
                string s = matches[i].Value;
                if (s.StartsWith("~", StringComparison.Ordinal)
                    || s.StartsWith("?", StringComparison.Ordinal)
                    || s.Contains("{")
                    || s.Contains("<")
                    || s.Contains(">"))
                {
                    continue;
                }

                if (s.StartsWith("bool(", StringComparison.Ordinal)
                    || s.StartsWith("int(", StringComparison.Ordinal)
                    || s.StartsWith("float(", StringComparison.Ordinal)
                    || s.StartsWith("string(", StringComparison.Ordinal))
                {
                    continue;
                }

                if (s.EndsWith("~", StringComparison.Ordinal))
                    s = s.Substring(0, s.Length - 1);

                if (s.Length >= 3)
                    tags.Add(s);
            }

            var list = new List<string>(tags);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        private static void WriteReport(ManualLogSource log, string[] tags, string source)
        {
            int skipCount = CountSkipped(tags);
            log.LogInfo(
                "FastBoot: ES2 save analyzer ("
                + source
                + ") — "
                + tags.Length
                + " tags, "
                + skipCount
                + " would be skipped in dev. Full list: WinterMP/es2-tags.log");

            var sb = new StringBuilder();
            sb.AppendLine("=== ES2 save tag report (" + source + ") ===");
            sb.AppendLine("  Tag count: " + tags.Length);
            sb.AppendLine("  Dev skip would ignore: " + skipCount);
            sb.AppendLine("--- tags ---");
            for (int i = 0; i < tags.Length; i++)
            {
                string mark = Es2HydratePolicy.ShouldSkipTag(tags[i]) ? " [skip]" : string.Empty;
                sb.AppendLine("  " + tags[i] + mark);
            }

            string report = sb.ToString();

            try
            {
                string dir = Path.Combine(BepInEx.Paths.GameRootPath, "WinterMP");
                Directory.CreateDirectory(dir);
                File.WriteAllText(
                    Path.Combine(dir, "es2-tags.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]\n" + report);
            }
            catch
            {
                // best effort
            }
        }

        private static int CountSkipped(string[] tags)
        {
            int n = 0;
            for (int i = 0; i < tags.Length; i++)
            {
                if (Es2HydratePolicy.ShouldSkipTag(tags[i])) n++;
            }

            return n;
        }
    }
}
