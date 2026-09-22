using System;
using System.IO;
using System.Reflection;
using WinterMP.Core.Util;
using Xunit;

// Only the filesystem/log-role dependencies are substituted. The trace and
// exception propagation under test are the production net35 source.
namespace BepInEx
{
    internal static class Paths
    {
        public static string GameRootPath = string.Empty;
    }
}

namespace WinterMP.Core.Util
{
    internal static class InstanceLogRedirect
    {
        public static string Role = "host";
    }
}

namespace WinterMP.Net.Tests
{
    public class BootTraceTests
    {
        [Fact]
        public void PairedStepsFlushBoundedErrorsAndPreserveActionAndExceptionSemantics()
        {
            string root = Path.Combine(Path.GetTempPath(), "wintermp-boot-trace-" + Guid.NewGuid().ToString("N"));
            var writerField = typeof(BootTrace).GetField("_writer", BindingFlags.NonPublic | BindingFlags.Static)!;
            BepInEx.Paths.GameRootPath = root;
            try
            {
                int calls = 0;
                BootTrace.Step("successful", () => calls++);
                Assert.Equal(1, calls);
                string log = Path.Combine(root, "WinterMP", "boot-trace-host.log");
                string text = File.ReadAllText(log);
                Assert.Contains("successful begin", text);
                Assert.Contains("successful end", text);
                Assert.Contains("pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + " role=host", text);
                Assert.Matches(@"\[\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z\]", text);

                var error = new InvalidOperationException("failure\n" + new string('x', 5000));
                Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
                    BootTrace.Step("failed", () => { calls++; throw error; })));
                Assert.Equal(2, calls);
                text = File.ReadAllText(log);
                Assert.Contains("failed begin", text);
                Assert.Contains("failed error System.InvalidOperationException: failure ", text);
                Assert.DoesNotContain("failed end", text);
                foreach (string line in File.ReadAllLines(log)) Assert.True(line.Length < 2300);

                ((StreamWriter)writerField.GetValue(null)!).Dispose();
                writerField.SetValue(null, null);
                // A trace I/O failure must neither skip the real operation nor
                // replace its exception (e.g. a read-only/unavailable game root).
                BepInEx.Paths.GameRootPath = log;
                BootTrace.Step("unwritable", () => calls++);
                Assert.Equal(3, calls);
                Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
                    BootTrace.Step("unwritable failure", () => throw error)));
            }
            finally
            {
                (writerField.GetValue(null) as StreamWriter)?.Dispose();
                writerField.SetValue(null, null);
                if (Directory.Exists(root)) Directory.Delete(root, true);
                BepInEx.Paths.GameRootPath = string.Empty;
            }
        }
    }
}
