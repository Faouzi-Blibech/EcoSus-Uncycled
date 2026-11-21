using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Bots.Meeting;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public class MeetingLogTests
    {
        private const byte Self = 0;

        private static ChatEvent Say(byte seat, string id, params ChatArg[] args) => new ChatEvent(seat, id, args);

        private static MeetingLog NewLog(TestMatch m) => new MeetingLog(m.Content.Phrases!);

        /// <summary>
        /// Bot 0 saw the first visible player in his zone at t=100; the meeting has one clue pile in
        /// that zone at t=110 (inside NearPileWindowS). Returns the model after Update and the seat seen.
        /// </summary>
        private static (SuspicionModel model, BotMemory mem, byte seen, string zone) Setup(TestMatch m, bool withClue)
        {
            SeatView live = m.Engine.ViewFor(Self);
            PlayerView seenPlayer = live.Players[0];
            string zone = m.Map.ZoneAt(seenPlayer.Position);

            var mem = new BotMemory(m.Map, m.Config);
            mem.Observe(live, 100f, new List<WitnessRecord>());

            var clues = new List<ClueInfo>();
            if (withClue)
            {
                clues.Add(new ClueInfo(1, PileKind.Dump, zone, 110f, WasteType.Glass, null));
            }

            var meeting = new MeetingView(MeetingStage.Discussion, 30f, 0, MeetingCause.Report, clues, new List<byte>(), false, null, 0f);
            var view = new SeatView(Self, MatchPhase.Meeting, 30f, 130f, live.DeadlineS, live.DeadlineClock, 130f, 10f, 2, live.Me, new List<PlayerView>(), new List<PileView>(), null, meeting, null);
            var model = new SuspicionModel();
            model.Update(mem, view);
            return (model, mem, seenPlayer.Seat, zone);
        }

        private static byte OtherSeat(params byte[] taken)
        {
            for (byte s = 1; s < 8; s++)
            {
                if (System.Array.IndexOf(taken, s) < 0)
                {
                    return s;
                }
            }

            throw new System.InvalidOperationException();
        }

        [Fact]
        public void Log_ParsesSlotsIntoActs()
        {
            TestMatch m = TestMatch.Create();
            MeetingLog log = NewLog(m);
            log.Add(Say(2, "saw_dump", ChatArg.ForSeat(3), ChatArg.ForZone("depot")), 12f);
            log.Add(Say(4, "not_me"), 15f);
            log.Add(Say(5, "with", ChatArg.ForSeat(6), ChatArg.ForZone("market")), 18f);
            log.Add(Say(5, "no_such_phrase"), 19f);

            Assert.Equal(3, log.Acts.Count);
            ChatAct a = log.Acts[0];
            Assert.Equal((byte)2, a.Speaker);
            Assert.Equal("saw_dump", a.PhraseId);
            Assert.Equal("accuse", a.Kind);
            Assert.Equal((byte?)3, a.Seat);
            Assert.Equal("depot", a.Zone);
            Assert.Equal(12f, a.Time);
            Assert.Null(log.Acts[1].Seat);
            Assert.Null(log.Acts[1].Zone);
            Assert.Equal("react", log.Acts[1].Kind);
            Assert.Equal((byte?)6, log.Acts[2].Seat);
            Assert.Equal("market", log.Acts[2].Zone);

            log.Clear();
            Assert.Empty(log.Acts);
        }

        [Fact]
        public void Hearsay_OneAccusationStaysBelowTheVoteThreshold()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, _) = Setup(m, withClue: false);
            byte target = OtherSeat(seen);
            MeetingLog log = NewLog(m);
            log.Add(Say(seen, "sus", ChatArg.ForSeat(target)), 5f);
            log.Add(Say(seen, "saw_hit", ChatArg.ForSeat(target), ChatArg.ForZone("depot")), 6f);

            model.ApplyChat(log, mem, Self);

            Assert.Equal(SuspicionModel.HearsayWeight, model.Score(target));
            Assert.True(model.Score(target) < SuspicionModel.VoteThreshold);
            Assert.Null(model.Vote(m.Engine.ViewFor(Self), Role.Citizen, new List<byte>()));

            // applying the same log again must not stack
            model.ApplyChat(log, mem, Self);
            Assert.Equal(SuspicionModel.HearsayWeight, model.Score(target));
        }

        [Fact]
        public void Hearsay_TwoAccusersPlusNearPileCrossesIt()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, _) = Setup(m, withClue: true);
            byte a1 = OtherSeat(seen);
            byte a2 = OtherSeat(seen, a1);
            MeetingLog log = NewLog(m);
            log.Add(Say(a1, "sus", ChatArg.ForSeat(seen)), 5f);
            log.Add(Say(a2, "near_pile", ChatArg.ForSeat(seen), ChatArg.ForZone("depot")), 6f);

            model.ApplyChat(log, mem, Self);

            Assert.Equal(SuspicionModel.NearPileWeight + 2 * SuspicionModel.HearsayWeight, model.Score(seen));
            Assert.Equal((byte?)seen, model.Vote(m.Engine.ViewFor(Self), Role.Citizen, new List<byte>()));
        }

        [Fact]
        public void Contradiction_AlibiAgainstOwnSightingRaisesTheSpeaker()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, string zone) = Setup(m, withClue: true);
            string other = zone == "depot" ? "market" : "depot";

            // the alibi names the zone the bot really saw him in: no contradiction
            var honest = NewLog(m);
            honest.Add(Say(seen, "i_was", ChatArg.ForZone(zone)), 8f);
            model.ApplyChat(honest, mem, Self);
            Assert.Equal(SuspicionModel.NearPileWeight, model.Score(seen));

            // a different zone contradicts the bot's own sighting
            var liar = NewLog(m);
            liar.Add(Say(seen, "i_was", ChatArg.ForZone(other)), 8f);
            model.ApplyChat(liar, mem, Self);
            Assert.Equal(SuspicionModel.NearPileWeight + SuspicionModel.ContradictionWeight, model.Score(seen));
        }

        [Fact]
        public void Alibi_AnHonestRuleAlibiNeverRaisesTheSpeakerInAnotherBotsModel()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, string zone) = Setup(m, withClue: true);
            string elsewhere = zone == "depot" ? "market" : "depot";

            // the speaker was in `zone` at the clue time (bot 0 saw it there), then walked to `elsewhere` before the meeting
            MeetingState speaker = new MeetingState
            {
                Self = seen,
                Role = Role.Citizen,
                Living = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 },
                Stage = MeetingStage.Discussion,
                Clues = new List<ClueInfo> { new ClueInfo(1, PileKind.Dump, zone, 110f, WasteType.Glass, null) },
                Zones = new[] { "depot", "market", "garden" },
                Evidence = new List<EvidenceItem>
                {
                    new EvidenceItem(EvidenceItem.MyZone, null, zone, 100f),
                    new EvidenceItem(EvidenceItem.MyZone, null, elsewhere, 150f),
                },
                Chat = new List<ChatAct>
                {
                    new ChatAct(0, "sus", "accuse", seen, null, 8f),
                    new ChatAct(seen, "not_me", "react", null, null, 9f),
                },
            };

            MeetingDecision alibi = RuleDecider.Decide(speaker);
            Assert.Equal("i_was", alibi.PhraseId);
            Assert.Equal(zone, alibi.Zone);
            Assert.Equal(zone, Grounding.Check(MeetingDecision.Say("fake", "i_was", null, elsewhere), speaker).Zone); // a model's later zone is replaced too

            var log = NewLog(m);
            log.Add(Say(seen, "i_was", ChatArg.ForZone(alibi.Zone!)), 12f);
            model.ApplyChat(log, mem, Self);
            Assert.Equal(SuspicionModel.NearPileWeight, model.Score(seen));
        }

        [Fact]
        public void Memory_OwnZonesAreRecordedOnlyWhilePlaying()
        {
            TestMatch m = TestMatch.Create();
            SeatView live = m.Engine.ViewFor(Self);
            var mem = new BotMemory(m.Map, m.Config);
            mem.Observe(live, 100f, new List<WitnessRecord>());
            int playing = mem.OwnSightings.Count;
            Assert.Equal(1, playing);

            var meeting = new MeetingView(MeetingStage.Discussion, 30f, 0, MeetingCause.Report, new List<ClueInfo>(), new List<byte>(), false, null, 0f);
            for (int i = 1; i <= 100; i++)
            {
                var view = new SeatView(Self, MatchPhase.Meeting, 30f, 100f + i, live.DeadlineS, live.DeadlineClock, 100f + i, 10f, 2, live.Me, new List<PlayerView>(), new List<PileView>(), null, meeting, null);
                mem.Observe(view, 100f + i, new List<WitnessRecord>());
            }

            Assert.Equal(playing, mem.OwnSightings.Count);
            Assert.Equal(100f, mem.OwnSightings[0].Time);
        }

        [Fact]
        public void Hearsay_IgnoresAccusationsOfSelf()
        {
            TestMatch m = TestMatch.Create();
            (SuspicionModel model, BotMemory mem, byte seen, _) = Setup(m, withClue: false);
            MeetingLog log = NewLog(m);
            log.Add(Say(seen, "sus", ChatArg.ForSeat(Self)), 5f);
            log.Add(Say(OtherSeat(seen), "saw_dump", ChatArg.ForSeat(Self), ChatArg.ForZone("depot")), 6f);
            // and the bot's own accusation of someone else is not hearsay either
            log.Add(Say(Self, "sus", ChatArg.ForSeat(seen)), 7f);

            model.ApplyChat(log, mem, Self);

            Assert.Equal(0f, model.Score(Self));
            Assert.Equal(0f, model.Score(seen));
        }

        [Fact]
        public void Heat_CountsChatAccusations()
        {
            TestMatch m = TestMatch.Create();
            MeetingLog log = NewLog(m);
            log.Add(Say(1, "sus", ChatArg.ForSeat(5)), 1f);
            log.Add(Say(2, "saw_dump", ChatArg.ForSeat(5), ChatArg.ForZone("depot")), 2f);
            log.Add(Say(3, "near_pile", ChatArg.ForSeat(5), ChatArg.ForZone("depot")), 3f);
            log.Add(Say(4, "saw_hit", ChatArg.ForSeat(6), ChatArg.ForZone("depot")), 4f);
            log.Add(Say(1, "sus", ChatArg.ForSeat(5)), 5f); // same accuser again
            log.Add(Say(6, "agree", ChatArg.ForSeat(5)), 6f); // a reaction is not an accusation
            log.Add(Say(7, "where", ChatArg.ForSeat(5)), 7f);

            Assert.Equal(3, log.AccusationsAgainst(5));
            Assert.Equal(1, log.AccusationsAgainst(6));
            Assert.Equal(0, log.AccusationsAgainst(7));
            Assert.True(log.WasAccusedBy(5, 2));
            Assert.False(log.WasAccusedBy(5, 6));
            Assert.False(log.WasAccusedBy(6, 2));
        }
    }
}
