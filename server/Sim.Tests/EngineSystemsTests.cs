using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Xunit;
using static Sim.Tests.TestWorld;

namespace Sim.Tests
{
    public class EngineSystemsTests
    {
        /// <summary>The sabotage opening lock is not what these tests are about: they sabotage at once.</summary>
        private static MatchConfig NoOpeningLock()
        {
            return new MatchConfig { SabotageStartCooldownS = 0f };
        }

        private static MatchConfig ShortMeetings(float emergencyMin = 15f)
        {
            return new MatchConfig { DiscussionS = 1f, VotingS = 1f, ResultsS = 1f, EmergencyMinSinceMeetingS = emergencyMin };
        }

        // ---------- emergency meetings ----------

        [Theory]
        [InlineData("early", RejectReason.OnCooldown)]
        [InlineData("far", RejectReason.OutOfRange)]
        [InlineData("fire", RejectReason.FireActive, Skip = "v1 rule, out of play in v2")]
        [InlineData("none_left", RejectReason.NoEmergenciesLeft)]
        [InlineData("out", RejectReason.NotAllowed)]
        [InlineData("ok", null)]
        public void Emergency_rules(string scenario, RejectReason? expected)
        {
            MatchEngine e = Playing(ShortMeetings());
            byte cit = Citizen(e);
            byte pol = Polluter(e);

            if (scenario == "early")
            {
                Advance(e, 5f);
            }
            else
            {
                Advance(e, 15.2f);
            }

            e.TestTeleport(cit, ButtonPos);
            switch (scenario)
            {
                case "far":
                    e.TestTeleport(cit, new Vec2(3.5f, 6.5f));
                    break;
                case "fire":
                    Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Fire)).IsOk);
                    break;
                case "none_left":
                    for (int i = 0; i < 2; i++)
                    {
                        e.TestTeleport(cit, ButtonPos);
                        Assert.True(e.Apply(cit, new CallMeetingCommand()).IsOk);
                        Advance(e, 3.5f);
                        Assert.Equal(MatchPhase.Playing, e.Phase);
                        if (i == 0)
                        {
                            // 15 s minimum since the last meeting
                            Assert.Equal(RejectReason.OnCooldown, e.Apply(cit, new CallMeetingCommand()).Reason);
                        }
                        Advance(e, 15.2f);
                    }

                    Assert.Equal(0, e.EmergenciesLeft);
                    e.TestTeleport(cit, ButtonPos);
                    break;
                case "out":
                    e.TestTeleport(cit, ButtonPos);
                    // vote the caller out through a meeting
                    Assert.True(e.Apply(cit, new CallMeetingCommand()).IsOk);
                    Advance(e, 1.1f);
                    foreach (PlayerState p in e.Players)
                    {
                        Assert.True(e.Apply(p.Seat, new VoteCommand(cit)).IsOk);
                    }

