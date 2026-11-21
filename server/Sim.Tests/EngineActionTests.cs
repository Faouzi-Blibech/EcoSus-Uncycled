using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Xunit;
using static Sim.Tests.TestWorld;

namespace Sim.Tests
{
    public class EngineActionTests
    {
        private static MatchConfig ShortMeetings()
        {
            return new MatchConfig { DiscussionS = 1f, VotingS = 1f, ResultsS = 1f, EmergencyMinSinceMeetingS = 0f };
        }

        // ---------- poison through the engine ----------

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Poison_0_34_68_out_with_immunity_body_pile_and_task_reassignment()
        {
            MatchEngine e = Playing(ShortMeetings());
            byte pol = Polluter(e);
            byte pol2 = Polluter(e, 1);
            byte cit = Citizen(e);
            ParkOthers(e, pol, pol2, cit);
            e.TestTeleport(pol, new Vec2(3.5f, 6.5f));
            e.TestTeleport(cit, new Vec2(4.3f, 6.5f));
            e.TestTeleport(pol2, new Vec2(4.3f, 7.3f));
            int totalSteps = e.Tasks.TotalSteps;
            int unfinished = e.Tasks.RemainingFor(cit);

            Advance(e, 10.2f);
            Assert.True(e.Apply(pol, new HitCommand(cit)).IsOk);
            Assert.Equal(34, P(e, cit).Poison);
            List<GameEvent> events = new List<GameEvent>(e.Tick(0f));
            PoisonedEvent poisoned = Assert.Single(Of<PoisonedEvent>(events));
            Assert.Equal(new byte[] { cit }, Arr(poisoned.Recipients));
            Assert.Equal(34, poisoned.Level);

            Assert.Equal(RejectReason.TargetImmune, e.Apply(pol2, new HitCommand(cit)).Reason);
            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new HitCommand(cit)).Reason);

            Advance(e, 25.2f);
            Assert.True(e.Apply(pol2, new HitCommand(cit)).IsOk);
            Assert.Equal(68, P(e, cit).Poison);
            Assert.True(P(e, cit).IsActive);

            Advance(e, 25.2f);
            Assert.True(e.Apply(pol, new HitCommand(cit)).IsOk);
            Assert.Equal(100, P(e, cit).Poison);
            Assert.False(P(e, cit).IsActive);
            events = new List<GameEvent>(e.Tick(0f));
            YouAreOutEvent outEvent = Assert.Single(Of<YouAreOutEvent>(events));
            Assert.Equal(OutCause.Poison, outEvent.Cause);

            // body pile at the victim
            Pile body = Assert.Single(e.Piles.All);
            Assert.Equal(PileKind.Body, body.Kind);
            Assert.Equal(cit, body.Victim);
            Assert.Null(body.Waste);

            // tasks moved, total unchanged
            List<TaskAddedEvent> added = Of<TaskAddedEvent>(events);
            Assert.Equal(unfinished, added.Count);
            foreach (TaskAddedEvent a in added)
            {
                Assert.Equal(new byte[] { a.Seat }, Arr(a.Recipients));
                Assert.True(P(e, a.Seat).IsActive && P(e, a.Seat).IsCitizen);
            }

            Assert.Equal(totalSteps, e.Tasks.TotalSteps);
            Assert.Equal(0, e.Tasks.RemainingFor(cit));

