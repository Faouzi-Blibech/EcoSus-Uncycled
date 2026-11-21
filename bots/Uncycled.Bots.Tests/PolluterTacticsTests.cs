using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>The scripted polluter's rules, one scene per rule (a synthetic world, no engine running).</summary>
    public class PolluterTacticsTests
    {
        private sealed class Fixed : IRandom
        {
            private readonly float _value;

            public Fixed(float value) { _value = value; }

            public int NextInt(int maxExclusive) => 0;

            public float NextFloat() => _value;
        }

        private static (SyntheticBot Bot, PolluterTactics Tactics) Scene(TestMatch m, float roll = 0f)
        {
            var bot = new SyntheticBot(m, Role.Polluter);
            bot.Partners.Add(1);
            bot.MatchTime = 100f;
            return (bot, new PolluterTactics(new Fixed(roll)));
        }

        private static PolluterDecision Decide(SyntheticBot bot, PolluterTactics t, bool busy = false, BotAction current = BotAction.Stay)
        {
            SeatView view = bot.View();
            bot.Memory.Observe(view, bot.MatchTime, new List<WitnessRecord>());
            return t.Decide(view, bot.Mask(), bot.Memory, busy, current);
        }

        private static void Heat(SyntheticBot bot, byte seat)
        {
            // everyone voted and accused the seat: heat 2
            var votes = new List<VoteRecord>();
            var accusations = new List<Accusation>();
            for (byte i = 0; i < 6; i++)
            {
                votes.Add(new VoteRecord(i, seat, false));
                accusations.Add(new Accusation(i, seat));
            }

            bot.Memory.RecordMeeting(new MeetingOutcome(votes, null, accusations));
        }

        [Fact]
        public void Hit_WhenTheTargetIsTheOnlyOneInSight()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, PolluterTactics t) = Scene(m);
            bot.Players.Add(SyntheticBot.Player(2, 11.2f, 12.5f));
            Assert.Equal(BotAction.HitNearest, Decide(bot, t).Action);
        }

        [Fact]
        public void NoHit_WhenAnotherCitizenWatches()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, PolluterTactics t) = Scene(m);
            bot.Players.Add(SyntheticBot.Player(2, 11.2f, 12.5f));
            bot.Players.Add(SyntheticBot.Player(3, 13.5f, 12.5f));
            Assert.NotEqual(BotAction.HitNearest, Decide(bot, t).Action);
        }

        [Fact]
        public void Smog_BeforeAPlannedHit()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, PolluterTactics t) = Scene(m);
            // hit ready, one citizen 3 tiles away (out of hit range, in stalking distance), no smog yet
            bot.Players.Add(SyntheticBot.Player(2, 13.5f, 12.5f));
            Assert.Equal(BotAction.SabotageSmog, Decide(bot, t).Action);
        }

        [Fact]
        public void NoSmogPrep_WhenSmogAlreadyRuns()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, PolluterTactics t) = Scene(m);
            bot.Sabotage = new SabotageView(SabotageKind.Smog, null, new List<string>(), 0.8f);
            bot.Players.Add(SyntheticBot.Player(2, 13.5f, 12.5f));
            Assert.NotEqual(BotAction.SabotageSmog, Decide(bot, t).Action);
        }

        [Fact]
        public void Fire_OnlyWhenNoCitizenIsInSight()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot alone, PolluterTactics t1) = Scene(m);
            alone.Cooldowns = new CooldownView(25f, 40f, 0f); // hit and dump not ready: nothing else to do
            Assert.Equal(BotAction.SabotageFire, Decide(alone, t1).Action);

            (SyntheticBot crowd, PolluterTactics t2) = Scene(m);
            crowd.Cooldowns = new CooldownView(25f, 40f, 0f);
            crowd.Players.Add(SyntheticBot.Player(2, 14f, 12.5f));
            crowd.Players.Add(SyntheticBot.Player(3, 15f, 12.5f));
            Assert.NotEqual(BotAction.SabotageFire, Decide(crowd, t2).Action);
        }

        [Fact]
        public void Dump_OnlyOutOfSightAndAwayFromPiles()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot alone, PolluterTactics t1) = Scene(m);
            Assert.Equal(BotAction.DumpHere, Decide(alone, t1).Action);

            (SyntheticBot watched, PolluterTactics t2) = Scene(m);
            watched.Players.Add(SyntheticBot.Player(2, 14.5f, 12.5f)); // in sight, even if farther than a hit
            watched.Players.Add(SyntheticBot.Player(3, 14.5f, 13.5f));
            Assert.NotEqual(BotAction.DumpHere, Decide(watched, t2).Action);

            (SyntheticBot nearPile, PolluterTactics t3) = Scene(m);
            nearPile.Piles.Add(new PileView(1, new Vec2(12.5f, 12.5f), PileKind.Dump, PileState.Fresh));
            Assert.NotEqual(BotAction.DumpHere, Decide(nearPile, t3).Action);
        }

        [Fact]
        public void FakeATask_WhenCitizensAreNear()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, PolluterTactics t) = Scene(m);
            bot.Cooldowns = new CooldownView(25f, 40f, 30f);
            // standing at a task console with two citizens around
            bot.Position = System.Linq.Enumerable.First(m.Map.Consoles, c => c.Kind == ConsoleKind.Task).Position;
            bot.Players.Add(SyntheticBot.Player(2, bot.Position.X + 3f, bot.Position.Y));
            bot.Players.Add(SyntheticBot.Player(3, bot.Position.X + 3f, bot.Position.Y + 1f));
            PolluterDecision d = Decide(bot, t);
            Assert.True(bot.Mask()[(int)BotAction.FakeTaskHere], "the scene must offer a fake task");
            Assert.Equal(BotAction.FakeTaskHere, d.Action);
        }

        [Fact]
        public void HotPolluter_DoesNotHitWhenSomeoneWasInSightJustNow_ButHitsAfterQuiet()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, PolluterTactics t) = Scene(m);
            Heat(bot, bot.Seat);
            Assert.True(bot.Memory.HeatOf(bot.Seat) >= 0.4f);

            // a second citizen was in sight a moment ago, now only the target is
            bot.Players.Add(SyntheticBot.Player(2, 11.2f, 12.5f));
            bot.Players.Add(SyntheticBot.Player(3, 14f, 12.5f));
            Decide(bot, t);
            bot.Players.RemoveAt(1);
            bot.MatchTime += 1f;
            Assert.NotEqual(BotAction.HitNearest, Decide(bot, t).Action);

            bot.MatchTime += 5f;
            Assert.Equal(BotAction.HitNearest, Decide(bot, t).Action);
        }

        [Fact]
        public void HotPolluter_StaysWithAGroup()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, PolluterTactics t) = Scene(m);
            bot.Cooldowns = new CooldownView(25f, 40f, 30f);
            Heat(bot, bot.Seat);
            bot.Players.Add(SyntheticBot.Player(2, 15f, 12.5f));
            bot.Players.Add(SyntheticBot.Player(3, 15f, 13.5f));
            BotAction? a = Decide(bot, t).Action;
            Assert.True(a == BotAction.FollowSlot0 || a == BotAction.FollowSlot1 || a == BotAction.FakeTaskHere, "got " + a);
        }
    }
}