                    Advance(e, 1.5f + e.Config.EjectionS + 0.5f);
                    Assert.False(P(e, cit).IsActive);
                    Advance(e, 15.2f);
                    e.TestTeleport(cit, ButtonPos);
                    break;
            }

            CommandResult r = e.Apply(cit, new CallMeetingCommand());
            Assert.Equal(expected == null, r.IsOk);
            if (expected != null)
            {
                Assert.Equal(expected.Value, r.Reason);
            }
            else
            {
                Assert.Equal(MatchPhase.Meeting, e.Phase);
                Assert.Equal(1, e.EmergenciesLeft);
            }
        }

        // ---------- meeting flow ----------

        [Fact]
        public void Votes_plurality_ejects_and_confirm_reveals_polluter()
        {
            MatchConfig cfg = ShortMeetings(0f);
            cfg.ChatMinGapS = 0f; // this test sends many lines inside a one-second discussion
            MatchEngine e = Playing(cfg);
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            e.TestTeleport(cit, ButtonPos);
            Assert.True(e.Apply(cit, new CallMeetingCommand()).IsOk);
            Assert.Equal(RejectReason.WrongPhase, e.Apply(cit, new VoteCommand(pol)).Reason); // still discussion
            Assert.True(e.Apply(cit, new ChatPhraseCommand("i_suspect", new[] { ChatArg.ForSeat(pol), ChatArg.ForZone("west") })).IsOk);
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(cit, new ChatPhraseCommand("i_suspect", new[] { ChatArg.ForZone("moon") })).Reason);
            Assert.True(e.Apply(cit, new ChatPhraseCommand("a", null)).IsOk);
            Assert.True(e.Apply(cit, new ChatPhraseCommand("b", null)).IsOk);
            Assert.True(e.Apply(cit, new ChatPhraseCommand("c", null)).IsOk);
            Assert.True(e.Apply(cit, new ChatPhraseCommand("d", null)).IsOk);
            Assert.Equal(RejectReason.PhraseLimit, e.Apply(cit, new ChatPhraseCommand("e", null)).Reason); // 5 per meeting

            Advance(e, 1.1f);
            Assert.Equal(MeetingStage.Voting, e.Meeting!.Stage);
            int votes = 0;
            foreach (PlayerState p in e.Players)
            {
                if (p.Seat == pol)
                {
                    continue; // the polluter times out: counts as skip
                }

                Assert.True(e.Apply(p.Seat, new VoteCommand(pol)).IsOk);
                votes++;
            }

            Assert.Equal(RejectReason.NotAllowed, e.Apply(cit, new VoteCommand(pol)).Reason); // already voted

            List<GameEvent> events = Advance(e, 1.5f + e.Config.EjectionS + 0.5f);
            MeetingResultEvent result = Assert.Single(Of<MeetingResultEvent>(events));
            Assert.Equal((byte?)pol, result.Ejected);
            Assert.False(result.Tie);
            Assert.Equal(8, result.Votes.Count);
            EjectionEvent ejection = Assert.Single(Of<EjectionEvent>(events));
            Assert.Equal(pol, ejection.Seat);
            Assert.True(ejection.WasPolluter);
            Assert.Equal(7, votes);
            Assert.Equal(MatchPhase.Playing, e.Phase);
            Assert.Equal(8, Of<RoundResumedEvent>(events).Count);
            Assert.Equal(e.Map.SpawnFor(0), e.Players[0].Position);
        }

        [Fact]
        public void Both_polluters_voted_out_citizens_win()
        {
            MatchEngine e = Playing(ShortMeetings(0f));
            List<byte> polluters = SeatsWithRole(e, Role.Polluter);
            byte cit = Citizen(e);
            foreach (byte target in polluters)
            {
                e.TestTeleport(cit, ButtonPos);
                Assert.True(e.Apply(cit, new CallMeetingCommand()).IsOk);
                Advance(e, 1.1f);
                foreach (PlayerState p in e.Players)
                {
                    if (p.IsActive)
                    {
                        Assert.True(e.Apply(p.Seat, new VoteCommand(target)).IsOk);
                    }
                }

                Advance(e, 1.5f + e.Config.EjectionS + 0.5f);
            }

            Assert.Equal(MatchPhase.Ended, e.Phase);
            Assert.NotNull(e.Result);
            Assert.Equal(Winner.Citizens, e.Result!.Winner);
            Assert.Equal(GameOverReason.PollutersVotedOut, e.Result.Reason);
            Assert.Equal(8, e.Result.Roles.Count);
            Assert.Equal(RejectReason.WrongPhase, e.Apply(cit, new DumpCommand()).Reason);
        }

        // ---------- fire ----------

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Fire_is_fixed_by_two_holders_and_cooldown_starts_at_the_end()
        {
            MatchEngine e = Playing(NoOpeningLock());
            byte pol = Polluter(e);
            byte c1 = Citizen(e);
            byte c2 = Citizen(e, 1);
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Fire)).IsOk);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(pol, new FixCommand("fire_a", true)).Reason);

            e.TestTeleport(c1, new Vec2(2.5f, 2.0f));
            e.TestTeleport(c2, new Vec2(17.5f, 2.0f));
            Assert.True(e.Apply(c1, new FixCommand("fire_a", true)).IsOk);
            Advance(e, 1f);
            Assert.True(e.Fire.Active); // one holder is not enough

            // holder walks away: released on the next tick
            e.TestTeleport(c1, new Vec2(5.5f, 6.5f));
            e.Tick(0.05f);
            Assert.Empty(e.Fire.HeldConsoleIds());

            e.TestTeleport(c1, new Vec2(2.5f, 2.0f));
            Assert.True(e.Apply(c1, new FixCommand("fire_a", true)).IsOk);
            Assert.True(e.Apply(c2, new FixCommand("fire_b", true)).IsOk);
            List<GameEvent> events = Advance(e, 0.1f);
            SabotageEndedEvent ended = Assert.Single(Of<SabotageEndedEvent>(events));
            Assert.Equal(SabotageEndResult.Fixed, ended.Result);
            Assert.False(e.Fire.Active);

            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new SabotageCommand(SabotageKind.Smog)).Reason);
            Advance(e, 30.2f);
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Smog)).IsOk);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Fire_expiry_adds_20_and_does_not_end_the_match()
        {
            MatchEngine e = Playing(NoOpeningLock());
            byte pol = Polluter(e);
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Fire)).IsOk);
            float before = e.Meter.Value;
            List<GameEvent> events = Advance(e, 45.2f);
            SabotageEndedEvent ended = Assert.Single(Of<SabotageEndedEvent>(events));
            Assert.Equal(SabotageEndResult.Expired, ended.Result);
            Assert.True(e.Meter.Value >= before + 20f);
            Assert.Equal(MatchPhase.Playing, e.Phase);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Meeting_clears_an_active_fire()
        {
            MatchEngine e = Playing(ShortMeetings(0f));
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            ParkOthers(e, pol, cit);
            e.TestTeleport(pol, new Vec2(3.5f, 6.5f));
            Advance(e, 10.2f);
            Assert.True(e.Apply(pol, new DumpCommand()).IsOk);
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Fire)).IsOk);
            e.TestTeleport(cit, new Vec2(4.5f, 6.5f));
            Assert.True(e.Apply(cit, new ReportCommand(e.Piles.All[0].Id)).IsOk); // allowed during fire
            Assert.False(e.Fire.Active);
            Assert.Equal(SabotageEndResult.Cleared, Assert.Single(Of<SabotageEndedEvent>(e.Tick(0f))).Result);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Sabotage_is_locked_for_the_opening_seconds_like_hit_and_dump()
        {
            MatchEngine e = Playing();
            byte pol = Polluter(e);

            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new SabotageCommand(SabotageKind.Fire)).Reason);
            Assert.Equal(10f, e.ViewFor(pol).Me.Cooldowns!.Sabotage, 2);
            Advance(e, 9.8f);
            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new SabotageCommand(SabotageKind.Smog)).Reason);
            Advance(e, 0.4f);
            Assert.Equal(0f, e.ViewFor(pol).Me.Cooldowns!.Sabotage, 2);
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Smog)).IsOk);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Sabotage_opening_lock_comes_from_the_config()
        {
            MatchEngine e = Playing(new MatchConfig { SabotageStartCooldownS = 0f });
            Assert.True(e.Apply(Polluter(e), new SabotageCommand(SabotageKind.Fire)).IsOk);
        }

        // ---------- smog ----------

        [Fact]
        public void Smog_lowers_citizen_vision_only_and_is_cleared_at_the_panel()
        {
            MatchEngine e = Playing(NoOpeningLock());
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Smog)).IsOk);
            Advance(e, 1f);
            Assert.Equal(2.5f, e.ViewFor(cit).Me.Vision, 1);
            Advance(e, 1.1f);
            Assert.Equal(1f, e.ViewFor(cit).Me.Vision, 3);
            Assert.Equal(5f, e.ViewFor(pol).Me.Vision, 3);
            Assert.Equal(SabotageKind.Smog, e.ViewFor(cit).Sabotage!.Kind);

            Assert.Equal(RejectReason.OutOfRange, e.Apply(cit, new FixCommand("smog_panel", true)).Reason);
            e.TestTeleport(cit, new Vec2(12.5f, 8.0f));
            Assert.True(e.Apply(cit, new FixCommand("smog_panel", true)).IsOk);
            List<GameEvent> events = Advance(e, 3.1f);
            Assert.Equal(SabotageEndResult.Fixed, Assert.Single(Of<SabotageEndedEvent>(events)).Result);
            Advance(e, 2.1f);
            Assert.Equal(4f, e.ViewFor(cit).Me.Vision, 3);
            Assert.Null(e.ViewFor(cit).Sabotage);
        }

        [Fact]
        public void Smog_repair_progress_is_sent_to_the_repairer_only_and_a_repeat_tap_does_not_restart_it()
        {
            MatchEngine e = Playing(NoOpeningLock());
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            byte other = Citizen(e, 1);
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Smog)).IsOk);
            e.TestTeleport(cit, SmogPos);
            Assert.Null(e.ExtrasFor(cit).RepairElapsed);
            Assert.True(e.Apply(cit, new FixCommand("smog_panel", false)).IsOk);

            Advance(e, 1.5f);
            Assert.Equal(1.5f, e.ExtrasFor(cit).RepairElapsed!.Value, 1);
            Assert.Null(e.ExtrasFor(other).RepairElapsed);
            Assert.Null(e.ExtrasFor(pol).RepairElapsed);

            Assert.True(e.Apply(cit, new FixCommand("smog_panel", false)).IsOk); // a repeat tap
            Advance(e, 0.5f);
            Assert.Equal(2.0f, e.ExtrasFor(cit).RepairElapsed!.Value, 1);

            // the server drops the repair when its position leaves the range: no progress is sent
            e.TestTeleport(cit, Corner);
            e.Tick(0.05f);
            Assert.Null(e.ExtrasFor(cit).RepairElapsed);
        }

        [Fact]
        public void Smog_level_stays_in_my_view_while_vision_eases_back_after_the_smog_ends()
        {
            MatchEngine e = Playing(NoOpeningLock());
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            Assert.Equal(0f, e.ExtrasFor(cit).SmogLevel, 3);
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Smog)).IsOk);
            Advance(e, 1f);
            Assert.Equal(e.ViewFor(cit).Sabotage!.SmogLevel, e.ExtrasFor(cit).SmogLevel, 3);
            Advance(e, 1.1f);
            e.TestTeleport(cit, SmogPos);
            Assert.True(e.Apply(cit, new FixCommand("smog_panel", false)).IsOk);
            Advance(e, 3.1f);

            Advance(e, 0.5f);
            Assert.Null(e.ViewFor(cit).Sabotage);
            Assert.InRange(e.ExtrasFor(cit).SmogLevel, 0.01f, 0.99f);
            Advance(e, 2.1f);
            Assert.Equal(0f, e.ExtrasFor(cit).SmogLevel, 3);
        }

        // ---------- deadline on both clocks ----------

        [Fact]
        public void Default_deadline_clock_does_not_advance_during_a_meeting()
        {
            var cfg = new MatchConfig
            {
                DeadlineS = 20f,
                DiscussionS = 10f,
                VotingS = 10f,
                ResultsS = 1f,
                EmergencyMinSinceMeetingS = 0f,
            };
            Assert.Equal(DeadlineClock.ActiveTime, cfg.DeadlineClock);
            MatchEngine e = Playing(cfg);
            byte cit = Citizen(e);
            Advance(e, 5f);
            float before = e.ViewFor(cit).DeadlineClockValue;
            e.TestTeleport(cit, ButtonPos);
            Assert.True(e.Apply(cit, new CallMeetingCommand()).IsOk);
            Advance(e, 21.5f); // the 21 s meeting, then half a second of play
            Assert.Equal(MatchPhase.Playing, e.Phase);
            float after = e.ViewFor(cit).DeadlineClockValue;
            Assert.InRange(after - before, 0.4f, 0.7f);

            Advance(e, 20f - after - 1f);
            Assert.Equal(MatchPhase.Playing, e.Phase);
            Advance(e, 1.5f);
            Assert.Equal(MatchPhase.Ended, e.Phase);
            Assert.Equal(GameOverReason.Deadline, e.Result!.Reason);
        }

        [Theory]
        [InlineData(DeadlineClock.MatchTime)]
        [InlineData(DeadlineClock.ActiveTime)]
        public void Deadline_runs_on_the_configured_clock(DeadlineClock clock)
        {
            var cfg = new MatchConfig
            {
                DeadlineS = 20f,
                DeadlineClock = clock,
                DiscussionS = 10f,
                VotingS = 10f,
                ResultsS = 1f,
                EmergencyMinSinceMeetingS = 0f,
            };
            MatchEngine e = Playing(cfg);
            byte cit = Citizen(e);
            e.TestTeleport(cit, ButtonPos);
            Assert.True(e.Apply(cit, new CallMeetingCommand()).IsOk);
            Advance(e, 21.5f); // whole meeting, MatchTime passes 20, ActiveTime stays 0

            if (clock == DeadlineClock.MatchTime)
            {
                Assert.Equal(MatchPhase.Ended, e.Phase);
                Assert.Equal(GameOverReason.Deadline, e.Result!.Reason);
                Assert.Equal(Winner.Polluters, e.Result.Winner);
                return;
            }

            Assert.Equal(MatchPhase.Playing, e.Phase);
            Advance(e, 19f);
            Assert.Equal(MatchPhase.Playing, e.Phase);
            Advance(e, 1.2f);
            Assert.Equal(MatchPhase.Ended, e.Phase);
            Assert.Equal(GameOverReason.Deadline, e.Result!.Reason);
        }

        // ---------- hidden information ----------

        [Fact]
        public void Citizens_never_learn_who_the_polluters_are()
        {
            MatchEngine e = Engine();
            List<GameEvent> start = new List<GameEvent>(e.Tick(0f));
            List<MatchStartedEvent> started = Of<MatchStartedEvent>(start);
            Assert.Equal(8, started.Count);
            foreach (MatchStartedEvent s in started)
            {
                Assert.Equal(new byte[] { s.Seat }, Arr(s.Recipients));
                if (s.Role == Role.Citizen)
                {
                    Assert.Empty(s.Partners);
                    Assert.Null(s.Signature);
                    Assert.NotEmpty(s.Tasks);
                }
                else
                {
                    Assert.Single(s.Partners);
                    Assert.NotNull(s.Signature);
                    Assert.Empty(s.Tasks);
                }
            }

            e.Tick(e.Config.IntroS);
            foreach (PlayerState p in e.Players)
            {
                SeatView v = e.ViewFor(p.Seat);
                Assert.Equal(p.Role, v.Me.Role);
                if (p.IsCitizen)
                {
                    Assert.Empty(v.Me.Partners);
                    Assert.Null(v.Me.Signature);
                    Assert.Null(v.Me.Cooldowns);
                    Assert.NotNull(v.Me.Poison);
                }
                else
                {
                    Assert.Single(v.Me.Partners);
                    Assert.NotNull(v.Me.Cooldowns);
                    Assert.Null(v.Me.Poison);
                }

                Assert.Null(v.GameOver);
            }
        }

        [Fact]
        public void Player_behind_a_wall_is_absent_after_the_lost_sight_grace()
        {
            MatchEngine e = Playing();
            byte a = Citizen(e);
            byte b = Citizen(e, 1);
            ParkOthers(e, a, b);
            e.TestTeleport(a, new Vec2(4.5f, 5.5f));
            e.TestTeleport(b, new Vec2(8.0f, 5.5f));
            Advance(e, e.Config.LostSightGraceS + 0.1f);
            PlayerView seen = Assert.Single(e.ViewFor(a).Players);
            Assert.Equal(b, seen.Seat);

            e.TestTeleport(b, new Vec2(7.5f, 3.5f)); // 3.6 tiles away, inner wall between
            PlayerView ghost = Assert.Single(e.ViewFor(a).Players);
            Assert.Equal(new Vec2(8.0f, 5.5f), ghost.Position); // last visible position, never updated

            Advance(e, 0.4f);
            Assert.Empty(e.ViewFor(a).Players);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Piles_outside_vision_are_absent()
        {
            MatchEngine e = Playing();
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            ParkOthers(e, pol, cit);
            e.TestTeleport(pol, new Vec2(3.5f, 6.5f));
            Advance(e, 10.2f);
            Assert.True(e.Apply(pol, new DumpCommand()).IsOk);
            e.TestTeleport(pol, Corner);
            e.TestTeleport(cit, new Vec2(6.5f, 6.5f));
            Assert.Single(e.ViewFor(cit).Piles);
            e.TestTeleport(cit, new Vec2(9.0f, 6.5f)); // 5.5 tiles
            Assert.Empty(e.ViewFor(cit).Piles);
        }
    }
}
