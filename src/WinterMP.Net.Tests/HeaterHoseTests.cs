using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class HeaterHoseTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Fact]
        public void OnlyTheTwoHeaterHosesRequireNativeInstalledReads()
        {
            var profile = SyncCatalogJson.Parse(Catalog().ToJsonString()).GuestEngineInputs!;
            var block = Assert.IsType<EngineBlockData>(profile.Block);
            Assert.Equal(new byte[] { 2, 3 }, block.CoolantHoses
                .Where(hose => hose.ProjectNativeReads).Select(hose => hose.Index).ToArray());
            Assert.Equal(new[] { "VIN216", "VIN217" }, block.CoolantHoses
                .Where(hose => hose.ProjectNativeReads).Select(hose => hose.Mount.PartPrefix).ToArray());
            Assert.All(block.CoolantHoses.Skip(2), hose => Assert.Equal("Update", hose.Mount.ReadyState));
            Assert.Equal(102, profile.Entries.Count);
            Assert.Equal(182, profile.Entries.Sum(entry => entry.Readers.Count));
        }

        [Theory]
        [InlineData(2, null)]
        [InlineData(3, null)]
        [InlineData(2, "false")]
        [InlineData(3, "false")]
        [InlineData(2, "1")]
        [InlineData(3, "\"true\"")]
        [InlineData(2, "null")]
        [InlineData(0, "true")]
        [InlineData(1, "true")]
        public void IncompleteOrForeignHoseProjectionBlocksInputAdmission(int index, string? value)
        {
            var json = Catalog();
            var hose = json["guestEngineInputs"]!["block"]!["coolantHoses"]![index]!.AsObject();
            if (value == null) hose.Remove("projectNativeReads");
            else hose["projectNativeReads"] = JsonNode.Parse(value);

            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineInputs);
            Assert.NotNull(parsed.GuestEngineProtection);
            Assert.NotEmpty(parsed.Doors);
        }
    }
}