            // out seat sees nothing
            SeatView outView = e.ViewFor(cit);
            Assert.Empty(outView.Players);
            Assert.Empty(outView.Piles);
            Assert.Equal(PlayerStatus.Out, outView.Me.Status);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(cit, new ReportCommand(body.Id)).Reason);

            // body reported, then removed at meeting end
            e.TestTeleport(pol2, new Vec2(4.0f, 6.0f));
            Assert.True(e.Apply(pol2, new ReportCommand(body.Id)).IsOk);
            Assert.Equal(PileState.Reported, body.State);
            Advance(e, 3.5f);
            Assert.Equal(MatchPhase.Playing, e.Phase);
            Assert.Equal(PileState.Removed, body.State);
        }

        [Theory(Skip = "v1 rule, out of play in v2")]
        [InlineData("cooldown", RejectReason.OnCooldown)]
        [InlineData("range", RejectReason.OutOfRange)]
        [InlineData("wall", RejectReason.WallBetween)]
        [InlineData("polluter", RejectReason.TargetInvalid)]
        [InlineData("self", RejectReason.TargetInvalid)]
        [InlineData("meeting", RejectReason.WrongPhase)]
        [InlineData("citizen_hits", RejectReason.NotAllowed)]
        [InlineData("ok", null)]
        public void Hit_validation(string scenario, RejectReason? expected)
        {
            MatchConfig cfg = ShortMeetings();
            if (scenario == "wall")
            {
                cfg.HitRange = 3f;
            }

            MatchEngine e = Playing(cfg);
            byte pol = Polluter(e);
            byte pol2 = Polluter(e, 1);
            byte cit = Citizen(e);
            ParkOthers(e, pol, pol2, cit);
            e.TestTeleport(pol, new Vec2(3.5f, 6.5f));
            e.TestTeleport(cit, new Vec2(4.3f, 6.5f));
            e.TestTeleport(pol2, new Vec2(3.5f, 7.3f));
            if (scenario != "cooldown")
            {
                Advance(e, 10.2f);
            }

            byte actor = pol;
            byte target = cit;
            switch (scenario)
            {
                case "range":
                    e.TestTeleport(cit, new Vec2(5.5f, 6.5f));
                    break;
                case "wall":
                    e.TestTeleport(pol, new Vec2(5.0f, 3.5f));
                    e.TestTeleport(cit, new Vec2(7.5f, 3.5f));
                    break;
                case "polluter":
                    target = pol2;
                    break;
                case "self":
                    target = pol;
                    break;
                case "meeting":
                    e.TestTeleport(cit, ButtonPos);
                    Assert.True(e.Apply(cit, new CallMeetingCommand()).IsOk);
                    break;
                case "citizen_hits":
                    actor = cit;
                    target = Citizen(e, 1);
                    break;
            }

            CommandResult r = e.Apply(actor, new HitCommand(target));
            if (expected == null)
            {
                Assert.True(r.IsOk);
            }
            else
            {
                Assert.False(r.IsOk);
                Assert.Equal(expected.Value, r.Reason);
            }
        }

        // ---------- dump ----------

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Dump_cooldown_meter_pile_cue_and_witness()
        {
            MatchEngine e = Playing();
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            ParkOthers(e, pol, cit);
            e.TestTeleport(pol, new Vec2(3.5f, 6.5f));
            e.TestTeleport(cit, new Vec2(5.5f, 6.5f));

            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new DumpCommand()).Reason);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(cit, new DumpCommand()).Reason);
            Advance(e, 10.2f);

            float before = e.Meter.Value;
            Assert.True(e.Apply(pol, new DumpCommand()).IsOk);
            Assert.Equal(before + 5f, e.Meter.Value, 3);

            Pile pile = Assert.Single(e.Piles.All);
            Assert.Equal(PileKind.Dump, pile.Kind);
            Assert.Equal(PileState.Fresh, pile.State);
            Assert.Equal(P(e, pol).Signature, pile.Waste);
            Assert.Equal("west", pile.ZoneId);
            Assert.Equal(e.MatchTime, pile.CreatedAt);

            List<GameEvent> events = new List<GameEvent>(e.Tick(0f));
            CueEvent cue = Assert.Single(Of<CueEvent>(events));
            Assert.Equal(new byte[] { cit }, Arr(cue.Recipients));
            Assert.Equal(CueKind.Dump, cue.Kind);
            Assert.Equal(pol, cue.Actor);
            WitnessRecord w = Assert.Single(e.Witnesses.SeenBy(cit));
            Assert.Equal(ActKind.Dump, w.Act);
            Assert.Equal(pol, w.Actor);
            Assert.Equal(pile.Id, w.PileId);

            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new DumpCommand()).Reason);
            Advance(e, 40.2f);
            Assert.True(e.Apply(pol, new DumpCommand()).IsOk);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void No_cue_without_line_of_sight()
        {
            MatchEngine e = Playing();
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            ParkOthers(e, pol, cit);
            e.TestTeleport(pol, new Vec2(5.0f, 3.5f));
            e.TestTeleport(cit, new Vec2(7.5f, 3.5f)); // 2.5 tiles, wall between
            Advance(e, 10.2f);
            Assert.True(e.Apply(pol, new DumpCommand()).IsOk);
            Assert.Empty(Of<CueEvent>(e.Tick(0f)));
            Assert.Empty(e.Witnesses.All);
        }

        // ---------- piles and reports ----------

        [Fact]
        public void Meeting_reports_every_fresh_pile_and_a_pile_is_reported_once()
        {
            MatchEngine e = Playing(ShortMeetings());
            byte pol = Polluter(e);
            byte pol2 = Polluter(e, 1);
            byte cit = Citizen(e);
            ParkOthers(e, pol, pol2, cit);
            Pile west = SeedDump(e, pol, new Vec2(3.5f, 6.5f));
            Pile east = SeedDump(e, pol2, new Vec2(15.5f, 6.5f));

            e.TestTeleport(cit, new Vec2(16.5f, 6.5f));
            Assert.True(e.Apply(cit, new ReportCommand(east.Id)).IsOk);
            Assert.Equal(MatchPhase.Meeting, e.Phase);
            Assert.Equal(PileState.Reported, west.State);
            Assert.Equal(PileState.Reported, east.State);

            MeetingStartedEvent started = Assert.Single(Of<MeetingStartedEvent>(e.Tick(0f)));
            Assert.Null(started.Recipients);
            Assert.Equal(MeetingCause.Report, started.Cause);
            Assert.Equal(cit, started.By);
            Assert.Equal(east.Id, started.ReportedPileId);
            Assert.Equal(2, started.Clues.Count);
            Assert.Equal(P(e, pol).Signature, started.Clues[0].Waste);
            Assert.Equal(P(e, pol2).Signature, started.Clues[1].Waste);
            Assert.NotEqual(started.Clues[0].Waste, started.Clues[1].Waste);
            Assert.Null(started.Clues[0].Victim);
            Assert.Equal("east", started.Clues[1].ZoneId);

            Advance(e, 3.5f);
            Assert.Equal(MatchPhase.Playing, e.Phase);
            Assert.Equal(PileState.Reported, west.State); // dump piles stay

            e.TestTeleport(cit, new Vec2(4.5f, 6.5f));
            Assert.Equal(RejectReason.AlreadyReported, e.Apply(cit, new ReportCommand(west.Id)).Reason);
        }

        [Theory]
        [InlineData(5.0f, 6.4f, null)]
        [InlineData(5.0f, 6.7f, RejectReason.OutOfRange)]
        [InlineData(7.9f, 3.5f, RejectReason.WallBetween)]
        public void Report_range_and_line_of_sight(float x, float y, RejectReason? expected)
        {
            MatchEngine e = Playing();
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            ParkOthers(e, pol, cit);
            SeedDump(e, pol, new Vec2(5.0f, 3.5f));
            e.TestTeleport(pol, Corner);
            e.TestTeleport(cit, new Vec2(x, y));
            CommandResult r = e.Apply(cit, new ReportCommand(e.Piles.All[0].Id));
            Assert.Equal(expected == null, r.IsOk);
            if (expected != null)
            {
                Assert.Equal(expected.Value, r.Reason);
            }
        }

        // ---------- cleaning ----------

        private static MatchEngine WithDumpPile(MatchConfig cfg, out byte pol, out byte cit, out byte cit2, out Pile pile)
        {
            MatchEngine e = Playing(cfg);
            pol = Polluter(e);
            cit = Citizen(e);
            cit2 = Citizen(e, 1);
            ParkOthers(e, pol, cit, cit2);
            e.TestTeleport(pol, new Vec2(3.5f, 6.5f));
            Advance(e, 10.2f);
            Assert.True(e.Apply(pol, new DumpCommand()).IsOk);
            e.TestTeleport(pol, Corner);
            pile = e.Piles.All[0];
            e.TestTeleport(cit, new Vec2(3.5f, 7.0f));
            e.TestTeleport(cit2, new Vec2(3.0f, 6.5f));
            e.Tick(0f);
            return e;
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Cleaning_completes_after_3_seconds_and_lowers_the_meter()
        {
            MatchEngine e = WithDumpPile(ShortMeetings(), out byte pol, out byte cit, out byte cit2, out Pile pile);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(pol, new CleanStartCommand(pile.Id)).Reason);
            float before = e.Meter.Value;
            Assert.True(e.Apply(cit, new CleanStartCommand(pile.Id)).IsOk);
            Assert.Equal(RejectReason.AlreadyCleaning, e.Apply(cit2, new CleanStartCommand(pile.Id)).Reason);

            List<GameEvent> events = Advance(e, 3.1f);
            CleanOkEvent ok = Assert.Single(Of<CleanOkEvent>(events));
            Assert.Equal(3f, ok.Duration);
            CleanDoneEvent done = Assert.Single(Of<CleanDoneEvent>(events));
            Assert.Equal(new byte[] { cit }, Arr(done.Recipients));
            Assert.Equal(PileState.Cleaned, pile.State);
            float passive = 3.1f * e.Config.MeterPassivePerS;
            Assert.Equal(before - 2.5f + passive, e.Meter.Value, 1);
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(cit2, new CleanStartCommand(pile.Id)).Reason);
        }

        [Theory(Skip = "v1 rule, out of play in v2")]
        [InlineData("leave")]
        [InlineData("meeting")]
        [InlineData("cancel")]
        public void Cleaning_is_cancelled(string how)
        {
            MatchEngine e = WithDumpPile(ShortMeetings(), out byte _, out byte cit, out byte cit2, out Pile pile);
            Assert.True(e.Apply(cit, new CleanStartCommand(pile.Id)).IsOk);
            Advance(e, 1f);
            switch (how)
            {
                case "leave":
                    e.TestTeleport(cit, new Vec2(3.5f, 8.5f));
                    break;
                case "meeting":
                    e.TestTeleport(cit2, ButtonPos);
                    Assert.True(e.Apply(cit2, new CallMeetingCommand()).IsOk);
                    break;
                case "cancel":
                    Assert.True(e.Apply(cit, new CleanCancelCommand(pile.Id)).IsOk);
                    break;
            }

            List<GameEvent> events = Advance(e, 3f);
            Assert.Single(Of<CleanCancelledEvent>(events));
            Assert.Empty(Of<CleanDoneEvent>(events));
            Assert.NotEqual(PileState.Cleaned, pile.State);
        }

        // ---------- movement (08 section 2) ----------

        [Theory]
        [InlineData(3.5f, 6.5f, 4.5f, 6.5f, null, false)]
        [InlineData(3.5f, 6.5f, 5.5f, 6.5f, RejectReason.NotAllowed, true)]   // over budget
        [InlineData(5.6f, 3.5f, 7.05f, 3.5f, RejectReason.WallBetween, true)] // crosses the inner wall
        [InlineData(1.5f, 6.5f, 0.6f, 6.5f, RejectReason.WallBetween, true)]  // into the border
        public void Move_validation(float fx, float fy, float tx, float ty, RejectReason? expected, bool correction)
        {
            MatchEngine e = Playing();
            byte cit = Citizen(e);
            var from = new Vec2(fx, fy);
            e.TestTeleport(cit, from);
            CommandResult r = e.Apply(cit, new MoveCommand(new Vec2(tx, ty), 1));
            Assert.Equal(expected == null, r.IsOk);
            if (expected != null)
            {
                Assert.Equal(expected.Value, r.Reason);
            }

            List<CorrectionEvent> corrections = Of<CorrectionEvent>(e.Tick(0f));
            Assert.Equal(correction ? 1 : 0, corrections.Count);
            if (correction)
            {
                Assert.Equal(from, corrections[0].Position);
                Assert.Equal(from, P(e, cit).Position);
            }
        }

        [Fact]
        public void Move_sequence_and_budget_refill()
        {
            MatchEngine e = Playing();
            byte cit = Citizen(e);
            e.TestTeleport(cit, new Vec2(3.5f, 6.5f));
            Assert.True(e.Apply(cit, new MoveCommand(new Vec2(5.0f, 6.5f), 5)).IsOk); // spends the 1.5 budget
            Assert.Equal(RejectReason.NotAllowed, e.Apply(cit, new MoveCommand(new Vec2(5.1f, 6.5f), 5)).Reason); // same seq
            Assert.Equal(RejectReason.NotAllowed, e.Apply(cit, new MoveCommand(new Vec2(5.1f, 6.5f), 3)).Reason); // older seq
            Assert.Empty(Of<CorrectionEvent>(e.Tick(0f)));

            Assert.Equal(RejectReason.NotAllowed, e.Apply(cit, new MoveCommand(new Vec2(5.5f, 6.5f), 6)).Reason); // no budget
            Assert.Single(Of<CorrectionEvent>(e.Tick(0f)));

            Advance(e, 0.5f); // refills 2.5 * 1.2 * 0.5 = 1.5
            Assert.True(e.Apply(cit, new MoveCommand(new Vec2(5.5f, 6.5f), 7)).IsOk);
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(cit, new MoveCommand(new Vec2(float.NaN, 6.5f), 8)).Reason);
            Assert.Equal(new Vec2(5.5f, 6.5f), P(e, cit).Position);
        }

        // ---------- use / task_step ----------

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Use_and_task_steps_check_range_duration_and_order()
        {
            var types = new List<TaskTypeDef>
            {
                new TaskTypeDef("fill_crate", TaskCategory.Short, 1, 1f, new[] { "tap" }),
                new TaskTypeDef("turn_compost", TaskCategory.Long, 2, 2f, new[] { "compost", "tap" }),
            };
            var cfg = new MatchConfig { TasksCommon = 0, TasksShort = 1, TasksLong = 1 };
            MatchEngine e = Playing(cfg, 42, types);
            byte cit = Citizen(e);
            byte pol = Polluter(e);
            List<TaskState> mine = e.Tasks.OwnedBy(cit);
            Assert.Equal(2, mine.Count);
            TaskState crate = mine[0].Type.Id == "fill_crate" ? mine[0] : mine[1];
            TaskState compost = mine[0].Type.Id == "turn_compost" ? mine[0] : mine[1];

            e.TestTeleport(cit, new Vec2(3.5f, 6.5f));
            Assert.Equal(RejectReason.OutOfRange, e.Apply(cit, new UseCommand("tap")).Reason);
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(cit, new UseCommand("nope")).Reason);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(cit, new TaskStepCommand(crate.Id)).Reason); // no session

            e.TestTeleport(cit, new Vec2(16.5f, 7.0f));
            e.TestTeleport(pol, new Vec2(16.0f, 7.0f));
            Assert.Equal(RejectReason.NotAllowed, e.Apply(pol, new UseCommand("tap")).Reason);
            Assert.True(e.Apply(cit, new UseCommand("tap")).IsOk);
            UseOkEvent useOk = Assert.Single(Of<UseOkEvent>(e.Tick(0f)));
            Assert.Equal(crate.Id, useOk.TaskId);
            Assert.Equal(RejectReason.OnCooldown, e.Apply(cit, new TaskStepCommand(crate.Id)).Reason);
            Advance(e, 1.1f);
            Assert.True(e.Apply(cit, new TaskStepCommand(crate.Id)).IsOk);
            Assert.True(crate.IsDone);
            TaskProgressEvent progress = Assert.Single(Of<TaskProgressEvent>(e.Tick(0f)));
            Assert.Equal(1, progress.StepsDone);

            // step order: compost step 0 is at "compost", so "tap" has nothing to open now
            Assert.Equal(RejectReason.NotAllowed, e.Apply(cit, new UseCommand("tap")).Reason);

            e.TestTeleport(cit, new Vec2(16.5f, 3.0f));
            Assert.True(e.Apply(cit, new UseCommand("compost")).IsOk);
            Advance(e, 2.1f);
            Assert.True(e.Apply(cit, new TaskStepCommand(compost.Id)).IsOk);
            Assert.Equal(1, compost.StepsDone);
            Assert.Equal("tap", compost.CurrentConsoleId);
        }
    }
}
