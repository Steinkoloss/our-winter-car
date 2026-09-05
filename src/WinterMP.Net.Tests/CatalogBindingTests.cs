using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class CatalogBindingTests
    {
        private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!.AsObject();

        [Fact]
        public void DebtLetterBindingsFollowRentAndItsMovableEnvelope()
        {
            var json = Catalog();
            var data = SyncCatalogJson.Parse(json.ToJsonString()).DebtLetter!;
            Assert.Equal("Systems/Expenses", data["rentPath"]);
            Assert.Equal("Letter", data["rentEnvelope"]);
            Assert.Equal("Interest", data["interest"]);
            Assert.Equal("Date", data["commitState"]);
            json["debtLetter"]!["rentEnvelope"] = "UpdatedLetter";
            json["debtLetter"]!["commitState"] = "UpdatedPayment";
            data = SyncCatalogJson.Parse(json.ToJsonString()).DebtLetter!;
            Assert.Equal("UpdatedLetter", data["rentEnvelope"]);
            Assert.Equal("UpdatedPayment", data["commitState"]);
        }

        [Fact]
        public void EveryDebtLetterBindingIsRequiredBeforeInstallingPaymentHooks()
        {
            foreach (string key in DebtLetterData.RequiredBindings)
            {
                var json = Catalog();
                json["debtLetter"]!.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            }
        }

        [Fact]
        public void PokerBindingsIncludeAllControlsVisualSourcesAndNativePayouts()
        {
            var json = Catalog();
            json["videoPoker"]!["menuResetState"] = "UpdatedMenu";
            json["videoPoker"]!["assets"]![0]!["state"] = "UpdatedScreen";
            var poker = SyncCatalogJson.Parse(json.ToJsonString()).VideoPoker!;
            Assert.Equal(12, poker.Buttons.Length);
            Assert.Equal(5, poker.Cards.Length);
            Assert.Equal(44, poker.Assets.Count);
            Assert.Equal(new[] { 0, 1, 2, 3, 5, 7, 10, 15, 30, 50 }, poker.Payouts);
            Assert.Equal("UpdatedMenu", poker["menuResetState"]);
            Assert.Equal("UpdatedScreen", poker.Assets["screenGame"].State);
        }

        [Theory]
        [InlineData("buttons")]
        [InlineData("cards")]
        [InlineData("backs")]
        [InlineData("buttonMeshes")]
        [InlineData("assets")]
        public void IncompletePokerBindingsCannotPartiallyTakeOverTheMachine(string field)
        {
            var json = Catalog();
            json["videoPoker"]![field]!.AsArray().RemoveAt(0);
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, -1)]
        [InlineData(2, 1.5)]
        [InlineData(9, 51)]
        public void InvalidPokerPayoutsAreRejected(int category, double payout)
        {
            var json = Catalog();
            json["videoPoker"]!["payouts"]![category] = payout;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Theory]
        [InlineData("buttons")]
        [InlineData("assets")]
        public void DuplicatedPokerBindingsAreRejected(string field)
        {
            var json = Catalog();
            json["videoPoker"]![field]![1] = json["videoPoker"]![field]![0]!.DeepClone();
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void ShippedCatalogRetainsRetiredSlotsAndTransferDirections()
        {
            var data = SyncCatalogJson.Parse(Catalog().ToJsonString());
            Assert.NotNull(data.Banking);
            Assert.NotNull(data.VehicleDamage);
            Assert.Equal(5, data.Banking.Mutations.Count);
            Assert.Contains(data.Banking.Mutations, m => m.Direction == 1 && m.AmountVariable.Length > 0);
            Assert.Contains(data.Banking.Mutations, m => m.Direction == -1 && m.AmountVariable.Length > 0);
            Assert.Equal(16, data.VehicleDamage.Events.Count);
            Assert.Equal(16, data.VehicleDamage.PartVariables.Count);
            Assert.Equal("", data.VehicleDamage.Events[13]);
            Assert.Equal("BLOCK", data.VehicleDamage.Events[14]);
            Assert.Equal("", data.VehicleDamage.PartVariables[15]);
        }

        [Fact]
        public void GameUpdateBindingsAreReadFromCatalogWithoutChangingProtocolSlots()
        {
            var json = Catalog();
            json["banking"]!["atmPath"] = "UpdatedTown/ATM";
            json["banking"]!["cashGlobal"] = "UpdatedCash";
            json["vehicleDamage"]!["partVariables"]![14] = "UpdatedBlock";
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Equal("UpdatedTown/ATM", parsed.Banking!.AtmPath);
            Assert.Equal("UpdatedCash", parsed.Banking.CashGlobal);
            Assert.Equal("UpdatedBlock", parsed.VehicleDamage!.PartVariables[14]);
        }

        [Theory]
        [InlineData(13)]
        [InlineData(15)]
        public void RetiredSlotsCannotBeReused(int slot)
        {
            var json = Catalog();
            json["vehicleDamage"]!["events"]![slot] = "NEW_EVENT";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Theory]
        [InlineData("SEIZE")]
        [InlineData("CAMFAIL")]
        public void ConcreteSlotsCannotRerollRandomDamage(string selector)
        {
            var json = Catalog();
            json["vehicleDamage"]!["events"]![0] = selector;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void RemovingASlotCannotSilentlyRenumberTheFollowingParts()
        {
            var json = Catalog();
            json["vehicleDamage"]!["partVariables"]!.AsArray().RemoveAt(13);
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Theory]
        [InlineData("target", "anything")]
        [InlineData("balance", "anything")]
        [InlineData("amountVariable", "")]
        public void InvalidTransferBindingsFailBeforeHooksInstall(string key, string value)
        {
            var json = Catalog();
            json["banking"]!["mutations"]![2]![key] = value;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void DuplicateMutationCannotQueueOrGateASecondTransfer()
        {
            var json = Catalog();
            var mutations = json["banking"]!["mutations"]!.AsArray();
            mutations.Add(mutations[2]!.DeepClone());
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void SlotBindingsRetainSeparateBankrollsAndWeightedReels()
        {
            var slots = SyncCatalogJson.Parse(Catalog().ToJsonString()).SlotMachines!;
            Assert.Equal(2, slots.Paths.Count);
            Assert.Equal("AddedMoney", slots.CreditVariable);
            Assert.Equal("Winnings", slots.WinningsVariable);
            Assert.Equal("Win", slots.LastWinVariable);
            Assert.All(slots.Reels, reel => Assert.Equal(24, reel.Length));
            Assert.Equal(50, slots.Payouts[1]);
            Assert.Equal(7, slots.ButtonPaths.Length);
        }

        [Theory]
        [InlineData("reel1", 0, 0)]
        [InlineData("reel2", 1, 10)]
        [InlineData("reel3", 2, 1.5)]
        [InlineData("payoutMultipliers", 0, -1)]
        public void InvalidSlotRulesAreRejected(string field, int index, double value)
        {
            var json = Catalog();
            json["slotMachines"]![field]![index] = value;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void SlotActionBindingsCannotBeRemovedOrDuplicated()
        {
            var json = Catalog();
            json["slotMachines"]!["buttonPaths"]![1] = json["slotMachines"]!["buttonPaths"]![0]!.DeepClone();
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            json = Catalog();
            json["slotMachines"]!["buttonStates"]!.AsArray().RemoveAt(0);
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }
    }
}
