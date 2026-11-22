using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots.Meeting;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// Bots do not repeat a line: a phrase with the same id and slots as one already said in this meeting
    /// (by anyone) is replaced by agree, where, the bot's own evidence line or its vote, else the bot stays quiet.
    /// </summary>
    public class ChatVarietyTests
    {
        private static readonly string[] MapZones = { "depot", "market", "garden" };

        private static MeetingState State(
            Role role = Role.Citizen,
            byte self = 0,
            IEnumerable<ChatAct>? chat = null,
            IEnumerable<EvidenceItem>? evidence = null,
            Dictionary<byte, float>? suspicion = null,
            byte[]? partners = null)
        {
            return new MeetingState
            {
                Self = self,
                Role = role,
                Partners = partners ?? new byte[0],
                Living = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 },
                Stage = MeetingStage.Discussion,
                StageLeft = 30f,
                Evidence = (evidence ?? new EvidenceItem[0]).ToList(),
                Chat = (chat ?? new ChatAct[0]).ToList(),
                Suspicion = suspicion ?? new Dictionary<byte, float>(),
                Heat = new Dictionary<byte, float>(),
                Zones = MapZones,
            };
        }

        private static ChatAct Act(byte speaker, string id, string kind, byte? seat = null, string? zone = null) => new ChatAct(speaker, id, kind, seat, zone, 10f);

        private static ChatAct Sus(byte speaker, byte seat) => Act(speaker, "sus", "accuse", seat);

        private static readonly Dictionary<byte, float> Doubtful5 = new Dictionary<byte, float> { { 5, 20f } };

        [Fact]
        public void RepeatedAccusation_BecomesAgreeWithTheAccuser()
        {
            MeetingState s = State(chat: new[] { Sus(2, 5) }, suspicion: Doubtful5);

            MeetingDecision g = Grounding.Check(MeetingDecision.Say("fake", "sus", 5), s);

            Assert.True(g.Speak);
            Assert.Equal("agree", g.PhraseId);
            Assert.Equal((byte?)2, g.Seat);
        }

        [Fact]
        public void RepeatedAccusation_TurnedIntoAgreeOrWhere_StillAnnouncesTheVote()
        {
            MeetingState s = State(chat: new[] { Sus(2, 5) }, suspicion: Doubtful5);
            MeetingDecision agree = Grounding.Check(MeetingDecision.Say("fake", "sus", 5), s);
            Assert.Equal("agree", agree.PhraseId);
            Assert.Equal((byte?)5, agree.Announces);

            MeetingState asked = State(chat: new[] { Sus(2, 5), Act(3, "agree", "react", 2) }, suspicion: Doubtful5);
            MeetingDecision where = Grounding.Check(MeetingDecision.Say("fake", "sus", 5), asked);
            Assert.Equal("where", where.PhraseId);
            Assert.Equal((byte?)5, where.Announces);

            Assert.Null(Grounding.Check(MeetingDecision.Say("fake", "sus", 4), State(suspicion: new Dictionary<byte, float> { { 4, 20f } })).Announces);
        }

        [Fact]
        public void RepeatedAccusation_WhenAgreeWasSaid_AsksWhere_ThenSaysItsVote_ThenStaysQuiet()
        {
            ChatAct accusation = Sus(2, 5);
            ChatAct agreed = Act(3, "agree", "react", 2);
            ChatAct where = Act(4, "where", "question", 5);
            ChatAct vote = Act(6, "vote", "vote", 5);

            MeetingDecision g1 = Grounding.Check(MeetingDecision.Say("fake", "sus", 5), State(chat: new[] { accusation, agreed }, suspicion: Doubtful5));
            Assert.Equal("where", g1.PhraseId);
            Assert.Equal((byte?)5, g1.Seat);

            MeetingDecision g2 = Grounding.Check(MeetingDecision.Say("fake", "sus", 5), State(chat: new[] { accusation, agreed, where }, suspicion: Doubtful5));
            Assert.Equal("vote", g2.PhraseId);
            Assert.Equal((byte?)5, g2.Seat);

            MeetingDecision g3 = Grounding.Check(MeetingDecision.Say("fake", "sus", 5), State(chat: new[] { accusation, agreed, where, vote }, suspicion: Doubtful5));
            Assert.False(g3.Speak);
        }

        [Fact]
        public void RepeatedAccusation_WithOwnDifferentEvidence_SaysItsOwnLine()
        {
            MeetingState s = State(
                chat: new[] { Sus(2, 5), Act(3, "near_pile", "accuse", 5, "market") },
                evidence: new[] { new EvidenceItem(EvidenceItem.SawDump, 5, "depot", 50f) },
                suspicion: new Dictionary<byte, float> { { 5, 100f } });

            MeetingDecision g = Grounding.Check(MeetingDecision.Say("fake", "sus", 5), s);

            Assert.Equal("saw_dump", g.PhraseId);
            Assert.Equal((byte?)5, g.Seat);
            Assert.Equal("depot", g.Zone);
        }

        [Fact]
        public void RepeatedEyewitnessLine_IsNotSaidTwice_ButAnotherZoneIsADifferentLine()
        {
            var evidence = new[]
            {
                new EvidenceItem(EvidenceItem.SawDump, 5, "depot", 50f),
            };
            var chat = new[] { Act(2, "saw_dump", "accuse", 5, "depot") };

            MeetingDecision same = Grounding.Check(MeetingDecision.Say("fake", "saw_dump", 5, "depot"), State(chat: chat, evidence: evidence, suspicion: new Dictionary<byte, float> { { 5, 100f } }));
            Assert.Equal("agree", same.PhraseId);
            Assert.Equal((byte?)2, same.Seat);

            var otherZone = new[] { Act(2, "saw_dump", "accuse", 5, "market") };
            MeetingDecision other = Grounding.Check(MeetingDecision.Say("fake", "saw_dump", 5, "depot"), State(chat: otherZone, evidence: evidence, suspicion: new Dictionary<byte, float> { { 5, 100f } }));
            Assert.Equal("saw_dump", other.PhraseId);
            Assert.Equal("depot", other.Zone);
        }

        [Fact]
        public void AFirstLine_IsUnchanged()
        {
            MeetingState s = State(chat: new[] { Sus(2, 4) }, suspicion: Doubtful5);

            MeetingDecision g = Grounding.Check(MeetingDecision.Say("fake", "sus", 5), s);

            Assert.Equal("sus", g.PhraseId);
            Assert.Equal((byte?)5, g.Seat);
        }

        [Fact]
        public void PolluterWhoAgrees_NeverAgreesWithAnAccuserOfItsPartner()
        {
            // seat 2 accused the partner (seat 1) and nobody else: agreeing with it would turn on the partner
            MeetingState s = State(Role.Polluter, partners: new byte[] { 1 }, chat: new[] { Sus(2, 5), Sus(3, 1) });

            MeetingDecision g = Grounding.Check(MeetingDecision.Say("fake", "sus", 5), s);

            Assert.Equal("agree", g.PhraseId);
            Assert.NotEqual((byte?)3, g.Seat);
        }

        private static readonly Dictionary<string, string> KindOf = new Dictionary<string, string>
        {
            { "sus", "accuse" }, { "saw_dump", "accuse" }, { "saw_hit", "accuse" }, { "near_pile", "accuse" }, { "i_was", "alibi" },
            { "with", "alibi" }, { "where", "question" }, { "agree", "react" }, { "doubt", "react" }, { "not_me", "react" },
            { "lying", "react" }, { "vote", "vote" }, { "hmm", "filler" }, { "idk", "filler" }, { "skip_no_proof", "vote" },
        };

        /// <summary>What the bot at seat 0 says in successive rounds: the rule decider's pick, grounded, appended to the chat each time (quiet rounds are listed as "-").</summary>
        private static List<string> Rounds(Role role, IEnumerable<ChatAct> chat, IEnumerable<EvidenceItem> evidence, Dictionary<byte, float> suspicion, int rounds, byte[]? partners = null)
        {
            var said = chat.ToList();
            var result = new List<string>();
            for (int i = 0; i < rounds; i++)
            {
                MeetingState s = State(role, chat: said, evidence: evidence, suspicion: suspicion, partners: partners);
                MeetingDecision g = Grounding.Check(RuleDecider.Decide(s), s);
                if (!g.Speak)
                {
                    result.Add("-");
                    continue;
                }

                result.Add(g.PhraseId + (g.Seat != null ? " " + g.Seat : "") + (g.Zone != null ? " " + g.Zone : ""));
                said.Add(new ChatAct(0, g.PhraseId!, KindOf[g.PhraseId!], g.Seat, g.Zone, 20f));
            }

            return result;
        }

        [Fact]
        public void AccusedEyewitnessCitizen_AfterSomeoneElsesDenial_StillSaysItsOwnLine()
        {
            var evidence = new[]
            {
                new EvidenceItem(EvidenceItem.SawDump, 5, "depot", 50f),
                new EvidenceItem(EvidenceItem.MyZone, null, "garden", 40f),
            };
            var chat = new[] { Sus(2, 0), Act(1, "not_me", "react"), Act(1, "i_was", "alibi", null, "garden") };

            List<string> said = Rounds(Role.Citizen, chat, evidence, new Dictionary<byte, float> { { 5, 100f } }, 4);

            Assert.Equal(new[] { "not_me", "i_was garden", "saw_dump 5 depot", "-" }, said);
        }

        [Fact]
        public void AccusedPolluter_AfterACitizensAlibi_StillGivesItsOwnAlibi_NeverSilence()
        {
            var evidence = new[] { new EvidenceItem(EvidenceItem.MyZone, null, "garden", 40f) };
            var chat = new[] { Sus(2, 0), Act(1, "not_me", "react"), Act(1, "i_was", "alibi", null, "depot") };

            List<string> said = Rounds(Role.Polluter, chat, evidence, new Dictionary<byte, float>(), 3, new byte[] { 7 });

            Assert.Equal("not_me", said[0]);
            Assert.StartsWith("i_was ", said[1]); // a lie, in a zone the citizen named too: both are first-person statements
            Assert.NotEqual("-", said[1]);
        }

        [Fact]
        public void TwoBotsWhoWereBothInTheDepot_BothSayIWasDepot()
        {
            var evidence = new[] { new EvidenceItem(EvidenceItem.MyZone, null, "depot", 40f) };
            MeetingState s = State(chat: new[] { Act(2, "i_was", "alibi", null, "depot") }, evidence: evidence);

            MeetingDecision g = Grounding.Check(MeetingDecision.Say("fake", "i_was", null, "depot"), s);

            Assert.Equal("i_was", g.PhraseId);
            Assert.Equal("depot", g.Zone);
            Assert.False(Grounding.IsRepeat(s.Chat, 0, "i_was", null, "depot"));
            Assert.True(Grounding.IsRepeat(new[] { Act(0, "i_was", "alibi", null, "depot") }, 0, "i_was", null, "depot"));
        }

        [Fact]
        public void RepeatedFiller_StaysQuiet()
        {
            MeetingState s = State(chat: new[] { Act(2, "hmm", "filler") });

            Assert.False(Grounding.Check(MeetingDecision.Say("fake", "hmm"), s).Speak);
            Assert.True(Grounding.Check(MeetingDecision.Say("fake", "idk"), s).Speak);
        }

        [Fact]
        public void SeededMeetings_NeverHaveTheSameLineTwice()
        {
            string dir = Uncycled.Server.Content.ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
            Uncycled.Server.Content.ContentBundle content = Uncycled.Server.Content.ContentLoader.Load(dir);
            var options = Uncycled.Train.HostOptions.Parse(new[] { "--eval", "--content", dir, "--seeds", "1-10", "--threads", "2" });

            IReadOnlyList<string> lines = Uncycled.Train.EvalRunner.Run(options, content, Uncycled.Train.HostBrains.Scripted(new BotsSettings(), content));

            int meetings = 0;
            int chat = 0;
            int identical = 0;
            foreach (string line in lines)
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(line))
                {
                    meetings += doc.RootElement.GetProperty("meetings").GetInt32();
                    chat += doc.RootElement.GetProperty("chat_lines").GetInt32();
                    identical += doc.RootElement.GetProperty("identical_lines").GetInt32();
                }
            }

            Assert.True(meetings > 0, "no meeting in 10 matches: the test checks nothing");
            Assert.True(chat > 0, "the meetings had no chat line");
            Assert.Equal(0, identical);
        }
    }
}
