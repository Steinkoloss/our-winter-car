using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class CatalogBindingTests
    {
        private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!.AsObject();

        [Fact]
        public void LottoTicketsHaveDedicatedNativeBindingsAndNoGenericPaymentReplay()
        {
            var json = Catalog();
            var data = SyncCatalogJson.Parse(json.ToJsonString()).LottoTickets!;
            Assert.Equal(3, data.LinePrice); Assert.Equal(1000, data.BankThreshold);
            Assert.Equal("Sheets/LottoTicket/Pay", data["payPath"]);
            Assert.Equal("Check money", data["requestState"]); Assert.Equal("Date 2", data["commitState"]);
            Assert.Equal("ObjectNumberInt", data["counter"]); Assert.Equal("SaveID", data["saveId"]);
            Assert.Equal("lottery ticket(lotte)", data["ticketName"]);
            Assert.Equal("State 4", data["claimState"]); Assert.Equal("State 16", data["dataIdle"]);
            Assert.Equal("PERAPORTTI/Building/LOD100/Store/VoittousArea/TrashTrigger", data["claimPath"]);
            Assert.DoesNotContain(json["buys"]!.AsArray(), r => r!["pathContains"]?.GetValue<string>() == "LottoTicket/Pay");
            foreach (string key in LottoTicketsData.RequiredBindings)
            {
                var broken = Catalog(); broken["lottoTickets"]!.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(broken.ToJsonString()));
            }
            foreach (string key in new[] { "linePrice", "bankThreshold" })
            {
                var broken = Catalog(); broken["lottoTickets"]![key] = 0;
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(broken.ToJsonString()));
            }
        }

        [Fact]
        public void LottoCatalogBindsActualDrawListsAndCompletedNativeStates()
        {
            var data = SyncCatalogJson.Parse(Catalog().ToJsonString()).LottoDraw!;
            Assert.Equal("Systems/Lottery", data.Path); Assert.Equal("Numbers", data.Fsm);
            Assert.Equal("DrawDone", data.DrawDone);
            Assert.Equal("Systems/TV/Teletext/VKTekstiTV/PAGES/301/Texts", data.ResultsPath);
            Assert.Equal(new[] { "CurrentRound", "TicketRound", "NationalPot", "NationalPotMin", "NationalPotFull" }, data.Scalars);
            Assert.Equal(new[] { "NationalLine7", "NationalLine6+1", "NationalLine6", "NationalLine5", "NationalLine4" }, data.WinnerVariables);
            Assert.Equal(new[] { "Results", "ResultsBonus", "ResultsLinesWinnings", "ResultsLinesWon" }, data.Lists);
            Assert.Equal(new[] { "Reset points", "Day", "Time", "State 35" }, data.StableStates);
            Assert.DoesNotContain("State 2", data.StableStates);
            Assert.DoesNotContain("UTNational7", data.Scalars);
        }

        [Fact]
        public void LottoCatalogRejectsPartialAndAmbiguousBindings()
        {
            foreach (string key in new[] { "path", "fsm", "drawDone", "resultsPath", "scalars", "winnerVariables", "lists", "stableStates" })
            {
                var json = Catalog(); json["lottoDraw"]!.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            }
            foreach (string key in new[] { "scalars", "winnerVariables", "lists", "stableStates" })
            {
                var json = Catalog(); var slots = json["lottoDraw"]![key]!.AsArray();
                slots[1] = slots[0]!.DeepClone();
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
                json = Catalog(); json["lottoDraw"]![key]!.AsArray().RemoveAt(0);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            }
            foreach (string key in new[] { "path", "resultsPath" })
            {
                var json = Catalog(); json["lottoDraw"]![key] = "../Lottery";
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            }
            var overlap = Catalog(); overlap["lottoDraw"]!["winnerVariables"]![0] = "NationalPot";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(overlap.ToJsonString()));
            overlap = Catalog(); overlap["lottoDraw"]!["drawDone"] = "CurrentRound";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(overlap.ToJsonString()));
            overlap = Catalog(); overlap["lottoDraw"] = 5;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(overlap.ToJsonString()));
        }

        [Fact]
        public void RallyBindingsObserveDurableCompletionAndPreserveStageOrder()
        {
            var json = Catalog();
            var data = SyncCatalogJson.Parse(json.ToJsonString()).RallyProgress!;
            Assert.Equal("Timing", data.TimingFsm); Assert.Equal("Start", data.StartedVariable);
            Assert.Equal("Checkpoint", data.MarkerFsm);
            Assert.Equal("Set bool", data.CommitState); Assert.Equal("Idle", data.CompletedState);
            Assert.Equal(new[] { 4, 6, 4 }, data.Stages.Select(s => s.Checkpoints.Length));
            for (int stage = 1; stage <= 3; stage++)
            {
                Assert.Equal("RACES/RALLY/SS" + stage + "/TimingSS" + stage, data.Stages[stage - 1].TimingPath);
                Assert.Equal(Enumerable.Range(1, data.Stages[stage - 1].Checkpoints.Length).Select(i => "Checkpoint0" + i),
                    data.Stages[stage - 1].Checkpoints);
            }
            json["rallyProgress"]!["completedState"] = "Updated finished state";
            Assert.Equal("Updated finished state", SyncCatalogJson.Parse(json.ToJsonString()).RallyProgress!.CompletedState);
        }

        [Fact]
        public void RallyBindingsRejectMissingStatesDuplicatePathsAndInvalidCheckpointCounts()
        {
            foreach (string key in new[] { "timingFsm", "startedVariable", "markerFsm", "commitState", "completedState" })
            {
                var json = Catalog(); json["rallyProgress"]!.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            }
            var bad = Catalog(); bad["rallyProgress"]!["completedState"] = "Set bool";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(bad.ToJsonString()));
            bad = Catalog(); bad["rallyProgress"]!["stages"]!.AsArray().RemoveAt(0);
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(bad.ToJsonString()));
            bad = Catalog(); bad["rallyProgress"]!["stages"]![1]!["timingPath"] = bad["rallyProgress"]!["stages"]![0]!["timingPath"]!.DeepClone();
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(bad.ToJsonString()));
            bad = Catalog(); bad["rallyProgress"]!["stages"]![0]!["checkpoints"]![1] = "Checkpoint01";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(bad.ToJsonString()));
            bad = Catalog(); bad["rallyProgress"]!["stages"]![1]!["checkpoints"]!.AsArray().Add("Checkpoint07");
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(bad.ToJsonString()));
            bad = Catalog(); bad["rallyProgress"]!["stages"]![0]!["checkpoints"]!.AsArray().Clear();
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(bad.ToJsonString()));
        }

        [Fact]
        public void VenttiReactionLayoutKeepsCompleteParentFirstPosesAndNativeAudioVariations()
        {
            var data = SyncCatalogJson.Parse(Catalog().ToJsonString()).VenttiTable!.Reactions!;
            Assert.Equal(new[] { "PIG/VenttiPig", "Table", "Chair" }, data.Poses.Take(3));
            Assert.Equal(46, data.Poses.Length);
            Assert.Equal(21, data.Sounds.Length);
            Assert.Equal(0xeb404f24u, data.LayoutId);
            Assert.Equal(6, data.Sources.Count);
            Assert.Equal(new[] { "Win", "Lose", "Anim", "Delay", "Anim 3", "State 1" }, data.Sources.Select(s => s.State));
            Assert.Equal(new[] { 0f, 0f, 0f, 3f, 0f, 0f }, data.Sources.Select(s => s.Delay));
            Assert.Contains("MasterAudio/Pig/pig1", data.Sounds);
            Assert.DoesNotContain("MasterAudio/Pig/pig01", data.Sounds);
            Assert.Contains("MasterAudio/HouseFoley/cards_gather", data.Sounds);
            var state = new VenttiSceneState
            {
                Poses = data.Poses.Select(_ => new VenttiPose { Rotation = NetQuaternion.Identity }).ToArray(),
            };
            Assert.Equal(981, PacketCodec.Encode(state).Length);
        }

        [Fact]
        public void ChangedReactionSlotsChangeTheLayoutBeforeTheyCanBeApplied()
        {
            var json = Catalog();
            var data = SyncCatalogJson.Parse(json.ToJsonString()).VenttiTable!.Reactions!;
            json["venttiTable"]!["reactions"]!["sounds"]![0] = "MasterAudio/Pig/updated_lose";
            var updated = SyncCatalogJson.Parse(json.ToJsonString()).VenttiTable!.Reactions!;
            Assert.NotEqual(data.LayoutId, updated.LayoutId);
            Assert.NotEqual(data.LayoutId, VenttiSceneReplica.Layout(data.RootPath, Enumerable.Reverse(data.Poses).ToArray(), data.Sounds));
            var replica = new VenttiSceneReplica();
            var state = new VenttiSceneState { TableId = 1, LayoutId = data.LayoutId,
                Poses = data.Poses.Select(_ => new VenttiPose { Rotation = NetQuaternion.Identity }).ToArray() };
            Assert.False(replica.Receive(1, updated.LayoutId, updated.Poses.Length, state, 0));
            Assert.Null(replica.Current);
        }

        [Theory]
        [InlineData("missing-parent")]
        [InlineData("duplicate-pose")]
        [InlineData("overlapping-roots")]
        [InlineData("wrong-table")]
        [InlineData("oversized")]
        [InlineData("unsafe-root")]
        [InlineData("unsafe-pose")]
        [InlineData("duplicate-sound")]
        [InlineData("unknown-group")]
        [InlineData("unknown-variation")]
        [InlineData("both-variations")]
        [InlineData("missing-variation")]
        [InlineData("duplicate-hook")]
        [InlineData("invalid-delay")]
        [InlineData("invalid-index")]
        public void AmbiguousOrUnsafeVenttiReactionsFailAtCatalogLoad(string fault)
        {
            var json = Catalog();
            var data = json["venttiTable"]!["reactions"]!;
            var poses = data["poses"]!.AsArray();
            var sounds = data["sounds"]!.AsArray();
            var hooks = data["sources"]!.AsArray();
            switch (fault)
            {
                case "missing-parent": poses.Remove(poses.First(p => p!.GetValue<string>() == "PIG/VenttiPig/Pivot")); break;
                case "duplicate-pose": poses[2] = poses[1]!.DeepClone(); break;
                case "overlapping-roots": poses[2] = "Table/Chair"; break;
                case "wrong-table": poses[1] = "DifferentTable"; break;
                case "oversized": while (poses.Count <= VenttiSceneState.MaxPoses) poses.Add("Extra" + poses.Count); break;
                case "unsafe-root": data["rootPath"] = "../RoomVenttiPig"; break;
                case "unsafe-pose": poses[2] = "Chair\nAnotherChair"; break;
                case "duplicate-sound": sounds[1] = sounds[0]!.DeepClone(); break;
                case "unknown-group": hooks[0]!["group"] = "Unknown"; break;
                case "unknown-variation": hooks[2]!["variation"] = "Unknown"; break;
                case "both-variations": hooks[0]!["variation"] = "venttipig_win01"; break;
                case "missing-variation": hooks[0]!["variable"] = ""; break;
                case "duplicate-hook": hooks.Add(hooks[0]!.DeepClone()); break;
                case "invalid-delay": hooks[0]!["delay"] = 5.1; break;
                case "invalid-index": hooks[0]!["actionIndex"] = -1; break;
            }
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void VenttiPresentationAndAccountingCutsKeepNativePropertyActions()
        {
            var data = SyncCatalogJson.Parse(Catalog().ToJsonString()).VenttiTable!;
            Assert.Equal(1, data.MaterialIndex);
            Assert.Equal(9, data.CardSlots);
            Assert.Equal("_MainTex", data["textureProperty"]);
            Assert.Equal(new[] { "Check hand", "State 4" }, data.IdleStates);
            Assert.Equal(new[] { 6, 4, 3, 4, 3, 4 }, data.Outcomes.ConvertAll(o => o.SuppressActions.Length));
            Assert.Equal(new[] { "Win", "Lose", "Win car", "Lose car", "Win house", "Lose house" }, data.Outcomes.ConvertAll(o => o.State));
            foreach (var outcome in data.Outcomes)
                foreach (int cut in outcome.SuppressActions)
                {
                    Assert.NotEqual("SetIntValue", outcome.ActionTypes[cut]);
                    Assert.NotEqual("ActivateGameObject", outcome.ActionTypes[cut]);
                    Assert.NotEqual("EnableFSM", outcome.ActionTypes[cut]);
                }
        }

        [Theory]
        [InlineData("materialIndex")]
        [InlineData("cardSlots")]
        [InlineData("pickDistance")]
        [InlineData("winStress")]
        [InlineData("loseStress")]
        public void MissingOrInvalidVenttiPresentationSettingsFailDuringCatalogLoad(string key)
        {
            var json = Catalog();
            json["venttiTable"]!.AsObject().Remove(key);
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            json = Catalog();
            json["venttiTable"]![key] = -1;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void OutcomeCutsCannotBeMissingDuplicatedOrPointOutsideTheirActionList()
        {
            var json = Catalog();
            json["venttiTable"]!["outcomes"]![0]!["suppressActions"]![0] = 999;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            json = Catalog();
            json["venttiTable"]!["outcomes"]![0]!["suppressActions"]![0] = 3;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            json = Catalog();
            json["venttiTable"]!["outcomes"]!.AsArray().RemoveAt(0);
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            json = Catalog();
            json["venttiTable"]!["outcomes"]![0]!["state"] = "Lose";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void VenttiRulesKeepTheNativeCardSlotsAndBettingThresholds()
        {
            var rules = SyncCatalogJson.Parse(Catalog().ToJsonString()).VenttiTable!.Rules!;
            for (byte card = 1; card <= 52; card++) Assert.Equal((card - 1) / 4 + 1, rules.CardValue(card));
            Assert.Equal(50, rules.BetIncrement);
            Assert.Equal(4000, rules.PropertyThreshold);
            Assert.Equal(50, rules.WinLimitIncrease);
            Assert.Equal(7000, rules.OpponentLossLimit);
            Assert.Equal(6000, rules.PropertyWinLoss);
            Assert.Equal(0, rules.PropertyLoseLoss);
        }

        [Theory]
        [InlineData("betIncrement")]
        [InlineData("propertyThreshold")]
        [InlineData("winLimitIncrease")]
        [InlineData("opponentLossLimit")]
        [InlineData("propertyWinLoss")]
        [InlineData("propertyLoseLoss")]
        public void MissingOrInvalidVenttiNumericRulesAreRejected(string field)
        {
            var json = Catalog();
            json["venttiTable"]!["rules"]!.AsObject().Remove(field);
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            json = Catalog();
            json["venttiTable"]!["rules"]![field] = -1;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            json["venttiTable"]!["rules"]![field] = "50";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void VenttiCardSlotsCannotBeRemovedOrAssignedImpossibleValues()
        {
            var json = Catalog();
            json["venttiTable"]!["rules"]!["cardValues"]!.AsArray().RemoveAt(0);
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            foreach (var pair in new[] { new[] { 0, 1 }, new[] { 1, 0 }, new[] { 52, 14 } })
            {
                json = Catalog();
                json["venttiTable"]!["rules"]!["cardValues"]![pair[0]] = pair[1];
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            }
        }

        [Fact]
        public void VenttiTableBindingsPreserveNativeResultMeaningAndCanFollowGameUpdates()
        {
            var json = Catalog();
            var data = SyncCatalogJson.Parse(json.ToJsonString()).VenttiTable!;
            Assert.Equal("Bet", data["stake"]);
            Assert.Equal("LoseText", data["resultFsm"]);
            Assert.Equal("Status", data["result"]);
            Assert.Equal(new[] { "YOU WIN", "YOU LOSE", "YOU WON A CAR", "YOU LOST THE SATSUMA", "YOU WON THIS CABIN", "YOU LOST YOUR HOME" },
                Array.ConvertAll(VenttiTableData.OutcomeBindings, key => data[key]));
            json["venttiTable"]!["stake"] = "UpdatedStake";
            json["venttiTable"]!["winCarText"] = "Updated car result";
            data = SyncCatalogJson.Parse(json.ToJsonString()).VenttiTable!;
            Assert.Equal("UpdatedStake", data["stake"]);
            Assert.Equal("Updated car result", data["winCarText"]);
        }

        [Fact]
        public void MissingOrAmbiguousVenttiTableBindingsAreRejected()
        {
            foreach (string key in VenttiTableData.RequiredBindings)
            {
                var json = Catalog();
                json["venttiTable"]!.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            }
            var duplicate = Catalog();
            duplicate["venttiTable"]!["winHouseText"] = "YOU LOST YOUR HOME";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(duplicate.ToJsonString()));
            duplicate = Catalog();
            duplicate["venttiTable"]!["housePath"] = duplicate["venttiTable"]!["playerPath"]!.DeepClone();
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(duplicate.ToJsonString()));
            duplicate = Catalog();
            duplicate["venttiTable"]!["managerPath"] = "AnotherResolver";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(duplicate.ToJsonString()));
        }

        [Fact]
        public void VenttiPropertyBindingsKeepTheNativeKeyAndAccessOrder()
        {
            var json = Catalog();
            var data = SyncCatalogJson.Parse(json.ToJsonString()).VenttiProperty!;
            Assert.Equal("PlayerKeyRuscko", data["rusckoKey"]);
            Assert.Equal("PlayerKeySatsuma", data["satsumaKey"]);
            Assert.Equal("PlayerKeyHome", data["homeKey"]);
            Assert.Equal("CABIN/LOD/Sleep", data["sleepPath"]);
            json["venttiProperty"]!["loggingPath"] = "CABIN/UpdatedLogging";
            data = SyncCatalogJson.Parse(json.ToJsonString()).VenttiProperty!;
            Assert.Equal("CABIN/UpdatedLogging", data["loggingPath"]);
        }

        [Fact]
        public void IncompleteOrAliasedVenttiBindingsAreRejected()
        {
            foreach (string key in VenttiPropertyData.RequiredBindings)
            {
                var json = Catalog();
                json["venttiProperty"]!.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            }
            var duplicateKey = Catalog();
            duplicateKey["venttiProperty"]!["satsumaKey"] = "PlayerKeyHome";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(duplicateKey.ToJsonString()));
            var duplicateAccess = Catalog();
            duplicateAccess["venttiProperty"]!["hatchPath"] = "CABIN/LOD/Sleep";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(duplicateAccess.ToJsonString()));
        }

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
        public void HockeyBindingsCarryLiveOddsResultsAndStandingsInsteadOfSaveTags()
        {
            var json = Catalog();
            var data = SyncCatalogJson.Parse(json.ToJsonString()).HockeyBetting!;
            Assert.Equal("Systems/HockeyGames/Betting", data["bettingPath"]);
            Assert.Equal(new[] { "0", "1", "2", "3", "4", "5" }, data.Tables);
            Assert.Equal(new[] { "1", "X", "2" }, data.Keys);
            Assert.Equal(new[] { "PairsNew", "Pairs", "ResultsGame", "ResultsOdds", "ResultsPair",
                "Order", "GamesString", "GoalsString", "PointsString" }, data.Lists);
            Assert.Equal(new[] { "Day", "Time", "Reset points" }, data.SeasonIdle);
            json["hockeyBetting"]!["tables"]![5] = "UpdatedLastMatch";
            json["hockeyBetting"]!["seasonPath"] = "UpdatedTown/Season";
            data = SyncCatalogJson.Parse(json.ToJsonString()).HockeyBetting!;
            Assert.Equal("UpdatedLastMatch", data.Tables[5]);
            Assert.Equal("UpdatedTown/Season", data["seasonPath"]);
        }

        [Fact]
        public void EveryHockeyBindingIsRequiredAndCollectionSlotsCannotAlias()
        {
            foreach (string key in HockeyBettingData.RequiredBindings.Concat(new[] { "ints", "floats", "lists", "tables",
                "keys", "seasonIdle", "bettingIdle", "displays" }))
            {
                var json = Catalog(); json["hockeyBetting"]!.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            }
            foreach (string key in new[] { "ints", "floats", "lists", "tables", "keys", "seasonIdle", "bettingIdle", "displays" })
            {
                var json = Catalog(); json["hockeyBetting"]![key]![1] = json["hockeyBetting"]![key]![0]!.DeepClone();
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            }
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
