using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Bots.Meeting;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// How a bot weighs a meeting accusation like a real player: a specific eyewitness line (saw_dump / saw_hit
    /// with a zone) is worth a vote by itself, unless the bot's own sightings contradict it, the bot already
    /// suspects the accuser, or an unsuspected player spoke for the accused. A vague "sus" stays hearsay.
    /// </summary>
    public class EyewitnessTests
    {
        private const byte Self = 0;

        private static ChatEvent Say(byte seat, string id, params ChatArg[] args) => new ChatEvent(seat, id, args);

        private static MeetingLog NewLog(TestMatch m) => new MeetingLog(m.Content.Phrases!);

        /// <summary>Bot 0 saw the first visible player at t=100 (in `zone`); with a clue, a pile in that zone at t=110 makes him a near-pile suspect (30).</summary>
        private static (SuspicionModel Model, BotMemory Mem, byte Seen, string Zone, SeatView View) Setup(TestMatch m, bool withClue)
        {
            SeatView live = m.Engine.ViewFor(Self);
            PlayerView seenPlayer = live.Players[0];
            string zone = m.Map.ZoneAt(seenPlayer.Position);
            var mem = new BotMemory(m.Map, m.Config);
            // this bot saw only that one player: every other seat is a stranger to it (no own-sighting score)
            var one = new SeatView(Self, MatchPhase.Playing, 0f, 100f, live.DeadlineS, live.DeadlineClock, 100f, 10f, 2, live.Me, new List<PlayerView> { seenPlayer }, new List<PileView>(), null, null, null);
            mem.Observe(one, 100f, new List<WitnessRecord>());
            var clues = new List<ClueInfo>();
            if (withClue)
            {
                clues.Add(new ClueInfo(1, PileKind.Dump, zone, 110f, WasteType.Glass, null));
            }

            var meeting = new MeetingView(MeetingStage.Discussion, 30f, 0, MeetingCause.Report, clues, new List<byte>(), false, null, 0f);
            var view = new SeatView(Self, MatchPhase.Meeting, 30f, 130f, live.DeadlineS, live.DeadlineClock, 130f, 10f, 2, live.Me, new List<PlayerView>(), new List<PileView>(), null, meeting, null);
            var model = new SuspicionModel();
            model.Update(mem, view);
            return (model, mem, seenPlayer.Seat, zone, view);
        }

        /// <summary>A seat this bot never saw (so it has no own-sighting score) and that is not in `taken`.</summary>
        private static byte OtherSeat(BotMemory mem, params byte[] taken)
        {
            for (byte s = 1; s < 8; s++)
            {
                if (System.Array.IndexOf(taken, s) < 0 && !mem.SeenSeats.Contains(s))
                {
                    return s;
                }
            }

            throw new System.InvalidOperationException();
        }

        [Fact]
        public void OneEyewitness_IsEnoughForUndecidedBotsToVote()
        {
            TestMatch m = TestMatch.Create();
            byte accuser = 1;
            byte accused = 6;
            var votes = new List<byte?>();
            for (int bot = 0; bot < 4; bot++)
            {
                // four bots that saw nothing themselves: they only read the chat
                var mem = new BotMemory(m.Map, m.Config);
                SeatView view = m.Engine.ViewFor(Self);
                var model = new SuspicionModel();
                mem.Observe(view, 100f, new List<WitnessRecord>());
                model.Update(mem, view);
                MeetingLog log = NewLog(m);
                log.Add(Say(accuser, "saw_dump", ChatArg.ForSeat(accused), ChatArg.ForZone("market")), 5f);
                model.ApplyChat(log, mem, Self);
                votes.Add(model.Vote(view, Role.Citizen, new List<byte>()));
            }

            // with the witness's own vote that is 5 votes against 3 skips: the tally (more than the skips) ejects
            Assert.All(votes, v => Assert.Equal((byte?)accused, v));
        }

        [Fact]
        public void OneEyewitness_AddsTheVoteThreshold_OncePerAccuser()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, _, _) = Setup(m, withClue: false);
            byte target = OtherSeat(mem, seen);
            MeetingLog log = NewLog(m);
            log.Add(Say(seen, "saw_hit", ChatArg.ForSeat(target), ChatArg.ForZone("depot")), 5f);
            log.Add(Say(seen, "saw_dump", ChatArg.ForSeat(target), ChatArg.ForZone("depot")), 6f); // the same accuser again
            log.Add(Say(seen, "sus", ChatArg.ForSeat(target)), 7f);

            model.ApplyChat(log, mem, Self);

            Assert.Equal(SuspicionModel.VoteThreshold, model.Score(target));
            model.ApplyChat(log, mem, Self);
            Assert.Equal(SuspicionModel.VoteThreshold, model.Score(target)); // no stacking
        }

        [Fact]
        public void VagueAccusation_StaysHearsay_AndOneAccuserCannotVoteAlone()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, _, SeatView view) = Setup(m, withClue: false);
            byte target = OtherSeat(mem, seen);
            MeetingLog log = NewLog(m);
            log.Add(Say(seen, "sus", ChatArg.ForSeat(target)), 5f);
            log.Add(Say(seen, "sus", ChatArg.ForSeat(target)), 6f);

            model.ApplyChat(log, mem, Self);

            Assert.Equal(SuspicionModel.HearsayWeight, model.Score(target));
            Assert.Null(model.Vote(view, Role.Citizen, new List<byte>()));
        }

        [Fact]
        public void Eyewitness_ContradictedByTheBotsOwnSighting_AddsNothing()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, string zone, SeatView view) = Setup(m, withClue: true);
            string elsewhere = zone == "depot" ? "market" : "depot";
            byte accuser = OtherSeat(mem, seen);

            // the accuser says he saw `seen` dump in a zone where this bot did not see him at the clue time
            MeetingLog log = NewLog(m);
            log.Add(Say(accuser, "saw_dump", ChatArg.ForSeat(seen), ChatArg.ForZone(elsewhere)), 5f);
            model.ApplyChat(log, mem, Self);
            Assert.Equal(SuspicionModel.NearPileWeight, model.Score(seen)); // only the own near-pile sighting

            // the same line in the zone this bot saw him in is not contradicted
            var model2 = new SuspicionModel();
            model2.Update(mem, view);
            MeetingLog log2 = NewLog(m);
            log2.Add(Say(accuser, "saw_dump", ChatArg.ForSeat(seen), ChatArg.ForZone(zone)), 5f);
            model2.ApplyChat(log2, mem, Self);
            Assert.Equal(SuspicionModel.NearPileWeight + SuspicionModel.VoteThreshold, model2.Score(seen));
        }

        /// <summary>
        /// Bot 0 saw the first visible player at t=100, then the meeting starts at `meetingAt` with no clue pile:
        /// only the bot's own sightings can contradict a claim. Returns the model after Update and Observe(meeting).
        /// </summary>
        private static (SuspicionModel Model, BotMemory Mem, byte Seen, string Zone) SetupNoClue(TestMatch m, float meetingAt, params float[] seenAt)
        {
            SeatView live = m.Engine.ViewFor(Self);
            PlayerView seenPlayer = live.Players[0];
            string zone = m.Map.ZoneAt(seenPlayer.Position);
            var mem = new BotMemory(m.Map, m.Config);
            foreach (float t in seenAt.Length == 0 ? new[] { 100f } : seenAt)
            {
                var one = new SeatView(Self, MatchPhase.Playing, 0f, t, live.DeadlineS, live.DeadlineClock, t, 10f, 2, live.Me, new List<PlayerView> { seenPlayer }, new List<PileView>(), null, null, null);
                mem.Observe(one, t, new List<WitnessRecord>());
            }

            var meeting = new MeetingView(MeetingStage.Discussion, 30f, 0, MeetingCause.Emergency, new List<ClueInfo>(), new List<byte>(), false, null, 0f);
            var view = new SeatView(Self, MatchPhase.Meeting, 30f, meetingAt, live.DeadlineS, live.DeadlineClock, meetingAt, 10f, 2, live.Me, new List<PlayerView>(), new List<PileView>(), null, meeting, null);
            mem.Observe(view, meetingAt, new List<WitnessRecord>());
            var model = new SuspicionModel();
            model.Update(mem, view);
            return (model, mem, seenPlayer.Seat, zone);
        }

        [Fact]
        public void Eyewitness_WithNoCluePile_IsContradicted_WhenTheBotSawTheAccusedElsewhereTwice_12sApart()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, string zone) = SetupNoClue(m, 115f, 100f, 112f); // a real alibi: two sightings spanning 12 s inside the window
            string elsewhere = zone == "depot" ? "market" : "depot";
            byte accuser = OtherSeat(mem, seen);
            MeetingLog log = NewLog(m);
            log.Add(Say(accuser, "saw_dump", ChatArg.ForSeat(seen), ChatArg.ForZone(elsewhere)), 5f);

            model.ApplyChat(log, mem, Self);

            Assert.Equal(0f, model.Score(seen));
        }

        [Fact]
        public void Eyewitness_WithNoCluePile_Counts_WhenTheBotSawTheAccusedElsewhereOnlyOnce()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, string zone) = SetupNoClue(m, 110f, 100f); // one sighting elsewhere proves nothing
            string elsewhere = zone == "depot" ? "market" : "depot";
            byte accuser = OtherSeat(mem, seen);
            MeetingLog log = NewLog(m);
            log.Add(Say(accuser, "saw_dump", ChatArg.ForSeat(seen), ChatArg.ForZone(elsewhere)), 5f);

            model.ApplyChat(log, mem, Self);

            Assert.Equal(SuspicionModel.VoteThreshold, model.Score(seen));
        }

        [Fact]
        public void Eyewitness_WithNoCluePile_Counts_WhenTheTwoOwnSightingsSpanLessThan10s()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, string zone) = SetupNoClue(m, 110f, 100f, 106f);
            string elsewhere = zone == "depot" ? "market" : "depot";
            byte accuser = OtherSeat(mem, seen);
            MeetingLog log = NewLog(m);
            log.Add(Say(accuser, "saw_dump", ChatArg.ForSeat(seen), ChatArg.ForZone(elsewhere)), 5f);

            model.ApplyChat(log, mem, Self);

            Assert.Equal(SuspicionModel.VoteThreshold, model.Score(seen));
        }

        [Fact]
        public void Eyewitness_WithNoCluePile_Counts_WhenTheBotSawTheAccusedInTheClaimedZone()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, string zone) = SetupNoClue(m, 115f, 100f, 112f);
            byte accuser = OtherSeat(mem, seen);
            MeetingLog log = NewLog(m);
            log.Add(Say(accuser, "saw_dump", ChatArg.ForSeat(seen), ChatArg.ForZone(zone)), 5f);

            model.ApplyChat(log, mem, Self);

            Assert.Equal(SuspicionModel.VoteThreshold, model.Score(seen));
        }

        [Fact]
        public void Eyewitness_WithNoCluePile_Counts_WhenTheOwnSightingIsOlderThanTheWindow()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, string zone) = SetupNoClue(m, 100f + SuspicionModel.NearPileWindowS + 30f, 100f, 112f); // both sightings long before the meeting
            string elsewhere = zone == "depot" ? "market" : "depot";
            byte accuser = OtherSeat(mem, seen);
            MeetingLog log = NewLog(m);
            log.Add(Say(accuser, "saw_dump", ChatArg.ForSeat(seen), ChatArg.ForZone(elsewhere)), 5f);

            model.ApplyChat(log, mem, Self);

            Assert.Equal(SuspicionModel.VoteThreshold, model.Score(seen));
        }

        [Fact]
        public void Eyewitness_FromAnAccuserTheBotAlreadySuspects_IsHearsay()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, _, _) = Setup(m, withClue: true); // `seen` scores 30 (near a pile)
            byte target = OtherSeat(mem, seen);
            MeetingLog log = NewLog(m);
            log.Add(Say(seen, "saw_dump", ChatArg.ForSeat(target), ChatArg.ForZone("depot")), 5f);

            model.ApplyChat(log, mem, Self);

            Assert.Equal(SuspicionModel.HearsayWeight, model.Score(target));
        }

        [Fact]
        public void Eyewitness_IsHearsay_WhenAnUnsuspectedPlayerSpeaksForTheAccused()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, _, _) = Setup(m, withClue: false);
            byte accuser = seen;
            byte accused = OtherSeat(mem, seen);
            byte defender = OtherSeat(mem, seen, accused);
            MeetingLog log = NewLog(m);
            log.Add(Say(accuser, "saw_dump", ChatArg.ForSeat(accused), ChatArg.ForZone("depot")), 5f);
            log.Add(Say(defender, "with", ChatArg.ForSeat(accused), ChatArg.ForZone("market")), 6f);

            model.ApplyChat(log, mem, Self);

            Assert.Equal(SuspicionModel.HearsayWeight, model.Score(accused));
        }

        [Fact]
        public void Eyewitness_StillCounts_WhenTheDefenderIsSuspectedToo()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, _, _) = Setup(m, withClue: true); // `seen` scores 30
            byte accuser = OtherSeat(mem, seen);
            byte accused = OtherSeat(mem, seen, accuser);
            MeetingLog log = NewLog(m);
            log.Add(Say(accuser, "saw_dump", ChatArg.ForSeat(accused), ChatArg.ForZone("depot")), 5f);
            log.Add(Say(seen, "with", ChatArg.ForSeat(accused), ChatArg.ForZone("market")), 6f); // a suspect vouching: no help

            model.ApplyChat(log, mem, Self);

            Assert.Equal(SuspicionModel.VoteThreshold, model.Score(accused));
        }
    }
}
