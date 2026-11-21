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
    public class ChatTextTests
    {
        private const string FilterJson = "{\"version\":1,\"words\":{\"en\":[\"fuck\",\"shit\",\"ass\",\"cafe\"]}}";

        private static PhraseCatalogue Shipped()
        {
            string? dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "content", "phrases.json");
                if (File.Exists(candidate))
                {
                    return PhraseCatalogue.Parse(File.ReadAllText(candidate));
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new FileNotFoundException("content/phrases.json not found above " + AppContext.BaseDirectory);
        }

        private static MatchEngine Playing8(ChatFilter? filter = null)
        {
            var cfg = new MatchConfig { DiscussionS = 30f, VotingS = 30f, ResultsS = 5f, EmergencyMinSinceMeetingS = 0f };
            var e = new MatchEngine(cfg, Map(), Seats(cfg.Seats), TaskTypes(), new XorShiftRandom(42), Shipped(), filter ?? ChatFilter.Empty);
            e.Tick(e.Config.IntroS);
            return e;
        }

        private static (MatchEngine e, byte cit) InMeeting(ChatFilter? filter = null)
        {
            MatchEngine e = Playing8(filter);
            byte cit = Citizen(e);
            e.TestTeleport(cit, ButtonPos);
            Assert.True(e.Apply(cit, new CallMeetingCommand()).IsOk);
            Assert.Equal(MeetingStage.Discussion, e.Meeting!.Stage);
            return (e, cit);
        }

        /// <summary>Puts two citizens out directly (the poison route is covered in ChatTests).</summary>
        private static (byte out1, byte out2, byte living) TwoOut(MatchEngine e)
        {
            byte o1 = Citizen(e, 0);
            byte o2 = Citizen(e, 1);
            byte living = Citizen(e, 2);
            e.Players.First(p => p.Seat == o1).Status = PlayerStatus.Out;
            e.Players.First(p => p.Seat == o2).Status = PlayerStatus.Out;
            return (o1, o2, living);
        }

        private static CommandResult Say(MatchEngine e, byte seat, string text) => e.Apply(seat, new ChatTextCommand(text));

        [Fact]
        public void Text_AcceptedInDiscussion()
        {
            (MatchEngine e, byte cit) = InMeeting();
            Assert.True(Say(e, cit, "  I was   at the west bins ").IsOk);
            ChatEvent ev = Assert.Single(e.Tick(0f).OfType<ChatEvent>());
            Assert.Equal(cit, ev.Seat);
            Assert.Equal("I was at the west bins", ev.Text);
            Assert.Null(ev.PhraseId);
            Assert.False(ev.Ghost);
            Assert.Null(ev.Recipients);
        }

        [Fact]
        public void Text_RejectedOutsideDiscussion()
        {
            MatchEngine playing = Playing8();
            Assert.Equal(RejectReason.WrongPhase, Say(playing, Citizen(playing), "hello").Reason);

            (MatchEngine e, byte cit) = InMeeting();
            Advance(e, 30.5f);
            Assert.Equal(MeetingStage.Voting, e.Meeting!.Stage);
            Assert.Equal(RejectReason.WrongPhase, Say(e, cit, "hello").Reason);
        }

        [Fact]
        public void Text_TooLongRejected()
        {
            (MatchEngine e, byte cit) = InMeeting();
            Assert.Equal(RejectReason.TooLong, Say(e, cit, new string('a', 121)).Reason);
            Assert.Equal(0, e.ViewFor(cit).Meeting!.MyText); // a rejected line is not counted
            Assert.True(Say(e, cit, new string('a', 120)).IsOk);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   \t \n ")]
        [InlineData("\u200B\u202E\u0007")]
        public void Text_EmptyAfterNormalizeRejected(string raw)
        {
            (MatchEngine e, byte cit) = InMeeting();
            Assert.Equal(RejectReason.TargetInvalid, Say(e, cit, raw).Reason);
        }

        [Fact]
        public void Text_SixthLineRejected()
        {
            (MatchEngine e, byte cit) = InMeeting();
            for (int i = 0; i < 5; i++)
            {
                Assert.True(Say(e, cit, "line " + i).IsOk);
                e.Tick(2f);
            }

            Assert.Equal(RejectReason.TextLimit, Say(e, cit, "one more").Reason);
        }

        [Fact]
        public void Phrase_SixthRejectedWithPhraseLimit()
        {
            (MatchEngine e, byte cit) = InMeeting();
            for (int i = 0; i < 5; i++)
            {
                Assert.True(e.Apply(cit, new ChatPhraseCommand("hmm", null)).IsOk);
                e.Tick(2f);
            }

            Assert.Equal(RejectReason.PhraseLimit, e.Apply(cit, new ChatPhraseCommand("hmm", null)).Reason);
        }

        [Fact]
        public void Limits_PhrasesAndTextsAreCountedSeparately()
        {
            (MatchEngine e, byte cit) = InMeeting();
            for (int i = 0; i < 5; i++)
            {
                Assert.True(e.Apply(cit, new ChatPhraseCommand("hmm", null)).IsOk);
                e.Tick(2f);
            }

            Assert.True(Say(e, cit, "typed after five phrases").IsOk);
            MeetingView mv = e.ViewFor(cit).Meeting!;
            Assert.Equal(5, mv.MyChatCount);
            Assert.Equal(1, mv.MyText);
        }

        [Fact]
        public void Chat_GapAppliesToPhrasesAndText()
        {
            (MatchEngine e, byte cit) = InMeeting();
            Assert.True(Say(e, cit, "first").IsOk);
            e.Tick(1.9f);
            Assert.Equal(RejectReason.RateLimited, Say(e, cit, "second").Reason);
            Assert.Equal(RejectReason.RateLimited, e.Apply(cit, new ChatPhraseCommand("hmm", null)).Reason);
            e.Tick(0.1f);
            Assert.True(e.Apply(cit, new ChatPhraseCommand("hmm", null)).IsOk); // phrase after text, 2.0 s later
            e.Tick(1.9f);
            Assert.Equal(RejectReason.RateLimited, Say(e, cit, "third").Reason); // text after phrase
            e.Tick(0.1f);
            Assert.True(Say(e, cit, "third").IsOk);

            // A rate-limited line is not counted and does not restart the gap.
            Assert.Equal(1, e.ViewFor(cit).Meeting!.MyChatCount);
            Assert.Equal(2, e.ViewFor(cit).Meeting!.MyText);
        }

        [Fact]
        public void Chat_GapIsPerSeat()
        {
            (MatchEngine e, byte cit) = InMeeting();
            byte other = Citizen(e, 1);
            Assert.True(Say(e, cit, "a").IsOk);
            Assert.True(Say(e, other, "b").IsOk);
        }

        [Theory]
        [InlineData("a\u200Bb\u202Ec  d", "abc d")]
        [InlineData("  tab\tand\nnewline\u00A0nbsp  ", "tab and newline nbsp")]
        [InlineData("[b]bold[/b]", "[b]bold[/b]")]
        [InlineData("\uD83D\uDC69\u200D\uD83D\uDCBB works", "\uD83D\uDC69\u200D\uD83D\uDCBB works")]
        [InlineData("a\u200Db", "ab")]
        public void Text_StripsControlAndFormatChars(string raw, string expected)
        {
            Assert.Equal(expected, ChatText.Normalize(raw));
        }

        [Fact]
        public void Text_DropsLoneSurrogates()
        {
            Assert.Equal("lone surrogate", ChatText.Normalize("lone " + (char)0xD83D + " surrogate " + (char)0xDC69));
        }

        [Theory]
        [InlineData("FUCK you", "*** you")]
        [InlineData("Fuuuck!", "***!")]
        [InlineData("sh1t happens", "*** happens")]
        [InlineData("5H1T", "***")]
        [InlineData("a5s", "***")]
        [InlineData("CAF\u00C9 au lait", "*** au lait")]
        [InlineData("cafe\u0301", "***")]
        [InlineData("fuck, shit.", "***, ***.")]
        [InlineData("as class assist passion shift", "as class assist passion shift")]
        [InlineData("fuc k", "fuc k")]
        [InlineData("who killed 1337", "who killed 1337")]
        public void Filter_ReplacesListedWordsCaseAccentsRepeatsDigits(string input, string expected)
        {
            Assert.Equal(expected, ChatFilter.Parse(FilterJson).Apply(input));
        }

        [Fact]
        public void Filter_EmptyChangesNothing()
        {
            Assert.Equal("fuck", ChatFilter.Empty.Apply("fuck"));
        }

        [Fact]
        public void Text_BroadcastIsFilteredAndNormalised()
        {
            (MatchEngine e, byte cit) = InMeeting(ChatFilter.Parse(FilterJson));
            Assert.True(Say(e, cit, "  you   SH1T \u200B").IsOk);
            Assert.Equal("you ***", Assert.Single(e.Tick(0f).OfType<ChatEvent>()).Text);
        }

        [Fact]
        public void Text_LengthIsCheckedBeforeTheFilterShortensIt()
        {
            (MatchEngine e, byte cit) = InMeeting(ChatFilter.Parse(FilterJson));
            string raw = string.Join(" ", Enumerable.Repeat("fuck", 25)); // 124 chars, 75 after the filter
            Assert.Equal(RejectReason.TooLong, Say(e, cit, raw).Reason);
        }

        [Fact]
        public void ChatEvent_OldConstructorStaysAPhraseForEveryone()
        {
            var ev = new ChatEvent(3, "hmm", new List<ChatArg>());
            Assert.Equal("hmm", ev.PhraseId);
            Assert.Null(ev.Text);
            Assert.False(ev.Ghost);
            Assert.Null(ev.Recipients);
            Assert.True(ev.IsFor(0));
        }

        [Fact]
        public void Ghost_OutSeatTextGoesToOutSeatsOnly()
        {
            MatchEngine e = Playing8();
            (byte o1, byte o2, byte living) = TwoOut(e);
            e.Tick(0f);

            Assert.True(Say(e, o1, " boo   hoo ").IsOk);
            ChatEvent ev = Assert.Single(e.Tick(0f).OfType<ChatEvent>());
            Assert.True(ev.Ghost);
            Assert.Equal("boo hoo", ev.Text);
            Assert.Equal(new[] { o1, o2 }.OrderBy(s => s), ev.Recipients!.OrderBy(s => s));
            Assert.True(ev.IsFor(o1));
            Assert.True(ev.IsFor(o2));
            Assert.False(ev.IsFor(living));
            foreach (PlayerState p in e.Players.Where(p => p.IsActive))
            {
                Assert.False(ev.IsFor(p.Seat));
            }
        }

        [Fact]
        public void Ghost_PhraseFromOutSeatIsGhostToo()
        {
            MatchEngine e = Playing8();
            (byte o1, byte o2, byte living) = TwoOut(e);
            Assert.True(e.Apply(o1, new ChatPhraseCommand("sus", new[] { ChatArg.ForSeat(living) })).IsOk);
            ChatEvent ev = Assert.Single(e.Tick(0f).OfType<ChatEvent>());
            Assert.True(ev.Ghost);
            Assert.Equal("sus", ev.PhraseId);
            Assert.False(ev.IsFor(living));
            Assert.True(ev.IsFor(o2));
        }

        [Fact]
        public void Ghost_WorksInMeetingsWithoutThePerMeetingCaps()
        {
            (MatchEngine e, byte cit) = InMeeting();
            byte o1 = Citizen(e, 1);
            e.Players.First(p => p.Seat == o1).Status = PlayerStatus.Out;
            var lines = new List<ChatEvent>();
            for (int i = 0; i < 7; i++)
            {
                Assert.True(Say(e, o1, "ghost " + i).IsOk);
                lines.AddRange(e.Tick(2f).OfType<ChatEvent>());
            }

            Assert.True(e.Apply(o1, new ChatPhraseCommand("hmm", null)).IsOk);
            lines.AddRange(e.Tick(0f).OfType<ChatEvent>());
            Assert.Equal(8, lines.Count);
            Assert.All(lines, ev => Assert.False(ev.IsFor(cit)));
            Assert.Equal(0, e.ViewFor(o1).Meeting!.MyText); // ghost lines are not meeting lines
        }

        [Fact]
        public void Ghost_ObeysFilterGapAndLength()
        {
            MatchEngine e = Playing8(ChatFilter.Parse(FilterJson));
            (byte o1, _, _) = TwoOut(e);
            Assert.True(Say(e, o1, "shit").IsOk);
            Assert.Equal("***", Assert.Single(e.Tick(0f).OfType<ChatEvent>()).Text);
            Assert.Equal(RejectReason.RateLimited, Say(e, o1, "again").Reason);
            e.Tick(2f);
            Assert.Equal(RejectReason.TooLong, Say(e, o1, new string('x', 121)).Reason);
            Assert.Equal(RejectReason.TargetInvalid, Say(e, o1, " ").Reason);
        }

        [Fact]
        public void Ghost_LinesKeptForOutSeatWelcome()
        {
            MatchEngine e = Playing8();
            (byte o1, byte o2, byte living) = TwoOut(e);
            Assert.True(Say(e, o1, "first").IsOk);
            e.Tick(2f);
            Assert.True(e.Apply(o2, new ChatPhraseCommand("hmm", null)).IsOk);
            e.Tick(2f);

            IReadOnlyList<ChatEvent> seen = e.ViewFor(o2).GhostChat;
            Assert.Equal(2, seen.Count);
            Assert.Equal("first", seen[0].Text);
            Assert.Equal("hmm", seen[1].PhraseId);
            Assert.All(seen, ev => Assert.True(ev.Ghost));
            Assert.Equal(2, e.ViewFor(o1).GhostChat.Count);

            // Living seats never get ghost data.
            Assert.Empty(e.ViewFor(living).GhostChat);
        }

        [Fact]
        public void Ghost_LivingSeatNeverSeesGhostLinesAtMeetingStart()
        {
            MatchEngine e = Playing8();
            (byte o1, _, byte living) = TwoOut(e);
            Assert.True(Say(e, o1, "secret").IsOk);
            e.Tick(2f);
            e.TestTeleport(living, ButtonPos);
            Assert.True(e.Apply(living, new CallMeetingCommand()).IsOk);
            Assert.Empty(e.ViewFor(living).GhostChat);
        }

        [Fact]
        public void MeetingView_MyTextCounts()
        {
            (MatchEngine e, byte cit) = InMeeting();
            Assert.Equal(0, e.ViewFor(cit).Meeting!.MyText);
            Assert.True(Say(e, cit, "one").IsOk);
            e.Tick(2f);
            Assert.True(Say(e, cit, "two").IsOk);
            Assert.Equal(2, e.ViewFor(cit).Meeting!.MyText);
            byte other = cit == 0 ? (byte)1 : (byte)0;
            Assert.Equal(0, e.ViewFor(other).Meeting!.MyText);
        }
    }
}
