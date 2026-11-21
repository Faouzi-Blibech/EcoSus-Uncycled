using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>Review fix round 1, part 2: no engine reads in the executor, results, schema text, vote retry.</summary>
    public class FixRoundMoreTests
    {
        private static readonly Vec2 MarketBins = new Vec2(7.5f, 5.5f);

        private static SyntheticBot BotAtMarketBins(TestMatch m)
        {
            var bot = new SyntheticBot(m, Role.Citizen, MarketBins);
            bot.Tasks.Add(new TaskInfo(1, "sort_waste", 1, 0, "market_bins"));
            return bot;
        }

        private static int StepsUntilTaskStep(SyntheticBot bot)
        {
            bot.Begin(BotAction.UseHere);
            for (int i = 1; i <= 400; i++)
            {
                bot.Step();
                if (bot.Sent.Any(c => c is TaskStepCommand))
                {
                    return i;
                }
            }

            return -1;
        }

        // ---------- I4: min_s comes from the definitions the executor is given, not from the engine ----------

        [Fact]
        public void Executor_UsesTheTaskDefinitionsItIsGiven()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot withDefs = BotAtMarketBins(m);
            int steps = StepsUntilTaskStep(withDefs);

            float minS = m.Content.Tasks.Single(t => t.Id == "sort_waste").MinSeconds;
            Assert.InRange(steps * SyntheticBot.Dt, minS, minS + 0.5f);

            SyntheticBot without = BotAtMarketBins(m);
            without.Executor.TaskTypes = null;
            int fallback = StepsUntilTaskStep(without);
            Assert.InRange(fallback * SyntheticBot.Dt, ActionExecutor.FallbackTaskMinS, ActionExecutor.FallbackTaskMinS + 0.5f);
        }

        [Fact]
        public void Executor_WorksFromTheConfigConstructorWithoutMemory()
        {
            TestMatch m = TestMatch.Create();
            var nav = new Navigator(m.Map.Walk, m.Config.SpeedTilesPerS);
            var exec = new ActionExecutor(nav, new MoveSender(), m.Config, m.Content.Tasks);
            var bot = new SyntheticBot(m, Role.Polluter);
            bot.Players.Add(SyntheticBot.Player(1, 16.5f, 12.5f));
            var sent = new List<ICommand>();

            exec.Begin(BotAction.FollowSlot0, bot.View(), m.Map, 30f);
            exec.Step(bot.View(), 0.05f, 30f, sent.Add);

            Assert.Contains(sent, c => c is MoveCommand);
        }

        // ---------- m3: the executor can be told a command was rejected ----------

        [Fact]
        public void Executor_EndsRepairSmogWhenTheFixWasRejected()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Citizen, new Vec2(40.5f, 6.5f));
            bot.Sabotage = new SabotageView(SabotageKind.Smog, null, new List<string>(), 0.5f);
            bot.Begin(BotAction.RepairSmog);
            bot.Step();
            Assert.True(bot.Executor.Busy);

            bot.Executor.NotifyResult(bot.Sent[0], CommandResult.Rejected(RejectReason.NotAllowed));

            Assert.False(bot.Executor.Busy);
        }

        [Fact]
        public void Executor_EndsCleanWhenSomeoneElseIsAlreadyCleaning()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Citizen, new Vec2(11.5f, 12.5f));
            bot.Piles.Add(new PileView(1, new Vec2(11.5f, 12.5f), PileKind.Dump, PileState.Fresh));
            bot.Begin(BotAction.CleanNearest);
            bot.Step();
            Assert.IsType<CleanStartCommand>(Assert.Single(bot.Sent));
            Assert.True(bot.Executor.Busy);

            bot.Executor.NotifyResult(bot.Sent[0], CommandResult.Rejected(RejectReason.AlreadyCleaning));

            Assert.False(bot.Executor.Busy);
        }

        [Fact]
        public void Executor_KeepsGoingOnAnAcceptedResult()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Citizen, new Vec2(11.5f, 12.5f));
            bot.Piles.Add(new PileView(1, new Vec2(11.5f, 12.5f), PileKind.Dump, PileState.Fresh));
            bot.Begin(BotAction.CleanNearest);
            bot.Step();

            bot.Executor.NotifyResult(bot.Sent[0], CommandResult.Ok);

            Assert.True(bot.Executor.Busy);
        }

        // ---------- m6: the hash covers generic types and the scale constants ----------

        [Fact]
        public void SchemaText_CoversGenericTypesAndScaleConstants()
        {
            string text = ObservationBuilder.SchemaText;

            Assert.Contains("System.Collections.Generic.IReadOnlyList`1[Sim.Match.PlayerView]", text);
            Assert.Contains("System.Nullable`1[System.Int32]", text);
            Assert.Contains("const:RelScale=", text);
            Assert.Contains("const:DistScale=", text);
            Assert.Contains("const:SuspicionMax=", text);
        }

        // ---------- settings ----------

        [Fact]
        public void BotsSettings_ReadsTheFollowCap()
        {
            string path = Path.Combine(Path.GetTempPath(), "uncycled-bots-" + System.Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{ \"follow_cap_s\": 2.5, \"skill\": 0.9 }");
            try
            {
                BotsSettings s = BotsSettings.Load(path);
                Assert.Equal(2.5f, s.FollowCapS);
                Assert.Equal(0.9f, s.Skill);
                Assert.Equal(4f, new BotsSettings().FollowCapS);
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ---------- the vote retry ----------

        [Fact]
        public void Brain_VotesSkipWhenItsSuspectWasAlreadyEjected()
        {
            TestMatch m = TestMatch.Create(2);
            m.TickFor(11f);
            byte polluter = m.FirstSeatOf(Role.Polluter);
            SeatView pv = m.Engine.ViewFor(polluter);
            PlayerView witness = pv.Players.First(p => Vec2.Distance(p.Position, pv.Me.Position) <= 1.0f
                && !pv.Me.Partners.Contains(p.Seat));
            byte bot = witness.Seat;

            // the polluter dumps in front of the bot, who reports the pile
            int pile = m.SeedDump(polluter).Id;
            Assert.Contains(m.Engine.Witnesses.SeenBy(bot), r => r.Act == ActKind.Dump && r.Actor == polluter);
            Assert.True(m.Engine.Apply(bot, new ReportCommand(pile)).IsOk);

            // meeting 1: everybody votes the polluter out
            for (int i = 0; i < 4000 && m.Engine.ViewFor(0).Meeting?.Stage != MeetingStage.Voting; i++)
            {
                m.Tick();
            }

            foreach (PlayerState p in m.Engine.Players.Where(p => p.IsActive))
            {
                Assert.True(m.Engine.Apply(p.Seat, new VoteCommand(p.Seat == polluter ? (byte?)null : polluter)).IsOk);
            }

            for (int i = 0; i < 4000 && m.Engine.Phase != MatchPhase.Playing; i++)
            {
                m.Tick();
            }

            Assert.False(m.Engine.Find(polluter)!.IsActive, "the polluter was not ejected");
            Assert.Equal(MatchPhase.Playing, m.Engine.Phase);

            // meeting 2, called at the button by another seat
            m.TickFor(16f);
            byte caller = m.Engine.Players.First(p => p.IsActive && p.Seat != bot && p.Role == Role.Citizen).Seat;
            var driver = new BotDriver(m, caller);
            driver.Begin(BotAction.GoEmergency);
            driver.RunUntilIdle(30f);
            driver.Begin(BotAction.CallMeeting);
            driver.RunUntilIdle(3f);
            Assert.Equal(MatchPhase.Meeting, m.Engine.Phase);

            // a brain whose memory still holds the dump it saw: it names the ejected polluter first
            IBotBrain brain = BrainFactory.Scripted(Role.Citizen, new BotsSettings(), new XorShiftRandom(9));
            var votes = new List<KeyValuePair<VoteCommand, CommandResult>>();
            for (int i = 0; i < 3000 && m.Engine.Phase == MatchPhase.Meeting && votes.Count < 2; i++)
            {
                brain.Think(m.Engine, bot, TestMatch.Dt, c =>
                {
                    if (c is VoteCommand v)
                    {
                        votes.Add(new KeyValuePair<VoteCommand, CommandResult>(v, m.Engine.Apply(bot, c)));
                    }
                });
                m.Tick();
            }

            Assert.Equal(2, votes.Count);
            Assert.Equal(polluter, votes[0].Key.Target);
            Assert.False(votes[0].Value.IsOk);
            Assert.Null(votes[1].Key.Target);
            Assert.True(votes[1].Value.IsOk);
            MeetingView meeting = m.Engine.ViewFor(bot).Meeting!;
            Assert.True(meeting.MyVoteCast);
            Assert.Null(meeting.MyVote);
        }
    }
}
