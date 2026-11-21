using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Xunit;
using static Sim.Tests.TestWorld;

namespace Sim.Tests
{
    public class ChatTests
    {
        private static readonly string[] ExpectedIds =
        {
            "sus", "saw_dump", "saw_hit", "near_pile", "i_was", "with", "where",
            "agree", "doubt", "not_me", "lying", "skip_no_proof", "vote", "hmm", "idk",
        };

        private static string ShippedFile()
        {
            string? dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "content", "phrases.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new FileNotFoundException("content/phrases.json not found above " + AppContext.BaseDirectory);
        }

        private static PhraseCatalogue Shipped() => PhraseCatalogue.Parse(File.ReadAllText(ShippedFile()));

        /// <summary>An engine with the shipped catalogue, in the discussion stage of a meeting.</summary>
        private static (MatchEngine e, byte cit) InMeeting()
        {
            var cfg = new MatchConfig { DiscussionS = 30f, VotingS = 30f, ResultsS = 5f, EmergencyMinSinceMeetingS = 0f };
            var e = new MatchEngine(cfg, Map(), Seats(cfg.Seats), TaskTypes(), new XorShiftRandom(42), Shipped());
            e.Tick(e.Config.IntroS);
            byte cit = Citizen(e);
            e.TestTeleport(cit, ButtonPos);
            Assert.True(e.Apply(cit, new CallMeetingCommand()).IsOk);
            Assert.Equal(MeetingStage.Discussion, e.Meeting!.Stage);
            return (e, cit);
        }

        [Fact]
        public void Chat_AcceptsACatalogueIdWithMatchingSlots()
        {
            (MatchEngine e, byte cit) = InMeeting();
            CommandResult r = e.Apply(cit, new ChatPhraseCommand("saw_dump", new[] { ChatArg.ForSeat(2), ChatArg.ForZone("west") }));
            Assert.True(r.IsOk);
            Assert.Contains(e.Tick(0f), ev => ev is ChatEvent c && c.PhraseId == "saw_dump" && c.Seat == cit);
        }

