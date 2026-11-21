using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// The executor commits a machine fix hold and a Trasher wait at a watched spot (the network is not asked
    /// meanwhile) and stops a committed bot on a gas warning for its room or its own infection.
    /// </summary>
    public class ExecutorV2CommitTests
    {
        private static ConsoleDef Machine(TestMatch m) => m.Map.ConsolesOfKind(ConsoleKind.Machine)[0];

        private static ConsoleDef SpotConsole(TestMatch m, int i) => m.Map.ConsolesOfKind(ConsoleKind.Task)[i];

        private static Vec2 Near(TestMatch m, ConsoleDef c) => new MapDistances_Helper(m).WalkableNear(c.Position, 0.8f);

        /// <summary>A Cleaner at the jammed machine that has started to hold the fix.</summary>
        private static SyntheticBot Holding(TestMatch m)
        {
            var bot = new SyntheticBot(m, Role.Citizen, Near(m, Machine(m))) { Seat = 0, MatchTime = 100f, MachineJammed = true };
            bot.Observe();
            bot.Begin(BotAction.FixMachine);
            Steps(bot, 4);

            Assert.Contains(bot.Sent, c => c is FixCommand f && f.Holding);
            return bot;
        }

        private static void Steps(SyntheticBot bot, int n)
        {
            for (int i = 0; i < n; i++)
            {
                bot.Step();
            }
        }

        // ---------- the machine hold ----------

        [Fact]
        public void FixMachine_IsCommittedWhileHolding()
        {
            SyntheticBot bot = Holding(TestMatch.Create());

            Assert.True(bot.Executor.Committed);
            bot.Begin(BotAction.Stay);
            Assert.Equal(BotAction.FixMachine, bot.Executor.Current);
            Assert.True(bot.Executor.Busy, "a committed hold ignores other choices");
        }

        [Fact]
        public void FixMachine_CommitEndsWhenUnjammed()
        {
            SyntheticBot bot = Holding(TestMatch.Create());
            Assert.True(bot.Executor.Committed);

            bot.MachineJammed = false;
            bot.Step();

            Assert.False(bot.Executor.Committed);
            Assert.False(bot.Executor.Busy);
        }

        [Fact]
        public void FixMachine_CommitEndsAfterMaxHold()
        {
            TestMatch m = TestMatch.Create();
            m.Config.MachineFixS = 30f; // the jam never clears and the hold itself is long: only MaxFixHoldS ends the commitment
            SyntheticBot bot = Holding(m);
            Assert.Equal(8f, bot.Executor.MaxFixHoldS);

            Steps(bot, (int)(6f / SyntheticBot.Dt));
            Assert.True(bot.Executor.Committed, "still within the max hold");

            Steps(bot, (int)(3f / SyntheticBot.Dt));
            Assert.False(bot.Executor.Committed, "9 s of a jammed machine: the network is asked again");
            Assert.True(bot.Executor.Busy, "the hold itself goes on");
        }

        // ---------- the watched wait ----------

        [Theory]
        [InlineData(BotAction.Repollute, SpotState.Clean)]
        [InlineData(BotAction.PlantVirus, SpotState.Dirty)]
        public void Repollute_IsCommittedWhileWaitingForWatchers(BotAction action, SpotState state)
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Polluter, Near(m, SpotConsole(m, 0))) { Seat = 0, MatchTime = 100f };
            bot.Spots.Add(new SpotView(SpotConsole(m, 0).Id, state));
            bot.Players.Add(SyntheticBot.Player(3, bot.Position.X + 0.3f, bot.Position.Y));
            bot.Observe();

            bot.Begin(action);
            Steps(bot, 3);

            Assert.True(bot.Executor.Committed, "waiting while watched is a commitment");
            Assert.DoesNotContain(bot.Sent, c => c is RepolluteCommand || c is PlantVirusCommand);
            bot.Begin(BotAction.Stay);
            Assert.Equal(action, bot.Executor.Current);

            bot.RunUntilIdle(10f);
            Assert.Contains(bot.Sent, c => c is RepolluteCommand || c is PlantVirusCommand);
            Assert.False(bot.Executor.Committed);
        }

        // ---------- stop triggers ----------

        [Fact]
        public void CommittedStep_StopsOnGasWarningForMyRoom_AndReleasesTheHold()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Holding(m);
            bot.Position = new Vec2(4.5f, 4.5f); // inside gas_market (the test moves the bot: only the view matters to the trigger)
            bot.Gas = new GasView("gas_market", 6f);

            bot.Step();
            bot.Step();

            Assert.False(bot.Executor.Committed);
            Assert.False(bot.Executor.Busy);
            Assert.Contains(bot.Sent, c => c is FixCommand f && !f.Holding);
        }

        [Fact]
        public void CommittedStep_IgnoresGasWarningForAnotherRoom()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Holding(m);
            Assert.False(m.Map.GasRooms.First(z => z.Id == "gas_depot").Contains(bot.Position));
            bot.Gas = new GasView("gas_depot", 6f);

            Steps(bot, 5);

            Assert.True(bot.Executor.Committed);
        }

        [Fact]
        public void CommittedStep_StopsWhenInfected()
        {
            SyntheticBot bot = Holding(TestMatch.Create());
            bot.InfectedS = 30f;

            bot.Step();

            Assert.False(bot.Executor.Committed);
            Assert.False(bot.Executor.Busy);
        }

        [Fact]
        public void CommittedStep_KeepsGoingWhenAlreadyInfectedBeforeTheHold()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Citizen, Near(m, Machine(m))) { Seat = 0, MatchTime = 100f, MachineJammed = true, InfectedS = 30f };
            bot.Observe();
            bot.Begin(BotAction.FixMachine);

            Steps(bot, 8);

            Assert.True(bot.Executor.Committed, "only the start of an infection stops a step");
        }

        [Fact]
        public void CommittedStep_KeepsGoing_ForATrasherInAWarnedGasRoom()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Polluter, Near(m, Machine(m))) { Seat = 0, MatchTime = 100f, MachineJammed = true };
            bot.Observe();
            bot.Begin(BotAction.FixMachine);
            Steps(bot, 4);
            Assert.True(bot.Executor.Committed);

            bot.Position = new Vec2(4.5f, 4.5f); // inside gas_market
            bot.Gas = new GasView("gas_market", 6f);
            Steps(bot, 5);

            Assert.True(bot.Executor.Committed, "the gas warning is for Cleaners");
        }

        [Fact]
        public void CommittedStep_KeepsGoing_ForACleanerWithAMaskInAWarnedGasRoom()
        {
            SyntheticBot bot = Holding(TestMatch.Create());
            bot.Masks = 1;
            bot.Position = new Vec2(4.5f, 4.5f);
            bot.Gas = new GasView("gas_market", 6f);

            Steps(bot, 5);

            Assert.True(bot.Executor.Committed, "the mask takes the gas");
        }

        // ---------- a meeting ends every commitment ----------

        [Fact]
        public void AMeetingStartingDuringTheMachineHold_ClearsTheCommitment()
        {
            SyntheticBot bot = Holding(TestMatch.Create());
            Assert.True(bot.Executor.Committed);

            bot.Phase = MatchPhase.Meeting;
            bot.Step();
            bot.Phase = MatchPhase.Playing;

            Assert.False(bot.Executor.Committed);
            Assert.False(bot.Executor.Busy);
        }

        [Fact]
        public void AMeetingStartingDuringTheWatchedWait_ClearsTheCommitment()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Polluter, Near(m, SpotConsole(m, 0))) { Seat = 0, MatchTime = 100f };
            bot.Spots.Add(new SpotView(SpotConsole(m, 0).Id, SpotState.Clean));
            bot.Players.Add(SyntheticBot.Player(3, bot.Position.X + 0.3f, bot.Position.Y));
            bot.Observe();
            bot.Begin(BotAction.Repollute);
            Steps(bot, 3);
            Assert.True(bot.Executor.Committed);

            bot.Phase = MatchPhase.Meeting;
            bot.Step();
            bot.Phase = MatchPhase.Playing;

            Assert.False(bot.Executor.Committed);
            Assert.False(bot.Executor.Busy);
        }
    }
}
