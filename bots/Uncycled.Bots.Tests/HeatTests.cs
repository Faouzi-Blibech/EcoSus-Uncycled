using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>Heat: what the table knows about a seat after meetings (votes it got, accusations against it).</summary>
    public class HeatTests
    {
        private static MeetingOutcome Outcome(int voters, byte[] votedFor, params (byte By, byte Target)[] accusations)
        {
            var votes = new List<VoteRecord>();
            for (int i = 0; i < voters; i++)
            {
                byte? target = i < votedFor.Length ? votedFor[i] : (byte?)null;
                votes.Add(new VoteRecord((byte)i, target, false));
            }

            return new MeetingOutcome(votes, null, accusations.Select(a => new Accusation(a.By, a.Target)).ToList());
        }

        private static BotMemory NewMemory()
        {
            TestMatch m = TestMatch.Create();
            return new BotMemory(m.Map, m.Config);
        }

        [Fact]
        public void Heat_IsVoteShareAndAccusationShare()
        {
            BotMemory mem = NewMemory();
            // 6 living voters: seat 3 gets 3 votes and 2 accusations, seat 1 gets one vote
            mem.RecordMeeting(Outcome(6, new byte[] { 3, 3, 3, 1 }, ((byte)0, (byte)3), ((byte)2, (byte)3)));

            Assert.Equal(0.5f, mem.HeatVotes(3), 4);
            Assert.Equal(2f / 6f, mem.HeatAccusations(3), 4);
            Assert.Equal(0.5f + 2f / 6f, mem.HeatOf(3), 4);
            Assert.Equal(1f / 6f, mem.HeatOf(1), 4);
            Assert.Equal(0f, mem.HeatOf(2), 4);
            Assert.Equal(1, mem.MeetingsRecorded);
        }

        [Fact]
        public void Heat_CoolsByHalfEachMeetingAndCountsMeetingsSinceAccused()
        {
            BotMemory mem = NewMemory();
            mem.RecordMeeting(Outcome(6, new byte[] { 3, 3, 3 }, ((byte)0, (byte)3)));
            float first = mem.HeatOf(3);
            Assert.Equal(0, mem.MeetingsSinceAccused(3));

            mem.RecordMeeting(Outcome(6, new byte[0]));
            Assert.Equal(first * 0.5f, mem.HeatOf(3), 4);
            Assert.Equal(1, mem.MeetingsSinceAccused(3));

            for (int i = 0; i < 10; i++)
            {
                mem.RecordMeeting(Outcome(6, new byte[0]));
            }

            Assert.Equal(BotMemory.MeetingsSinceAccusedCap, mem.MeetingsSinceAccused(3));
            Assert.Equal(BotMemory.MeetingsSinceAccusedCap, mem.MeetingsSinceAccused(5));
        }

        [Fact]
        public void Heat_AVoteWithoutAccusationDoesNotResetTheAccusedCounter()
        {
            BotMemory mem = NewMemory();
            mem.RecordMeeting(Outcome(6, new byte[0], ((byte)0, (byte)3)));
            mem.RecordMeeting(Outcome(6, new byte[] { 3, 3 }));
            Assert.Equal(1, mem.MeetingsSinceAccused(3));
        }

        [Fact]
        public void Observation_CarriesOwnHeatAndVisiblePlayerHeat()
        {
            TestMatch m = TestMatch.Create();
            m.TickFor(1f);
            var mem = new BotMemory(m.Map, m.Config);
            SeatView view = m.Engine.ViewFor(0);
            mem.Observe(view, m.Engine.MatchTime, m.Engine.Witnesses.SeenBy(0));
            Assert.NotEmpty(view.Players); // the spawn room: others are in sight

            byte other = view.Players[0].Seat;
            // own seat 0: 3 of 6 votes, 3 of 6 accusations; the visible seat: 3 of 6 votes, no accusation
            mem.RecordMeeting(Outcome(6, new byte[] { 0, 0, 0, other, other, other },
                ((byte)1, (byte)0), ((byte)2, (byte)0), ((byte)3, (byte)0)));
            var obs = new float[ObservationBuilder.Size];
            ObservationBuilder.Build(view, mem, m.Map, m.Config, m.Engine.MatchTime, obs);

            IReadOnlyList<string> names = ObservationBuilder.FieldNames;
            Assert.Equal(0.5f, obs[IndexOf(names, "self.heat_votes")], 4);
            Assert.Equal(0.5f, obs[IndexOf(names, "self.heat_accusations")], 4);
            Assert.Equal(0f, obs[IndexOf(names, "self.meetings_since_accused")], 4);
            Assert.Equal(0.5f, obs[IndexOf(names, "player0.heat")], 4);
        }

        private static int IndexOf(IReadOnlyList<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == name)
                {
                    return i;
                }
            }

            throw new InvalidOperationException("no field " + name);
        }

        private sealed class Spy : IMeetingObserver
        {
            public List<MeetingOutcome> Got { get; } = new List<MeetingOutcome>();

            public void OnMeetingEnded(MeetingOutcome outcome) => Got.Add(outcome);
        }

        private sealed class FixedAccuser : IAccuser
        {
            public FixedAccuser(byte? target) { Accusation = target; }

            public byte? Accusation { get; }
        }

        [Fact]
        public void MeetingFeed_BuildsOutcomeFromTheResultEventAndTheAccusers()
        {
            var feed = new MeetingFeed();
            var a = new Spy();
            var b = new Spy();
            feed.Add(0, a, new FixedAccuser(5));
            feed.Add(1, b, new FixedAccuser(null));
            feed.Add(2, new Spy(), new FixedAccuser(5));

            var votes = new List<VoteRecord> { new VoteRecord(0, 5, false), new VoteRecord(1, null, false), new VoteRecord(2, 5, false) };
            feed.Handle(new GameEvent[] { new MeetingResultEvent(votes, 5, false) });

            Assert.Single(a.Got);
            Assert.Single(b.Got);
            MeetingOutcome o = a.Got[0];
            Assert.Equal(3, o.Votes.Count);
            Assert.Equal((byte?)5, o.Ejected);
            Assert.Equal(2, o.Accusations.Count);
            Assert.All(o.Accusations, x => Assert.Equal((byte)5, x.Target));
            Assert.DoesNotContain(o.Accusations, x => x.Accuser == 1);
        }

        [Fact]
        public void MeetingFeed_IgnoresOtherEvents()
        {
            var feed = new MeetingFeed();
            var a = new Spy();
            feed.Add(0, a);
            feed.Handle(new GameEvent[] { new VoteCastEvent(0) });
            Assert.Empty(a.Got);
        }
    }
}
