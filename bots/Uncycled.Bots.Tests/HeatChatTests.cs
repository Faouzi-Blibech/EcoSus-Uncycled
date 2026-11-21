using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Bots.Meeting;
using Uncycled.Server.Content;
using Uncycled.Train;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// Chat accusations feed the Round 5 heat: the meeting talker and the neural observation read one
    /// heat value per seat (BotMemory.HeatOf), fed through the MeetingLog in live rooms and in the training host.
    /// </summary>
    public class HeatChatTests
    {
        private const int Living = 8;
        private const byte Target = 5;

        private static readonly byte[] AllSeats = { 0, 1, 2, 3, 4, 5, 6, 7 };

        private static ChatEvent Say(byte seat, string id, params ChatArg[] args) => new ChatEvent(seat, id, args);

        private static MeetingResultEvent Result(params (byte Voter, byte? Target)[] votes)
        {
            var list = new List<VoteRecord>();
            var voted = new HashSet<byte>();
            foreach ((byte voter, byte? target) in votes)
            {
                list.Add(new VoteRecord(voter, target, false));
                voted.Add(voter);
            }

            foreach (byte s in AllSeats)
            {
                if (!voted.Contains(s))
                {
                    list.Add(new VoteRecord(s, null, false));
                }
            }

            return new MeetingResultEvent(list, null, false);
        }

        private static GameEvent[] Accusers(int k, string id = "sus")
        {
            var events = new List<GameEvent>();
            for (byte i = 1; i <= k; i++)
            {
                events.Add(Say(i, id, ChatArg.ForSeat(Target)));
                events.Add(Say(i, id, ChatArg.ForSeat(Target))); // saying it twice is still one accuser
            }

            events.Add(Say(7, "agree", ChatArg.ForSeat(Target))); // a reaction is not an accusation
            return events.ToArray();
        }

        private static MeetingView MeetingOf(MeetingStage stage)
        {
            return new MeetingView(stage, 30f, 0, MeetingCause.Report, new List<ClueInfo>(), new List<byte>(), false, null, 0f, 0, AllSeats);
        }

        private static SeatView InMeeting(SeatView live, MeetingStage stage = MeetingStage.Discussion)
        {
            return new SeatView(live.Me.Seat, MatchPhase.Meeting, 30f, 130f, live.DeadlineS, live.DeadlineClock, 130f, 10f, 2, live.Me, new List<PlayerView>(), new List<PileView>(), null, MeetingOf(stage), null);
        }

        private sealed class Spy : IMeetingObserver
        {
            public void OnMeetingEnded(MeetingOutcome outcome) { }
        }

        private sealed class FixedAccuser : IAccuser
        {
            public FixedAccuser(byte? target) { Accusation = target; }

            public byte? Accusation { get; }
        }

        private sealed class Capture : IMeetingDecider
        {
            public string Name => "capture";

            public MeetingState? Last { get; private set; }

            public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
            {
                Last = state;
                return Task.FromResult(MeetingDecision.Quiet("capture"));
            }
        }

        // ---------- the live path: chat events through the feed's MeetingLog ----------

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void Heat_KDistinctChatAccusersRaiseRound5HeatByKOverLiving(int k)
        {
            TestMatch m = TestMatch.Create();
            var log = new MeetingLog(m.Content.Phrases!);
            var feed = new MeetingFeed(log);
            var mem = new BotMemory(m.Map, m.Config);
            feed.Add(0, mem);

            feed.Handle(Accusers(k));
            Assert.Equal(k, log.AccusationsAgainst(Target));
            feed.Handle(new GameEvent[] { Result() });

            float expected = k / (float)Living;
            Assert.Equal(expected, mem.HeatAccusations(Target), 4);
            Assert.Equal(expected, mem.HeatOf(Target), 4);
            Assert.Equal(0f, mem.HeatOf(6), 4);
            Assert.Equal(0, mem.MeetingsSinceAccused(Target));
        }

        [Fact]
        public void Heat_ASeatsChatAccusationAndItsVoteForTheSameTargetCountOnce()
        {
            TestMatch m = TestMatch.Create();
            var log = new MeetingLog(m.Content.Phrases!);
            var feed = new MeetingFeed(log);
            var mem = new BotMemory(m.Map, m.Config);
            feed.Add(0, mem);
            feed.Add(1, new Spy(), new FixedAccuser(Target)); // votes for the target it also named in chat
            feed.Add(2, new Spy(), new FixedAccuser(Target)); // votes for the target without a chat line

            feed.Handle(new GameEvent[] { Say(1, "sus", ChatArg.ForSeat(Target)) });
            feed.Handle(new GameEvent[] { Result((1, Target), (2, Target)) });

            Assert.Equal(2f / Living + 2f / Living, mem.HeatOf(Target), 4); // 2 accusers + 2 votes
            Assert.Equal(2f / Living, mem.HeatAccusations(Target), 4);
        }

        [Fact]
        public void Heat_ChatAccusationsCountInsideTheMeetingBeforeItEnds()
        {
            TestMatch m = TestMatch.Create();
            var log = new MeetingLog(m.Content.Phrases!);
            var mem = new BotMemory(m.Map, m.Config);
            SeatView live = m.Engine.ViewFor(0);
            SeatView meeting = InMeeting(live);

            mem.Observe(meeting, 130f, new List<WitnessRecord>());
            foreach (GameEvent e in Accusers(2))
            {
                log.Add((ChatEvent)e, 131f);
            }

            MeetingState s = MeetingStateBuilder.Build(meeting, mem, log, new SuspicionModel(), null);

            Assert.Equal(2f / Living, s.HeatOf(Target), 4);
            Assert.Equal(mem.HeatOf(Target), s.HeatOf(Target), 4);

            // back in play the log is no longer this meeting: the heat is what the table recorded
            mem.Observe(m.Engine.ViewFor(0), 200f, new List<WitnessRecord>());
            Assert.Equal(0f, mem.HeatOf(Target), 4);
        }

        [Fact]
        public void Heat_TheTalkersStateCarriesTheMemoryHeatOfEverySeat()
        {
            TestMatch m = TestMatch.Create();
            var log = new MeetingLog(m.Content.Phrases!);
            var mem = new BotMemory(m.Map, m.Config);
            var feed = new MeetingFeed(log);
            feed.Add(0, mem);

            // seat 5 was voted for once and accused once in an earlier meeting: 2/8 of recorded heat
            feed.Handle(new GameEvent[] { Say(1, "sus", ChatArg.ForSeat(Target)) });
            feed.Handle(new GameEvent[] { Result((1, Target)) });
            log.Clear();

            var capture = new Capture();
            var talker = new MeetingTalker(capture, capture, new MeetingFloor(), new Random(3));
            SeatView live = m.Engine.ViewFor(0);
            SeatView voting = InMeeting(live, MeetingStage.Voting);
            foreach (GameEvent e in Accusers(3))
            {
                log.Add((ChatEvent)e, 131f);
            }

            for (int i = 0; i < 400 && capture.Last == null; i++)
            {
                float now = 130f + i * 0.1f;
                mem.Observe(voting, now, new List<WitnessRecord>());
                talker.Step(m.Engine, 0, voting, mem, log, now, _ => { });
            }

            Assert.NotNull(capture.Last);
            foreach (byte seat in AllSeats)
            {
                Assert.Equal(Math.Min(1f, mem.HeatOf(seat)), capture.Last!.HeatOf(seat), 4);
            }

            // the recorded heat (1 vote + 1 accuser) plus 3 accusers now
            Assert.Equal(2f / Living + 3f / Living, capture.Last!.HeatOf(Target), 4);
        }

        [Fact]
        public void Heat_TheObservationShowsTheHeatTheTalkerSawAtTheEndOfTheMeeting()
        {
            TestMatch m = TestMatch.Create();
            SeatView live = m.Engine.ViewFor(0);
            byte seen = live.Players.Where(p => !p.IsOut).OrderBy(p => Vec2.Distance(p.Position, live.Me.Position)).ThenBy(p => p.Seat).First().Seat;
            var log = new MeetingLog(m.Content.Phrases!);
            var feed = new MeetingFeed(log);
            var mem = new BotMemory(m.Map, m.Config);
            feed.Add(0, mem);

            SeatView meeting = InMeeting(live);
            mem.Observe(meeting, 130f, new List<WitnessRecord>());
            feed.Handle(new GameEvent[]
            {
                Say(1, "sus", ChatArg.ForSeat(seen)), Say(2, "sus", ChatArg.ForSeat(seen)), Say(3, "sus", ChatArg.ForSeat(seen)),
            });
            float talkerSaw = MeetingStateBuilder.Build(meeting, mem, log, new SuspicionModel(), null).HeatOf(seen);
            feed.Handle(new GameEvent[] { Result() });

            mem.Observe(live, 200f, new List<WitnessRecord>());
            var obs = new float[ObservationBuilder.Size];
            ObservationBuilder.Build(live, mem, m.Map, m.Config, 200f, obs);
            float shown = obs[IndexOf("player0.heat")];

            Assert.Equal(3f / Living, talkerSaw, 4);
            Assert.Equal(talkerSaw, shown, 4);
        }

        private static int IndexOf(string field)
        {
            for (int i = 0; i < ObservationBuilder.FieldNames.Count; i++)
            {
                if (ObservationBuilder.FieldNames[i] == field)
                {
                    return i;
                }
            }

            throw new InvalidOperationException("no field " + field);
        }

        // ---------- the scripted accusation (a vote) goes through the same log ----------

        [Fact]
        public void Heat_ScriptedAccusationsReachHeatThroughTheMeetingLog()
        {
            TestMatch m = TestMatch.Create();
            var log = new MeetingLog(m.Content.Phrases!);
            var feed = new MeetingFeed(log);
            var mem = new BotMemory(m.Map, m.Config);
            feed.Add(0, mem);
            for (byte s = 1; s <= 3; s++)
            {
                feed.Add(s, new Spy(), new FixedAccuser(Target));
            }

            feed.Handle(new GameEvent[] { Result() });

            Assert.Equal(3, log.AccusationsAgainst(Target));
            Assert.Equal(3f / Living, mem.HeatAccusations(Target), 4);
        }

        [Fact]
        public void Host_TheTrainingRunnersMeetingsFeedHeatFromItsMeetingLog()
        {
            string dir = ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
            HostOptions o = HostOptions.Parse(new[] { "--content", dir, "--matches", "4", "--seed", "7", "--threads", "1" });
            ContentBundle content = ContentLoader.Load(o.ContentDir);
            var r = new MatchRunner(o, 0, content, HostBrains.Scripted(BotsSettings.Load("none"), content));
            var rng = new XorShiftRandom(21);
            int recorded = 0;
            int withAccusation = 0;
            for (int d = 0; d < 4000 && withAccusation < 3; d++)
            {
                r.AdvanceRandom(rng);
                BotMemory mem = r.AgentMemory(0);
                if (mem.MeetingsRecorded == recorded || r.MeetingChat.Acts.Count == 0)
                {
                    recorded = mem.MeetingsRecorded;
                    continue;
                }

                recorded = mem.MeetingsRecorded;
                for (byte s = 0; s < Living; s++)
                {
                    int accusers = r.MeetingChat.AccusationsAgainst(s);
                    Assert.True(accusers == 0 || mem.HeatAccusations(s) > 0f, "seat " + s + " has " + accusers + " accusers in the log but no accusation heat");
                    if (accusers > 0)
                    {
                        float voters = accusers / mem.HeatAccusations(s);
                        Assert.InRange(voters, 1.99f, 8.01f);
                        Assert.Equal(Math.Round(voters), voters, 3);
                        withAccusation++;
                    }
                    else
                    {
                        Assert.Equal(0f, mem.HeatAccusations(s));
                    }
                }
            }

            Assert.True(withAccusation > 0, "no accusation reached a learner memory through the log");
        }

        // ---------- thresholds on the Round 5 scale ----------

        [Fact]
        public void Grounding_HotIsTwoAccusersOfEightAndCoolToleratesOne()
        {
            Assert.True(2f / Living >= Grounding.HotHeat);
            Assert.True(1f / Living < Grounding.HotHeat);
            Assert.True(1f / 7f <= Grounding.CoolHeat);
            Assert.True(2f / Living > Grounding.CoolHeat);
        }
    }
}
