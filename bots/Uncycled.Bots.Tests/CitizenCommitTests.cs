using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.World;
using Uncycled.Bots.Neural;
using Uncycled.Server.Content;
using Uncycled.Train;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// A citizen that has opened a task session works the step to its end like a human: it is not
    /// re-asked every decision, only a meeting, a sabotage alarm or a hit stops it, and a step can fail
    /// by chance (bots.json pace.task_fail_chance) and is then worked again.
    /// </summary>
    public class CitizenCommitTests
    {
        private const string FrozenSchemaHash = "d0d5d025986cd8715b9e48ca244261fd1db77cd7156710c398fff5ed5ab3a6dd";

        private sealed class Fixed : IRandom
        {
            private readonly float _value;

            public Fixed(float value) { _value = value; }

            public int NextInt(int maxExclusive) => 0;

            public float NextFloat() => _value;
        }

        private sealed class Scripted : IRandom
        {
            private readonly Queue<float> _values;

            public Scripted(params float[] values) { _values = new Queue<float>(values); }

            public int NextInt(int maxExclusive) => 0;

            public float NextFloat() => _values.Count > 0 ? _values.Dequeue() : 0.5f;
        }

        private static PacePolicy Pace(IRandom rng, float failChance)
        {
            var s = new PaceSettings { PauseChance = 0f, WrongZoneChance = 0f, GroupChance = 0f, TaskFailChance = failChance };
            return new PacePolicy(s, rng);
        }

        /// <summary>A citizen standing at a task console with one open task (type unknown to the executor: min 6 s).</summary>
        private static SyntheticBot AtATask(TestMatch m, PacePolicy pace)
        {
            ConsoleDef console = m.Map.Consoles.First(c => c.Kind == ConsoleKind.Task);
            var bot = new SyntheticBot(m, Role.Citizen, console.Position);
            bot.Tasks.Add(new TaskInfo(1, "t", 1, 0, console.Id));
            bot.Executor.Pace = pace;
            return bot;
        }

        private static void StartCommitment(SyntheticBot bot)
        {
            bot.Begin(BotAction.UseHere);
            bot.Step();
            bot.Step();
            Assert.True(bot.Executor.Committed, "the session is open, the bot must be committed");
        }

        private static int Count<T>(SyntheticBot bot) => bot.Sent.Count(c => c is T);

        // ---------- commitment ----------

        [Fact]
        public void Committed_IgnoresOtherActionsUntilTheStepEnds_AndSendsOneTaskStep()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = AtATask(m, Pace(new Fixed(0.5f), 0f));
            StartCommitment(bot);

            for (int i = 0; i < 20; i++)
            {
                bot.Begin(i % 2 == 0 ? BotAction.GoZone0 : BotAction.Stay);
                bot.Step();
                Assert.True(bot.Executor.Committed);
                Assert.Equal(BotAction.UseHere, bot.Executor.Current);
            }

            bot.RunUntilIdle(30f);
            Assert.False(bot.Executor.Committed);
            Assert.Equal(1, Count<UseCommand>(bot));
            Assert.Equal(1, Count<TaskStepCommand>(bot));
            Assert.Equal(0, Count<MoveCommand>(bot));
        }

        [Theory]
        [InlineData("meeting")]
        [InlineData("fire")]
        [InlineData("smog")]
        [InlineData("hit")]
        public void StopTrigger_EndsTheCommitment_AndTheStepIsLost(string trigger)
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = AtATask(m, Pace(new Fixed(0.5f), 0f));
            StartCommitment(bot);
            for (int i = 0; i < 10; i++)
            {
                bot.Step();
            }

            Assert.True(bot.Executor.Committed);
            switch (trigger)
            {
                case "meeting": bot.Phase = MatchPhase.Meeting; break;
                case "fire": bot.Sabotage = new SabotageView(SabotageKind.Fire, 60f, new List<string>(), 0f); break;
                case "smog": bot.Sabotage = new SabotageView(SabotageKind.Smog, null, new List<string>(), 0.5f); break;
                case "hit": bot.Poison = 34; break;
            }

            bot.Step();
            Assert.False(bot.Executor.Committed);
            Assert.False(bot.Executor.Busy);

            bot.Phase = MatchPhase.Playing;
            bot.Sabotage = null;
            for (int i = 0; i < 400; i++)
            {
                bot.Step();
            }

            Assert.Equal(0, Count<TaskStepCommand>(bot));
        }

        [Fact]
        public void AnAlarmThatWasAlreadyOnWhenTheStepBegan_IsNoTrigger()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = AtATask(m, Pace(new Fixed(0.5f), 0f));
            bot.Sabotage = new SabotageView(SabotageKind.Smog, null, new List<string>(), 0.5f);
            bot.Poison = 34;
            StartCommitment(bot);

            bot.RunUntilIdle(30f);
            Assert.Equal(1, Count<TaskStepCommand>(bot));
        }

        [Fact]
        public void APolluterIsNeverCommitted()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Polluter, m.Map.Consoles.First(c => c.Kind == ConsoleKind.Task).Position);
            bot.Executor.Pace = Pace(new Fixed(0.5f), 1f);
            bot.Begin(BotAction.FakeTaskHere);
            bot.Step();
            bot.Step();
            Assert.False(bot.Executor.Committed);
            bot.Begin(BotAction.GoZone0);
            Assert.Equal(BotAction.GoZone0, bot.Executor.Current);
        }

        // ---------- failure ----------

        [Fact]
        public void FailChanceOne_NeverSendsATaskStep_AndKeepsRetrying()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = AtATask(m, Pace(new Fixed(0.5f), 1f));
            StartCommitment(bot);
            for (int i = 0; i < 1200; i++)
            {
                bot.Step();
            }

            Assert.Equal(0, Count<TaskStepCommand>(bot));
            Assert.True(bot.Executor.Committed, "the bot keeps working the step");
            Assert.True(bot.Executor.StepFailures >= 3, "failures " + bot.Executor.StepFailures);
        }

        [Fact]
        public void FailChanceZero_IsNeverRetried()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = AtATask(m, Pace(new Fixed(0.5f), 0f));
            StartCommitment(bot);
            bot.RunUntilIdle(30f);
            Assert.Equal(0, bot.Executor.StepFailures);
            Assert.Equal(1, Count<TaskStepCommand>(bot));
        }

        [Fact]
        public void AFailedStep_IsWorkedAgainForAFreshTime_ThenSent()
        {
            TestMatch m = TestMatch.Create();
            // draws in order: pause roll (none at chance 0), step factor 0.5 -> x2.0, fail roll 0.0 (fails at chance 0.5), fresh factor 0.5, fail roll 0.9 (passes)
            SyntheticBot bot = AtATask(m, Pace(new Scripted(0.5f, 0.5f, 0.0f, 0.5f, 0.9f), 0.5f));
            StartCommitment(bot);
            float used = bot.RunUntilIdle(60f) + 2 * SyntheticBot.Dt;

            Assert.Equal(1, bot.Executor.StepFailures);
            Assert.Equal(1, Count<TaskStepCommand>(bot));
            Assert.Equal(1, Count<UseCommand>(bot));
            float oneAttempt = 6f * 2.0f;
            Assert.InRange(used, 2 * oneAttempt, 2 * oneAttempt + 1f);
        }

        [Fact]
        public void FailureDice_AreSeededPerBot()
        {
            List<string> Run(ulong seed)
            {
                TestMatch m = TestMatch.Create();
                var pace = new PacePolicy(new PaceSettings { PauseChance = 0f, TaskFailChance = 0.5f }, new XorShiftRandom(seed));
                SyntheticBot bot = AtATask(m, pace);
                bot.Begin(BotAction.UseHere);
                var log = new List<string>();
                for (int i = 0; i < 1500; i++)
                {
                    int before = bot.Sent.Count;
                    bot.Step();
                    log.AddRange(bot.Sent.Skip(before).Select(c => i + ":" + c.GetType().Name));
                    log.Add("f" + bot.Executor.StepFailures);
                }

                return log;
            }

            Assert.Equal(Run(5), Run(5));
            Assert.Equal(Run(9), Run(9));
        }

        [Fact]
        public void Settings_TaskFailChanceDefaultsToTenPercent_AndIsReadFromBotsJson()
        {
            Assert.Equal(0.1f, new BotsSettings().Pace.TaskFailChance);
            string path = Path.Combine(Path.GetTempPath(), "fail-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{ \"pace\": { \"task_fail_chance\": 0.25 } }");
            try
            {
                Assert.Equal(0.25f, BotsSettings.Load(path).Pace.TaskFailChance);
            }
            finally
            {
                File.Delete(path);
            }

            string repoJson = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "bots.json"));
            if (File.Exists(repoJson))
            {
                Assert.Contains("task_fail_chance", File.ReadAllText(repoJson));
            }
        }

        // ---------- the brains ----------

        private static int Progress(TestMatch m) => m.Events.Count(e => e is TaskProgressEvent);

        private static BrainRig CitizensOnly(TestMatch m, float failChance)
        {
            var rig = new BrainRig(m, 5, s => s.Pace.TaskFailChance = failChance);
            foreach (PlayerState p in m.Engine.Players)
            {
                if (p.Role == Role.Polluter)
                {
                    rig.Skip.Add(p.Seat);
                }
            }

            return rig;
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void ScriptedCitizens_FailChanceOne_NeverCompleteAStep_ButZeroDoes()
        {
            TestMatch failing = TestMatch.Create(5);
            BrainRig failRig = CitizensOnly(failing, 1f);
            failRig.RunUntil(() => failing.Engine.Phase == MatchPhase.Ended, 240f);
            Assert.Equal(0, failRig.Sent.Count(c => c.Command is TaskStepCommand));
            Assert.Equal(0, Progress(failing));

            TestMatch ok = TestMatch.Create(5);
            BrainRig okRig = CitizensOnly(ok, 0f);
            okRig.RunUntil(() => ok.Engine.Phase == MatchPhase.Ended, 240f);
            Assert.True(Progress(ok) >= 6, "steps done " + Progress(ok));
            Assert.All(okRig.Sent.Where(c => c.Command is TaskStepCommand), c => Assert.True(c.Result.IsOk, c.Result.ToString()));
        }

        /// <summary>First time a task is in reach: use it. Every later call: some other valid action, never UseHere.</summary>
        private sealed class DistractedPolicy : IPolicy
        {
            private bool _used;

            public List<float> CallTimes { get; } = new List<float>();

            public Func<float> Clock { get; set; } = () => 0f;

            public int Act(float[] obs, bool[] mask, float temperature, IRandom rng)
            {
                CallTimes.Add(Clock());
                if (!_used && mask[(int)BotAction.UseHere])
                {
                    _used = true;
                    return (int)BotAction.UseHere;
                }

                if (!_used)
                {
                    return mask[(int)BotAction.GoNextTask] ? (int)BotAction.GoNextTask : (int)BotAction.Stay;
                }

                return mask[(int)BotAction.GoZone0] ? (int)BotAction.GoZone0 : (int)BotAction.Stay;
            }
        }

        private sealed class NoBrain : IBotBrain
        {
            public void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send) { }
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void NeuralBrain_IsNotAskedWhileCommitted_AndTheStepCompletes()
        {
            TestMatch m = TestMatch.Create(6);
            BrainRig rig = CitizensOnly(m, 0f);
            byte seat = m.FirstSeatOf(Role.Citizen);
            foreach (PlayerState p in m.Engine.Players)
            {
                rig.Skip.Add(p.Seat);
            }

            rig.Skip.Remove(seat);
            var settings = new BotsSettings();
            settings.Pace.PauseChance = 0f;
            settings.Pace.WrongZoneChance = 0f;
            settings.Pace.GroupChance = 0f;
            settings.Pace.TaskFailChance = 0f;
            var policy = new DistractedPolicy { Clock = () => m.Engine.MatchTime };
            rig.Brains[seat] = new NeuralBrain(policy, new NoBrain(), settings, new XorShiftRandom(3), m.Content.Tasks);

            rig.RunUntil(() => rig.Sent.Any(c => c.Seat == seat && c.Command is TaskStepCommand), 120f);

            SentCommand use = rig.Sent.First(c => c.Seat == seat && c.Command is UseCommand);
            SentCommand step = rig.Sent.First(c => c.Seat == seat && c.Command is TaskStepCommand);
            Assert.True(step.Result.IsOk, step.Result.ToString());
            Assert.Equal(1, Progress(m));
            Assert.DoesNotContain(policy.CallTimes, t => t > use.MatchTime + 0.001f && t < step.MatchTime);
        }

        // ---------- the training host ----------

        private static readonly string ContentDir = ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));

        private static MatchRunner TaskRewardRunner()
        {
            HostOptions o = HostOptions.Parse(new[] { "--content", ContentDir, "--matches", "1", "--seed", "7", "--threads", "1",
                "--rw-win", "0", "--rw-task", "1", "--rw-report", "0", "--rw-fix", "0", "--rw-hit", "0", "--rw-dump", "0", "--rw-ejected", "0" });
            ContentBundle content = ContentLoader.Load(o.ContentDir);
            return new MatchRunner(o, 0, content, HostBrains.Scripted(BotsSettings.Load("none"), content));
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Host_SendsNoDecisionFrameWhileCommitted_AndTheRewardReachesTheNextFrame()
        {
            MatchRunner r = TaskRewardRunner();
            var buf = new byte[r.AgentCount * r.FrameSize];
            var wasCommitted = new bool[r.AgentCount];
            float total = 0f;
            int placeholders = 0;
            int rewardedAfterCommit = 0;
            for (int d = 0; d < 600; d++)
            {
                r.WriteFrames(buf);
                var actions = new int[r.AgentCount];
                for (int a = 0; a < r.AgentCount; a++)
                {
                    int maskAt = a * r.FrameSize + r.MaskOffset;
                    float reward = BitConverter.ToSingle(buf, a * r.FrameSize + r.RewardOffset);
                    total += reward;
                    bool committed = r.AgentCommitted(a);
                    if (committed)
                    {
                        placeholders++;
                        Assert.Equal(0f, reward);
                        for (int i = 0; i < ActionCatalogue.Count; i++)
                        {
                            Assert.Equal(i == (int)BotAction.Stay, buf[maskAt + i] != 0);
                        }

                        actions[a] = (d % 2 == 0) ? (int)BotAction.Stay : (int)BotAction.GoZone0;
                    }
                    else
                    {
                        if (reward > 0f && wasCommitted[a])
                        {
                            rewardedAfterCommit++;
                        }

                        actions[a] = buf[maskAt + (int)BotAction.UseHere] != 0 ? (int)BotAction.UseHere
                            : buf[maskAt + (int)BotAction.GoNextTask] != 0 ? (int)BotAction.GoNextTask
                            : (int)BotAction.Stay;
                    }

                    wasCommitted[a] = committed;
                }

                r.Advance(actions);
            }

            Assert.True(placeholders > 0, "agents must have been committed at some decision");
            Assert.True(total >= 1f, "task steps must pay, total=" + total);
            Assert.True(rewardedAfterCommit >= 1, "the first frame after a committed step carries its reward");
            Assert.Equal(0, r.InvalidActions);
        }

        // ---------- schema ----------

        [Fact]
        public void SchemaHash_IsUnchanged()
        {
            Assert.Equal(FrozenSchemaHash, ObservationBuilder.SchemaHash);
        }
    }
}