        [Fact]
        public void Chat_RejectsWrongSlotTypesOrCount()
        {
            (MatchEngine e, byte cit) = InMeeting();
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(cit, new ChatPhraseCommand("saw_dump", new[] { ChatArg.ForZone("west"), ChatArg.ForSeat(2) })).Reason);
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(cit, new ChatPhraseCommand("saw_dump", new[] { ChatArg.ForSeat(2) })).Reason);
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(cit, new ChatPhraseCommand("not_me", new[] { ChatArg.ForSeat(2) })).Reason);
        }

        [Fact]
        public void Chat_RejectsUnknownId()
        {
            (MatchEngine e, byte cit) = InMeeting();
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(cit, new ChatPhraseCommand("free_text", null)).Reason);
        }

        [Fact]
        public void Chat_RejectsASeatThatIsOut()
        {
            var cfg = new MatchConfig { DiscussionS = 30f, VotingS = 30f, ResultsS = 5f, EmergencyMinSinceMeetingS = 0f };
            var e = new MatchEngine(cfg, Map(), Seats(cfg.Seats), TaskTypes(), new XorShiftRandom(42), Shipped());
            e.Tick(e.Config.IntroS);
            byte pol = Polluter(e);
            byte pol2 = Polluter(e, 1);
            byte victim = Citizen(e);
            byte caller = Citizen(e, 1);
            ParkOthers(e, pol, pol2, victim);
            e.TestTeleport(pol, new Vec2(3.5f, 6.5f));
            e.TestTeleport(victim, new Vec2(4.3f, 6.5f));
            e.TestTeleport(pol2, new Vec2(4.3f, 7.3f));
            Advance(e, 10.2f);
            Assert.True(e.Apply(pol, new HitCommand(victim)).IsOk);
            Advance(e, 25.2f);
            Assert.True(e.Apply(pol2, new HitCommand(victim)).IsOk);
            Advance(e, 25.2f);
            Assert.True(e.Apply(pol, new HitCommand(victim)).IsOk);
            Assert.False(e.Players.First(p => p.Seat == victim).IsActive);

            e.TestTeleport(caller, ButtonPos);
            Assert.True(e.Apply(caller, new CallMeetingCommand()).IsOk);
            Assert.Equal(MeetingStage.Discussion, e.Meeting!.Stage);

            Assert.Equal(RejectReason.TargetInvalid, e.Apply(caller, new ChatPhraseCommand("sus", new[] { ChatArg.ForSeat(victim) })).Reason);
            Assert.Equal(0, e.ViewFor(caller).Meeting!.MyChatCount); // a rejected line is not counted
            Assert.True(e.Apply(caller, new ChatPhraseCommand("sus", new[] { ChatArg.ForSeat(pol) })).IsOk);
        }

        [Fact]
        public void Chat_OnlyInDiscussion()
        {
            (MatchEngine e, byte cit) = InMeeting();
            Advance(e, 30.5f);
            Assert.Equal(MeetingStage.Voting, e.Meeting!.Stage);
            Assert.Equal(RejectReason.WrongPhase, e.Apply(cit, new ChatPhraseCommand("hmm", null)).Reason);
        }

        [Fact]
        public void Chat_LimitIsFivePerMeeting()
        {
            (MatchEngine e, byte cit) = InMeeting();
            for (int i = 0; i < 5; i++)
            {
                Assert.True(e.Apply(cit, new ChatPhraseCommand("hmm", null)).IsOk);
                e.Tick(2f); // the gap between chat lines
            }

            Assert.Equal(RejectReason.PhraseLimit, e.Apply(cit, new ChatPhraseCommand("hmm", null)).Reason);
        }

        [Fact]
        public void View_CarriesMyChatCountForARejoiningSeat()
        {
            (MatchEngine e, byte cit) = InMeeting();
            Assert.Equal(0, e.ViewFor(cit).Meeting!.MyChatCount);
            Assert.True(e.Apply(cit, new ChatPhraseCommand("hmm", null)).IsOk);
            e.Tick(2f); // the gap between chat lines
            Assert.True(e.Apply(cit, new ChatPhraseCommand("idk", null)).IsOk);
            Assert.Equal(2, e.ViewFor(cit).Meeting!.MyChatCount);
            byte other = cit == 0 ? (byte)1 : (byte)0;
            Assert.Equal(0, e.ViewFor(other).Meeting!.MyChatCount);
        }

        [Fact]
        public void Meeting_StartedEventAndViewCarryTheLivingSeats()
        {
            (MatchEngine e, byte cit) = InMeeting();
            var expected = new List<byte>();
            foreach (PlayerState p in e.Players)
            {
                expected.Add(p.Seat);
            }

            MeetingStartedEvent started = Assert.Single(e.Tick(0f).OfType<MeetingStartedEvent>());
            Assert.Equal(expected, started.Living);
            Assert.Equal(expected, e.ViewFor(cit).Meeting!.Living);
        }

        [Fact]
        public void Catalogue_ParsesTheShippedFile()
        {
            PhraseCatalogue c = Shipped();
            var ids = new List<string>();
            foreach (PhraseInfo p in c.All)
            {
                ids.Add(p.Id);
            }

            Assert.Equal(ExpectedIds, ids);
            Assert.True(c.TryGet("saw_dump", out PhraseInfo sawDump));
            Assert.Equal("accuse", sawDump.Kind);
            Assert.Equal(new[] { ChatArgKind.Seat, ChatArgKind.Zone }, sawDump.Slots);
            Assert.True(c.TryGet("i_was", out PhraseInfo iWas));
            Assert.Equal("alibi", iWas.Kind);
            Assert.Equal(new[] { ChatArgKind.Zone }, iWas.Slots);
            Assert.True(c.TryGet("not_me", out PhraseInfo notMe));
            Assert.Empty(notMe.Slots);
            Assert.True(c.TryGet("idk", out PhraseInfo idk));
            Assert.Equal("filler", idk.Kind);
            Assert.False(c.TryGet("nope", out _));
        }
    }
}
