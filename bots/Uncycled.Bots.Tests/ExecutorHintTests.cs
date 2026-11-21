using System.Linq;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// A policy choice that carries a new spot or zone hint restarts the running action (T4 review I2, T8 review
    /// M4), and GoZoneNear0..2 reach any zone of the map through GoZoneAt (T4 review I1).
    /// </summary>
    public class ExecutorHintTests
    {
        private static ConsoleDef SpotConsole(TestMatch m, int i) => m.Map.ConsolesOfKind(ConsoleKind.Task)[i];

        private static Vec2 Near(TestMatch m, ConsoleDef c) => new MapDistances_Helper(m).WalkableNear(c.Position, 0.8f);

        private static void Steps(SyntheticBot bot, int n)
        {
            for (int i = 0; i < n; i++)
            {
                bot.Step();
            }
        }

        // ---------- hints (T4 review I1, I2; T8 review M4) ----------

        private static MapData FiveZones(out List<ZoneDef> zones)
        {
            // map order is far to near on purpose: the three nearest zones are indices 3, 4, 2
            zones = new List<ZoneDef>
            {
                new ZoneDef("z0", 31f, 1f, 8f, 8f),
                new ZoneDef("z1", 23f, 1f, 8f, 8f),
                new ZoneDef("z2", 15f, 1f, 8f, 8f),
                new ZoneDef("z3", 1f, 1f, 6f, 8f),
                new ZoneDef("z4", 7f, 1f, 8f, 8f),
            };
            return SyntheticBot.TinyMap(40, 10, null, new List<ConsoleDef>(), zones);
        }

        [Fact]
        public void GoZoneNear_RanksEveryZoneOfTheMap_NotOnlyTheFirstThree()
        {
            TestMatch m = TestMatch.Create();
            MapData map = FiveZones(out List<ZoneDef> zones);
            var bot = new SyntheticBot(m, Role.Citizen, new Vec2(3.5f, 5.5f), map) { Seat = 0, MatchTime = 100f };
            SeatView view = bot.View();

            bool[] mask = PolicyActionsV2.Mask(view, bot.Memory, map);
            Assert.True(mask[1] && mask[2] && mask[3]);

            string[] expected = { "z3", "z4", "z2" };
            for (int k = 0; k < 3; k++)
            {
                PolicyChoice choice = PolicyActionsV2.Resolve(1 + k, view, bot.Memory, map);
                Assert.Equal(BotAction.GoZoneAt, choice.Action);
                Assert.Equal(expected[k], choice.Zone);
            }

            Assert.True(ActionCatalogue.FullMask(view, bot.Memory, map)[(int)BotAction.GoZoneAt]);
        }

        [Fact]
        public void GoZoneAt_WalksToTheHintedZone()
        {
            TestMatch m = TestMatch.Create();
            MapData map = FiveZones(out List<ZoneDef> zones);
            var bot = new SyntheticBot(m, Role.Citizen, new Vec2(3.5f, 5.5f), map) { Seat = 0, MatchTime = 100f };
            PolicyChoice choice = PolicyActionsV2.Resolve(2, bot.View(), bot.Memory, map); // second nearest: z4
            Assert.Equal("z4", choice.Zone);

            choice.ApplyTo(bot.Executor);
            bot.Observe();
            bot.Begin(choice.Action);
            bot.RunUntilIdle(30f);

            Assert.True(zones[4].Contains(bot.Position), "the bot ended at " + bot.Position.X + "," + bot.Position.Y);
        }

        [Fact]
        public void Begin_WithANewSpotHint_RestartsTheRunningAction()
        {
            TestMatch m = TestMatch.Create();
            ConsoleDef a = SpotConsole(m, 0);
            ConsoleDef b = SpotConsole(m, 1);
            var bot = new SyntheticBot(m, Role.Citizen, Near(m, a)) { Seat = 0, MatchTime = 100f };
            bot.Spots.Add(new SpotView(a.Id, SpotState.Dirty));
            bot.Spots.Add(new SpotView(b.Id, SpotState.Dirty));
            bot.Observe();
            bot.Executor.SpotHint = a.Id;
            bot.Begin(BotAction.GoDirtySpot);
            Assert.True(bot.Executor.Busy);

            bot.Executor.SpotHint = b.Id; // the network picked CleanSecond while the first walk runs
            bot.Begin(BotAction.GoDirtySpot);
            bot.RunUntilIdle(120f);

            Assert.Contains(bot.Sent, c => c is SpotDoneCommand d && d.SpotId == b.Id);
            Assert.DoesNotContain(bot.Sent, c => c is SpotDoneCommand d && d.SpotId == a.Id);
        }

        [Fact]
        public void Begin_WithTheSameOrNoSpotHint_KeepsTheRunningAction()
        {
            TestMatch m = TestMatch.Create();
            ConsoleDef a = SpotConsole(m, 0);
            ConsoleDef b = SpotConsole(m, 1);
            var bot = new SyntheticBot(m, Role.Citizen, Near(m, b)) { Seat = 0, MatchTime = 100f };
            bot.Spots.Add(new SpotView(a.Id, SpotState.Dirty));
            bot.Spots.Add(new SpotView(b.Id, SpotState.Dirty));
            bot.Observe();
            bot.Executor.SpotHint = a.Id; // walks to a although b is nearer
            bot.Begin(BotAction.GoDirtySpot);

            bot.Executor.SpotHint = a.Id;
            bot.Begin(BotAction.GoDirtySpot);
            bot.Executor.SpotHint = null;
            bot.Begin(BotAction.GoDirtySpot);
            bot.RunUntilIdle(120f);

            Assert.Contains(bot.Sent, c => c is SpotDoneCommand d && d.SpotId == a.Id);
            Assert.DoesNotContain(bot.Sent, c => c is SpotDoneCommand d && d.SpotId == b.Id);
        }

        [Fact]
        public void Begin_WithANewZoneHint_RestartsTheWalk()
        {
            TestMatch m = TestMatch.Create();
            MapData map = FiveZones(out List<ZoneDef> zones);
            var bot = new SyntheticBot(m, Role.Citizen, new Vec2(3.5f, 5.5f), map) { Seat = 0, MatchTime = 100f };
            bot.Observe();
            bot.Executor.ZoneHint = "z0";
            bot.Begin(BotAction.GoZoneAt);
            Steps(bot, 10);

            bot.Executor.ZoneHint = "z4";
            bot.Begin(BotAction.GoZoneAt);
            bot.RunUntilIdle(30f);

            Assert.True(zones[4].Contains(bot.Position), "the bot ended at " + bot.Position.X + "," + bot.Position.Y);
        }

        private sealed class Fixed : IRandom
        {
            public int NextInt(int maxExclusive) => 0;

            public float NextFloat() => 0f;
        }

        private static PacePolicy AlwaysPauses(float seconds)
        {
            var s = new PaceSettings { PauseChance = 1f, PauseMinS = seconds, PauseMaxS = seconds, WrongZoneChance = 0f, GroupChance = 0f, TaskFailChance = 0f };
            return new PacePolicy(s, new Fixed());
        }

        private static int Moves(SyntheticBot bot) => bot.Sent.Count(c => c is MoveCommand);

        [Fact]
        public void GoZoneAt_StartsWithTheHumanPause_AsGoZone0Does()
        {
            TestMatch m = TestMatch.Create();
            MapData map = FiveZones(out List<ZoneDef> zones);
            var bot = new SyntheticBot(m, Role.Citizen, new Vec2(3.5f, 5.5f), map) { Seat = 0, MatchTime = 100f };
            bot.Executor.Pace = AlwaysPauses(2f);
            bot.Observe();
            bot.Executor.ZoneHint = "z0";
            bot.Begin(BotAction.GoZoneAt);

            Steps(bot, 30); // 1.5 s of a 2 s pause
            Assert.Equal(0, Moves(bot));

            Steps(bot, 20);
            Assert.True(Moves(bot) > 0, "the walk starts once the pause is over");
        }

        [Fact]
        public void Begin_WithTheSameHintAsTheRunningActionStartedWith_DoesNotRestart_EvenWhenChooseSpotRefusedIt()
        {
            TestMatch m = TestMatch.Create();
            ConsoleDef a = SpotConsole(m, 0);
            ConsoleDef b = SpotConsole(m, 1);
            var bot = new SyntheticBot(m, Role.Citizen, Near(m, a)) { Seat = 0, MatchTime = 100f };
            bot.Spots.Add(new SpotView(a.Id, SpotState.Clean)); // the hint names a spot that is no longer dirty: the executor walks to b
            bot.Spots.Add(new SpotView(b.Id, SpotState.Dirty));
            bot.Executor.Pace = AlwaysPauses(2f);
            bot.Observe();
            bot.Executor.SpotHint = a.Id;
            bot.Begin(BotAction.GoDirtySpot);
            Steps(bot, 30);

            bot.Executor.SpotHint = a.Id; // the network picks the same index again
            bot.Begin(BotAction.GoDirtySpot);
            Steps(bot, 15);

            Assert.True(Moves(bot) > 0, "a restart would have drawn a fresh 2 s pause");
        }
    }
}
