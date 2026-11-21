using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>Review fix round 1: decision contract, masks, ranges, memory clocks.</summary>
    public class FixRoundTests
    {
        private static readonly Vec2 FireA = new Vec2(2.5f, 9.5f);

        private static SabotageView Fire(params string[] held) => new SabotageView(SabotageKind.Fire, 30f, held, 0f);

        // ---------- C1: the decision contract ----------

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Executor_ReDecidedEveryHalfSecondStillCompletesATask()
        {
            TestMatch m = TestMatch.Create();
            byte seat = m.FirstSeatOf(Role.Citizen);
            var driver = new BotDriver(m, seat);
            driver.Begin(BotAction.GoNextTask);
            driver.RunUntilIdle(80f);

            SeatView here = driver.View;
            TaskInfo task = here.Me.Tasks.First(t => t.StepsDone < t.Steps
                && Vec2.Distance(here.Me.Position, m.Map.FindConsole(t.ConsoleId)!.Position) <= m.Map.FindConsole(t.ConsoleId)!.Range);
            float minS = m.Engine.Tasks.Find(task.Id)!.Type.MinSeconds;

            int ticks = 0;
            int max = (int)((minS + 3f) / TestMatch.Dt);
            while (ticks < max && !m.Events.Any(e => e is TaskProgressEvent p && p.Seat == seat))
            {
                if (ticks % 10 == 0)
                {
                    driver.Begin(BotAction.UseHere);
                }

                driver.Step();
                ticks++;
            }

            Assert.Contains(m.Events, e => e is TaskProgressEvent p && p.Seat == seat);
            Assert.True(ticks * TestMatch.Dt <= minS + 1f, "took " + ticks * TestMatch.Dt + " s, min " + minS);
            Assert.Single(driver.Sent, kv => kv.Key is UseCommand);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Executor_ChangingActionMidUseIsIgnored_TheCommittedStepCompletes()
        {
            TestMatch m = TestMatch.Create();
            byte seat = m.FirstSeatOf(Role.Citizen);
            var driver = new BotDriver(m, seat);
            driver.Begin(BotAction.GoNextTask);
            driver.RunUntilIdle(80f);
            driver.Begin(BotAction.UseHere);
            for (int i = 0; i < 10; i++)
            {
                driver.Step();
            }

            // a citizen that opened the session works the step to its end (the old contract: any other action abandoned it)
            driver.Begin(BotAction.Stay);
            for (int i = 0; i < 200; i++)
            {
                driver.Step();
            }

            Assert.False(driver.Executor.Busy);
            Assert.Single(driver.Sent, kv => kv.Key is TaskStepCommand);
            Assert.Contains(m.Events, e => e is TaskProgressEvent p && p.Seat == seat);
        }

        [Fact]
        public void Executor_FollowEndsAfterTheCap()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m);
            bot.Players.Add(SyntheticBot.Player(1, 16.5f, 12.5f));
            bot.Begin(BotAction.FollowSlot0);

            float used = bot.RunUntilIdle(12f);

            Assert.False(bot.Executor.Busy);
            Assert.InRange(used, 1f, 6f);
        }

        // ---------- I1: a polluter follows into hit range ----------

        [Fact]
        public void Executor_PolluterFollowClosesToHitRange()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Polluter);
            bot.Players.Add(SyntheticBot.Player(1, 16.5f, 12.5f));
            bot.Begin(BotAction.FollowSlot0);

            bot.RunUntilIdle(8f);

            Assert.True(Vec2.Distance(bot.Position, new Vec2(16.5f, 12.5f)) <= m.Config.HitRange);
            Assert.True(bot.Mask()[(int)BotAction.HitNearest]);
        }

        // ---------- I2 and m10: fire holds ----------

        [Fact]
        public void Mask_HoldFireFalseOnConsoleHeldByOther()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Citizen, FireA);

            bot.Sabotage = Fire("fire_a");
            bool[] held = bot.Mask();
            Assert.False(held[(int)BotAction.HoldFire]);
            Assert.False(held[(int)BotAction.ReleaseFire]);

            bot.Sabotage = Fire();
            Assert.True(bot.Mask()[(int)BotAction.HoldFire]);
        }

        [Fact]
        public void Mask_ReleaseFireTrueOnlyForMyOwnHold()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Citizen, FireA);
            bot.Sabotage = Fire();
            bot.Begin(BotAction.HoldFire);
            bot.Step();
            Assert.Contains(bot.Sent, c => c is FixCommand f && f.Holding && f.ConsoleId == "fire_a");

            bot.Sabotage = Fire("fire_a");
            bool[] mask = bot.Mask();
            Assert.True(mask[(int)BotAction.HoldFire]);
            Assert.True(mask[(int)BotAction.ReleaseFire]);
        }

        [Fact]
        public void Executor_HoldFireSkipsConsoleHeldByOther()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Citizen, FireA);
            bot.Sabotage = Fire("fire_a");
            bot.Begin(BotAction.HoldFire);

            for (int i = 0; i < 20; i++)
            {
                bot.Step();
            }

            Assert.DoesNotContain(bot.Sent, c => c is FixCommand f && f.Holding && f.ConsoleId == "fire_a");
            Assert.Contains(bot.Sent, c => c is MoveCommand);
        }

        // ---------- I3: hit immunity from what the bot saw, on play time ----------

        private static WitnessRecord PartnerHit(float at, byte victim) =>
            new WitnessRecord(ActKind.Hit, 1, 0, at, "depot", new Vec2(10.5f, 12.5f), null, victim);

        private static SyntheticBot PolluterNextTo(TestMatch m, byte victim)
        {
            var bot = new SyntheticBot(m, Role.Polluter);
            bot.Partners.Add(1);
            bot.Players.Add(SyntheticBot.Player(victim, 11.3f, 12.5f));
            return bot;
        }

        [Fact]
        public void Mask_HitSkipsVictimOfAWitnessedPartnerHit()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = PolluterNextTo(m, 2);
            bot.MatchTime = 19.5f;
            bot.Observe();
            Assert.True(bot.Mask()[(int)BotAction.HitNearest]);

            bot.MatchTime = 20f;
            bot.Memory.Observe(bot.View(), 20f, new List<WitnessRecord> { PartnerHit(19.8f, 2) });

            Assert.False(bot.Mask()[(int)BotAction.HitNearest]);
        }

        [Fact]
        public void Mask_HitImmunityCountsPlayTimeNotMeetingTime()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = PolluterNextTo(m, 2);
            bot.MatchTime = 10f;
            bot.Memory.Observe(bot.View(), 10f, new List<WitnessRecord> { PartnerHit(10f, 2) });

            bot.Phase = MatchPhase.Meeting;
            bot.MatchTime = 11f;
            bot.Observe();
            bot.MatchTime = 60f;
            bot.Observe();
            bot.Phase = MatchPhase.Playing;
            bot.MatchTime = 61f;
            bot.Observe();
            bot.MatchTime = 62f;
            bot.Observe();
            Assert.False(bot.Mask()[(int)BotAction.HitNearest]);

            bot.MatchTime = 85f;
            bot.Observe();
            Assert.False(bot.Mask()[(int)BotAction.HitNearest]);

            bot.MatchTime = 90f;
            bot.Observe();
            Assert.True(bot.Mask()[(int)BotAction.HitNearest]);
        }

        // ---------- m1, m2: reach needs line of sight, meeting console in range ----------

        [Fact]
        public void Mask_ReachNeedsLineOfSight()
        {
            TestMatch m = TestMatch.Create();
            var console = new List<ConsoleDef> { new ConsoleDef("t1", ConsoleKind.Task, new Vec2(7.5f, 2.5f), 3.5f) };
            var behindWall = new SyntheticBot(m, Role.Polluter, new Vec2(5.5f, 2.5f), SyntheticBot.TinyMap(12, 5, 6, console));
            var open = new SyntheticBot(m, Role.Polluter, new Vec2(5.5f, 2.5f), SyntheticBot.TinyMap(12, 5, null, console));

            Assert.False(behindWall.Mask()[(int)BotAction.FakeTaskHere]);
            Assert.True(open.Mask()[(int)BotAction.FakeTaskHere]);
        }

        [Fact]
        public void Executor_CallMeetingUsesTheConsoleInRange()
        {
            TestMatch m = TestMatch.Create();
            var consoles = new List<ConsoleDef>
            {
                new ConsoleDef("btn_a", ConsoleKind.Emergency, new Vec2(2.5f, 2.5f), 1.2f),
                new ConsoleDef("btn_b", ConsoleKind.Emergency, new Vec2(9.5f, 2.5f), 1.2f),
            };
            var bot = new SyntheticBot(m, Role.Citizen, new Vec2(9.5f, 2.5f), SyntheticBot.TinyMap(12, 5, null, consoles));
            bot.MatchTime = 0.5f;
            bot.Observe();
            bot.MatchTime = 30f;
            Assert.True(bot.Mask()[(int)BotAction.CallMeeting]);

            bot.Begin(BotAction.CallMeeting);
            bot.Step();

            Assert.IsType<CallMeetingCommand>(Assert.Single(bot.Sent));
        }

        // ---------- m4, m11: clean restarts, report only in range ----------

        [Fact]
        public void Executor_CleanStartsTheNewPileAfterTheOldOneVanished()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m);
            bot.Piles.Add(new PileView(1, new Vec2(11.5f, 12.5f), PileKind.Dump, PileState.Fresh));
            bot.Begin(BotAction.CleanNearest);
            for (int i = 0; i < 60 && !bot.Sent.Any(c => c is CleanStartCommand); i++)
            {
                bot.Step();
            }

            Assert.Contains(bot.Sent, c => c is CleanStartCommand s && s.PileId == 1);

            bot.Piles.Clear();
            bot.Piles.Add(new PileView(2, new Vec2(11.5f, 13.5f), PileKind.Dump, PileState.Fresh));
            for (int i = 0; i < 60 && !bot.Sent.Any(c => c is CleanStartCommand s && s.PileId == 2); i++)
            {
                if (!bot.Executor.Busy)
                {
                    bot.Begin(BotAction.CleanNearest);
                }

                bot.Step();
            }

            Assert.Contains(bot.Sent, c => c is CleanStartCommand s && s.PileId == 2);
        }

        [Fact]
        public void Executor_ReportNotSentWhenTheWalkTimedOut()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m);
            bot.Piles.Add(new PileView(1, new Vec2(30.5f, 12.5f), PileKind.Dump, PileState.Fresh));
            bot.Begin(BotAction.ReportNearest);
            bot.MatchTime += 1000f;

            for (int i = 0; i < 5; i++)
            {
                bot.Step();
            }

            Assert.DoesNotContain(bot.Sent, c => c is ReportCommand);
            Assert.False(bot.Executor.Busy);
        }

        // ---------- m7, m8: memory ----------

        [Fact]
        public void Mask_CallMeetingFalseRightAfterATakeover()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m, Role.Citizen, new Vec2(23.5f, 13.5f));
            bot.MatchTime = 120f;
            bot.Observe();
            Assert.False(bot.Mask()[(int)BotAction.CallMeeting]);

            bot.MatchTime = 136f;
            bot.Observe();
            Assert.True(bot.Mask()[(int)BotAction.CallMeeting]);
        }

        [Fact]
        public void Suspicion_BodyVictimIsNotSuspected()
        {
            TestMatch m = TestMatch.Create();
            var bot = new SyntheticBot(m);
            bot.Players.Add(SyntheticBot.Player(4, 11.5f, 12.5f));
            bot.MatchTime = 100f;
            bot.Observe();

            bot.Players.Clear();
            bot.Phase = MatchPhase.Meeting;
            bot.MatchTime = 110f;
            var clue = new ClueInfo(7, PileKind.Body, m.Map.ZoneAt(new Vec2(11.5f, 12.5f)), 105f, null, 4);
            bot.Meeting = new MeetingView(MeetingStage.Discussion, 30f, 0, MeetingCause.Report, new List<ClueInfo> { clue }, new List<byte>(), false, null, 0f);
            bot.Observe();
            var model = new SuspicionModel();
            model.Update(bot.Memory, bot.View());

            Assert.Equal(0f, model.Score(4));
        }

        // ---------- m5, m9: observation safety ----------

        [Fact]
        public void Observation_NoNaNWithZeroedConfig()
        {
            TestMatch m = TestMatch.Create();
            m.TickFor(11f);
            byte polluter = m.FirstSeatOf(Role.Polluter);
            Assert.True(m.Engine.Apply(polluter, new SabotageCommand(SabotageKind.Smog)).IsOk);
            m.Tick();
            var zero = new MatchConfig
            {
                HitCooldownS = 0f,
                DumpCooldownS = 0f,
                SabotageSharedCooldownS = 0f,
                FireCountdownS = 0f,
                EmergenciesPerMatch = 0,
            };

            var mem = new BotMemory(m.Map, zero);
            SeatView view = m.Engine.ViewFor(polluter);
            mem.Observe(view, m.Engine.MatchTime, m.Engine.Witnesses.SeenBy(polluter));
            var obs = new float[ObservationBuilder.Size];
            ObservationBuilder.Build(view, mem, m.Map, zero, m.Engine.MatchTime, obs);

            for (int i = 0; i < obs.Length; i++)
            {
                Assert.True(float.IsFinite(obs[i]) && obs[i] >= -1f && obs[i] <= 1f, ObservationBuilder.FieldNames[i] + " = " + obs[i]);
            }
        }

        [Fact]
        public void Observation_RefusesMapsAndSeatCountsTheSchemaCannotHold()
        {
            TestMatch m = TestMatch.Create();
            var obs = new float[ObservationBuilder.Size];

            var nine = new MatchConfig { Seats = 9 };
            var bot = new SyntheticBot(m);
            Assert.Throws<NotSupportedException>(() =>
                ObservationBuilder.Build(bot.View(), new BotMemory(m.Map, nine), m.Map, nine, 30f, obs));

            var zones = new List<ZoneDef>();
            for (int i = 0; i < 4; i++)
            {
                zones.Add(new ZoneDef("z" + i, 1 + i, 1, 1, 1));
            }

            MapData fourZones = SyntheticBot.TinyMap(12, 5, null, new List<ConsoleDef>(), zones);
            Assert.Throws<NotSupportedException>(() =>
                ObservationBuilder.Build(bot.View(), new BotMemory(fourZones, m.Config), fourZones, m.Config, 30f, obs));
        }
    }
}
