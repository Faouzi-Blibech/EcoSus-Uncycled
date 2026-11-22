using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots.Meeting;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>A human's typed line in a fast-forward meeting is read by the bots like the phrase it says.</summary>
    public class TypedChatMeetingTests
    {
        private sealed class Sent
        {
            public Sent(byte seat, float time, ICommand command, CommandResult result)
            {
                Seat = seat;
                Time = time;
                Command = command;
                Result = result;
            }

            public byte Seat { get; }

            public float Time { get; }

            public ICommand Command { get; }

            public CommandResult Result { get; }
        }

        /// <summary>One seeded meeting: a reported dump, scripted bots with the rule decider, one human seat that sends one line at 5 s.</summary>
        private sealed class Run
        {
            public byte Human;
            public byte Accused;
            public float HumanAt;
            public readonly List<Sent> BotChat = new List<Sent>();
            public readonly Dictionary<byte, byte?> Votes = new Dictionary<byte, byte?>();
            public readonly Dictionary<byte, float> HeatOfAccused = new Dictionary<byte, float>();
            public MeetingLog Log = null!;
            public CommandResult HumanResult;
        }

        private static string DisplayName(byte seat) => "Bot " + (seat + 1);

        private static Run Play(ulong seed, Func<byte, ICommand> humanLine)
        {
            TestMatch m = TestMatch.Create(seed);
            m.TickFor(11f);
            MatchEngine e = m.Engine;
            var mems = new Dictionary<byte, BotMemory>();
            foreach (PlayerState p in e.Players)
            {
                var mem = new BotMemory(m.Map, m.Config);
                mem.Observe(e.ViewFor(p.Seat), e.MatchTime, e.Witnesses.SeenBy(p.Seat));
                mems[p.Seat] = mem;
            }

            byte polluter = m.FirstSeatOf(Role.Polluter);
            SeatView pv = e.ViewFor(polluter);
            PlayerView witness = pv.Players.First(p => Vec2.Distance(p.Position, pv.Me.Position) <= 1.0f && !pv.Me.Partners.Contains(p.Seat));
            byte reporter = witness.Seat;
            Assert.True(e.Apply(reporter, new ReportCommand(m.SeedDump(polluter).Id)).IsOk);

            var run = new Run();
            run.Human = e.Players.First(p => p.IsActive && p.Seat != reporter).Seat;
            run.Accused = e.Players.First(p => p.IsActive && p.Seat != reporter && p.Seat != run.Human).Seat;
            var log = new MeetingLog(m.Content.Phrases!, m.Content.Words)
            {
                LivingNames = () => e.Players.Where(p => p.IsActive).ToDictionary(p => p.Seat, p => DisplayName(p.Seat)),
            };
            run.Log = log;

            var floor = new MeetingFloor();
            var rule = new RuleDecider();
            var talkers = new Dictionary<byte, MeetingTalker>();
            foreach (PlayerState p in e.Players.Where(p => p.IsActive && p.Seat != run.Human))
            {
                talkers[p.Seat] = new MeetingTalker(rule, rule, floor, new Random((int)seed * 100 + p.Seat));
            }

            float start = e.MatchTime;
            int eventsSeen = m.Events.Count;
            bool sentHuman = false;
            bool sampled = false;
            for (int guard = 0; e.Phase == MatchPhase.Meeting && guard < 4000; guard++)
            {
                float now = e.MatchTime;
                MeetingStage stage = e.Meeting!.Stage;
                if (!sentHuman && now - start >= 5f)
                {
                    sentHuman = true;
                    run.HumanAt = now;
                    run.HumanResult = e.Apply(run.Human, humanLine(run.Accused));
                }

                if (stage == MeetingStage.Voting && !sampled)
                {
                    sampled = true;
                    foreach (KeyValuePair<byte, BotMemory> kv in mems)
                    {
                        run.HeatOfAccused[kv.Key] = kv.Value.HeatOf(run.Accused);
                    }
                }

                foreach (KeyValuePair<byte, MeetingTalker> kv in talkers)
                {
                    byte seat = kv.Key;
                    SeatView view = e.ViewFor(seat);
                    mems[seat].Observe(view, now, e.Witnesses.SeenBy(seat));
                    kv.Value.Step(e, seat, view, mems[seat], log, now, c =>
                    {
                        CommandResult r = e.Apply(seat, c);
                        if (c is ChatPhraseCommand)
                        {
                            run.BotChat.Add(new Sent(seat, now, c, r));
                        }
                        else if (c is VoteCommand v)
                        {
                            run.Votes[seat] = v.Target;
                        }
                    });
                }

                m.Tick();
                for (; eventsSeen < m.Events.Count; eventsSeen++)
                {
                    if (m.Events[eventsSeen] is ChatEvent ce)
                    {
                        log.Add(ce, e.MatchTime);
                    }
                }
            }

            return run;
        }

        [Fact]
        public void TypedAccusation_MakesTheAccusedBotAnswer_AndRaisesItsHeat()
        {
            Run run = Play(3, accused => new ChatTextCommand("I saw " + DisplayName(accused) + " re-pollute in the market"));

            Assert.True(run.HumanResult.IsOk, "the typed line was rejected: " + run.HumanResult);
            Assert.Contains(run.Log.Acts, a => a.Speaker == run.Human && a.PhraseId == "saw_dump" && a.Seat == run.Accused && a.Zone == "market");
            Sent answer = run.BotChat.FirstOrDefault(c => c.Seat == run.Accused && c.Time >= run.HumanAt && ((ChatPhraseCommand)c.Command).PhraseId is "not_me" or "i_was")
                ?? throw new Xunit.Sdk.XunitException("the accused bot did not answer");
            // the reply is planned 3 to 8 s after the line is read; the shared floor may hold it back by up to 2 s more
            Assert.InRange(answer.Time - run.HumanAt, 3f - 0.15f, 8f + MeetingFloor.MinGapS + 0.3f);
            Assert.True(run.HeatOfAccused.Values.Max() > 0f, "no bot counted the accusation in the accused's heat");
        }

        [Theory]
        [InlineData("saw_dump")]
        [InlineData("sus")]
        public void TypedLine_ActsLikeThePhrase_SameVotesSameHeat(string phrase)
        {
            Func<byte, string> typed = accused => phrase == "sus"
                ? DisplayName(accused) + " is sus"
                : "I saw " + DisplayName(accused) + " re-pollute in the market";
            Func<byte, ICommand> spoken = accused => new ChatPhraseCommand(
                phrase,
                phrase == "sus" ? new[] { ChatArg.ForSeat(accused) } : new[] { ChatArg.ForSeat(accused), ChatArg.ForZone("market") });

            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6 })
            {
                Run text = Play(seed, accused => new ChatTextCommand(typed(accused)));
                Run phr = Play(seed, spoken);

                Assert.True(text.HumanResult.IsOk && phr.HumanResult.IsOk);
                ChatAct t = text.Log.Acts.First(a => a.Speaker == text.Human);
                ChatAct p = phr.Log.Acts.First(a => a.Speaker == phr.Human);
                Assert.Equal((p.PhraseId, p.Kind, p.Seat, p.Zone), (t.PhraseId, t.Kind, t.Seat, t.Zone));
                Assert.Equal(phr.Votes.OrderBy(v => v.Key).ToList(), text.Votes.OrderBy(v => v.Key).ToList());
                Assert.Equal(phr.HeatOfAccused.OrderBy(v => v.Key).ToList(), text.HeatOfAccused.OrderBy(v => v.Key).ToList());
            }
        }

        [Fact]
        public void GhostText_NeverReachesTheLog()
        {
            TestMatch m = TestMatch.Create(1);
            var log = new MeetingLog(m.Content.Phrases!, m.Content.Words) { LivingNames = () => new Dictionary<byte, string> { { 0, "Bot 1" }, { 1, "Bot 2" } } };

            log.Add(new ChatEvent(0, null, new List<ChatArg>(), "bot 2 is sus", true, new byte[] { 3 }), 10f);
            Assert.Empty(log.Acts);

            log.Add(new ChatEvent(0, null, new List<ChatArg>(), "bot 2 is sus", false, null), 10f);
            Assert.Single(log.Acts);
        }

        [Fact]
        public void TypedText_WithoutNamesOrWords_IsIgnored()
        {
            TestMatch m = TestMatch.Create(1);
            var noNames = new MeetingLog(m.Content.Phrases!, m.Content.Words);
            noNames.Add(new ChatEvent(0, null, new List<ChatArg>(), "bot 2 is sus", false, null), 10f);
            var noWords = new MeetingLog(m.Content.Phrases!) { LivingNames = () => new Dictionary<byte, string> { { 0, "Bot 1" }, { 1, "Bot 2" } } };
            noWords.Add(new ChatEvent(0, null, new List<ChatArg>(), "bot 2 is sus", false, null), 10f);

            Assert.Empty(noNames.Acts);
            Assert.Empty(noWords.Acts);
        }

        [Fact]
        public void Bots_NeverHitRateLimited_OverTwentySeededMeetings()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                Run run = Play(seed, accused => new ChatTextCommand(DisplayName(accused) + " is sus"));

                Assert.NotEmpty(run.BotChat);
                foreach (Sent c in run.BotChat)
                {
                    Assert.True(c.Result.IsOk, "seed " + seed + ": bot " + c.Seat + " line rejected: " + c.Result);
                }
            }
        }
    }
}
