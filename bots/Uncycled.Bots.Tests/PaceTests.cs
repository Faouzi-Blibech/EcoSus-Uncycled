using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.World;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>Human-paced play lives in the action executor, so every brain gets it.</summary>
    public class PaceTests
    {
        private sealed class Fixed : IRandom
        {
            private readonly float _value;

            public Fixed(float value) { _value = value; }

            public int NextInt(int maxExclusive) => 0;

            public float NextFloat() => _value;
        }

        private static PacePolicy Policy(float roll, Action<PaceSettings>? tweak = null)
        {
            var s = new PaceSettings { PauseChance = 0f, WrongZoneChance = 0f, GroupChance = 0f, TaskFailChance = 0f };
            tweak?.Invoke(s);
            return new PacePolicy(s, new Fixed(roll));
        }

        [Theory]
        [InlineData(0f, 1.5f)]
        [InlineData(0.999f, 2.5f)]
        public void TaskStep_TakesMinSecondsTimesTheFactor(float roll, float factor)
        {
            TestMatch m = TestMatch.Create();
            byte seat = m.FirstSeatOf(Role.Citizen);
            var driver = new BotDriver(m, seat);
            driver.Executor.Pace = Policy(roll);

            driver.Begin(BotAction.GoNextTask);
            driver.RunUntilIdle(80f);
            SeatView here = driver.View;
            TaskInfo task = here.Me.Tasks.First(t => t.StepsDone < t.Steps
                && Vec2.Distance(here.Me.Position, m.Map.FindConsole(t.ConsoleId)!.Position) <= m.Map.FindConsole(t.ConsoleId)!.Range);
            float minS = m.Engine.Tasks.Find(task.Id)!.Type.MinSeconds;

            driver.Begin(BotAction.UseHere);
            float used = driver.RunUntilIdle(minS * 3f + 5f);

            Assert.Contains(m.Events, e => e is TaskProgressEvent p && p.Seat == seat);
            Assert.True(used >= minS * factor - 0.2f, "took " + used + " s, wanted at least " + minS * factor);
            Assert.True(used <= minS * factor + 1f, "took " + used + " s, wanted about " + minS * factor);
            Assert.All(driver.Sent, kv => Assert.True(kv.Value.IsOk, kv.Key.GetType().Name + " " + kv.Value));
        }

        [Fact]
        public void TaskFactor_IsDrawnOncePerTask_AndRepeatsForTheSameSeed()
        {
            var a = new PacePolicy(new PaceSettings(), new XorShiftRandom(5));
            var b = new PacePolicy(new PaceSettings(), new XorShiftRandom(5));
            float t1 = a.TaskTimeFactor(11);
            Assert.Equal(t1, a.TaskTimeFactor(11));
            Assert.InRange(t1, 1.5f, 2.5f);
            Assert.Equal(t1, b.TaskTimeFactor(11));
            Assert.Equal(a.TaskTimeFactor(12), b.TaskTimeFactor(12));
        }

        [Fact]
        public void Settings_ReadTheNestedPaceBlock()
        {
            string path = Path.Combine(Path.GetTempPath(), "pace-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{ \"skill\": 0.6, \"pace\": { \"task_time_factor_min\": 1.2, \"task_time_factor_max\": 3, \"pause_chance\": 0.5, \"pause_min_s\": 1, \"pause_max_s\": 4, \"wrong_zone_chance\": 0.2, \"group_chance\": 0.4 } }");
            try
            {
                PaceSettings p = BotsSettings.Load(path).Pace;
                Assert.Equal(1.2f, p.TaskTimeFactorMin);
                Assert.Equal(3f, p.TaskTimeFactorMax);
                Assert.Equal(0.5f, p.PauseChance);
                Assert.Equal(1f, p.PauseMinS);
                Assert.Equal(4f, p.PauseMaxS);
                Assert.Equal(0.2f, p.WrongZoneChance);
                Assert.Equal(0.4f, p.GroupChance);
            }
            finally
            {
                File.Delete(path);
            }

            PaceSettings d = new BotsSettings().Pace;
            Assert.Equal(1.5f, d.TaskTimeFactorMin);
            Assert.Equal(2.5f, d.TaskTimeFactorMax);
            Assert.Equal(0.3f, d.PauseChance);
            Assert.Equal(0.1f, d.WrongZoneChance);
            Assert.Equal(0.3f, d.GroupChance);
        }

        private static SyntheticBot CitizenWithTasks(TestMatch m, out List<ConsoleDef> consoles)
        {
            var bot = new SyntheticBot(m);
            consoles = m.Map.Consoles.Where(c => c.Kind == ConsoleKind.Task).ToList();
            int id = 1;
            foreach (ConsoleDef c in consoles)
            {
                bot.Tasks.Add(new TaskInfo(id++, "t", 1, 0, c.Id));
            }

            return bot;
        }

        [Fact]
        public void Pause_HoldsBackTheFirstMove()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot paused = CitizenWithTasks(m, out _);
            paused.Executor.Pace = Policy(0f, s => { s.PauseChance = 1f; s.PauseMinS = 1f; s.PauseMaxS = 1f; });
            paused.Begin(BotAction.GoNextTask);
            for (int i = 0; i < 18; i++)
            {
                paused.Step(); // 0.9 s
            }

            Assert.DoesNotContain(paused.Sent, c => c is MoveCommand);
            for (int i = 0; i < 20; i++)
            {
                paused.Step();
            }

            Assert.Contains(paused.Sent, c => c is MoveCommand);

            SyntheticBot eager = CitizenWithTasks(m, out _);
            eager.Executor.Pace = Policy(0f);
            eager.Begin(BotAction.GoNextTask);
            eager.Step();
            Assert.Contains(eager.Sent, c => c is MoveCommand);
        }

        [Fact]
        public void NoPause_BeforeReportOrClean()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = CitizenWithTasks(m, out _);
            bot.Executor.Pace = Policy(0f, s => { s.PauseChance = 1f; s.PauseMinS = 2f; s.PauseMaxS = 2f; });
            PileView pile = new PileView(1, new Vec2(bot.Position.X + 2f, bot.Position.Y), PileKind.Dump, PileState.Fresh);
            bot.Piles.Add(pile);
            bot.Begin(BotAction.ReportNearest);
            bot.Step();
            Assert.Contains(bot.Sent, c => c is MoveCommand || c is ReportCommand);
        }

        [Fact]
        public void WrongZone_ThenTheRightTask()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = CitizenWithTasks(m, out List<ConsoleDef> consoles);
            bot.Executor.Pace = Policy(0f, s => s.WrongZoneChance = 1f);
            bot.Begin(BotAction.GoNextTask);

            string startZone = m.Map.ZoneAt(bot.Position);

            var zonesVisited = new HashSet<string>();
            for (int i = 0; i < 4000 && bot.Executor.Busy; i++)
            {
                bot.Step();
                zonesVisited.Add(m.Map.ZoneAt(bot.Position));
            }

            Assert.False(bot.Executor.Busy);
            ConsoleDef reached = consoles.OrderBy(c => Vec2.Distance(c.Position, bot.Position)).First();
            Assert.True(Vec2.Distance(bot.Position, reached.Position) <= reached.Range + 0.6f, "ended " + Vec2.Distance(bot.Position, reached.Position) + " from the nearest task console");
            string rightZone = m.Map.ZoneAt(reached.Position);
            Assert.Contains(zonesVisited, z => z != rightZone && z != startZone);
        }

        [Fact]
        public void Group_PrefersATaskInAZoneWhereAPlayerIsInSight()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = CitizenWithTasks(m, out List<ConsoleDef> consoles);
            ConsoleDef nearest = consoles.OrderBy(c => Vec2.Distance(c.Position, bot.Position)).First();
            ConsoleDef other = consoles.Where(c => m.Map.ZoneAt(c.Position) != m.Map.ZoneAt(nearest.Position))
                .OrderBy(c => Vec2.Distance(c.Position, bot.Position)).First();
            bot.Players.Add(SyntheticBot.Player(2, other.Position.X + 0.5f, other.Position.Y));
            bot.Executor.Pace = Policy(0f, s => s.GroupChance = 1f);

            bot.Begin(BotAction.GoNextTask);
            Assert.True(bot.Executor.Busy);
            Assert.True(Vec2.Distance(bot.Nav.Goal, other.Position) < 1.5f, "goal " + bot.Nav.Goal + " should be the console where the player is");
        }

        [Fact]
        public void Group_FollowsASeenPlayerWhenNoTaskIsThere()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m);
            ConsoleDef only = m.Map.Consoles.First(c => c.Kind == ConsoleKind.Task);
            bot.Tasks.Add(new TaskInfo(1, "t", 1, 0, only.Id));
            // a player far from that console's zone
            ConsoleDef elsewhere = m.Map.Consoles.First(c => c.Kind == ConsoleKind.Task && m.Map.ZoneAt(c.Position) != m.Map.ZoneAt(only.Position));
            bot.Position = new Vec2(elsewhere.Position.X - 1f, elsewhere.Position.Y);
            bot.Players.Add(SyntheticBot.Player(2, bot.Position.X + 2f, bot.Position.Y));
            bot.Executor.Pace = Policy(0f, s => s.GroupChance = 1f);

            bot.Begin(BotAction.GoNextTask);
            Assert.Equal(BotAction.FollowSlot0, bot.Executor.Current);
        }

        private static double MeanMatchTime(BotsSettings settings)
        {
            string dir = Uncycled.Server.Content.ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
            Uncycled.Server.Content.ContentBundle content = Uncycled.Server.Content.ContentLoader.Load(dir);
            var options = Uncycled.Train.HostOptions.Parse(new[] { "--eval", "--content", dir, "--seeds", "1-8", "--threads", "1" });
            IReadOnlyList<string> lines = Uncycled.Train.EvalRunner.Run(options, content, Uncycled.Train.HostBrains.Scripted(settings, content));
            double sum = 0;
            foreach (string l in lines)
            {
                var m = System.Text.RegularExpressions.Regex.Match(l, "\"match_time\":([0-9.]+)");
                sum += double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }

            return sum / lines.Count;
        }

        [Fact]
        public void ScriptedBrains_PlayAtAHumanPace()
        {
            var off = new BotsSettings();
            off.Pace.TaskTimeFactorMin = 1f;
            off.Pace.TaskTimeFactorMax = 1f;
            off.Pace.PauseChance = 0f;
            off.Pace.WrongZoneChance = 0f;
            off.Pace.GroupChance = 0f;
            double fast = MeanMatchTime(off);
            double human = MeanMatchTime(new BotsSettings());
            Assert.True(human > fast * 1.15, "paced matches " + human + " s vs unpaced " + fast + " s");
        }

        [Fact]
        public void FakeTask_LastsLikeARealTaskStep()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Polluter);
            ConsoleDef c = m.Map.Consoles.First(x => x.Kind == ConsoleKind.Task);
            bot.Position = c.Position;
            bot.Executor.Pace = Policy(0f);
            float minS = m.Content.Tasks.First(t => t.ConsoleIds.Contains(c.Id)).MinSeconds;

            bot.Begin(BotAction.FakeTaskHere);
            float used = bot.RunUntilIdle(60f);
            Assert.True(used >= minS * 1.5f - 0.2f, "fake task lasted " + used + " s, a real step takes " + minS * 1.5f);
        }
    }
}
