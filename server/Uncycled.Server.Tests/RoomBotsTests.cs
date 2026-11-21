using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots;
using Uncycled.Server.Bots;
using Uncycled.Server.Rooms;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>
    /// A real room (1 human, 7 seats the host's start fills with bots) played in fast-forward by
    /// calling Step with simulated time instead of waiting for the 20 Hz loop.
    /// </summary>
    public class RoomBotsTests
    {
        private sealed class ThrowingBrain : IBotBrain
        {
            public int Calls;

            public void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send)
            {
                Calls++;
                throw new InvalidOperationException("brain exploded");
            }
        }

        private sealed class SlowBrain : IBotBrain
        {
            public void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send)
            {
                Thread.Sleep(40);
            }
        }

        /// <summary>A brain factory that cannot build any brain.</summary>
        private sealed class ExplodingBrains : BotBrains
        {
            public ExplodingBrains()
                : base(new BotsSettings(), RoomRig.EmptyModelsDir(), Array.Empty<Sim.Uncycled.TaskTypeDef>(), new CountingLog())
            {
            }

            public override IBotBrain Create(Role role, byte seat, RoomMeeting? meeting = null) => throw new InvalidOperationException("cannot build a brain");
        }

        private sealed class Result
        {
            public MatchEngine Engine = null!;
            public RoomRig Rig = null!;
            public int BotsThatMoved;
            public int TaskStepsDone;
            public bool Ended;

            public string Summary =>
                "winner " + Engine.Result?.Winner + " by " + Engine.Result?.Reason + " at " + Engine.Result?.MatchTime +
                " s, task steps " + TaskStepsDone + "/" + Engine.Tasks.TotalSteps + ", bots that moved " + BotsThatMoved;
        }

        private static Result PlayMatch(Func<IReadOnlyList<Sim.Uncycled.TaskTypeDef>, BotBrains> makeBrains)
        {
            var rig = new RoomRig(c => makeBrains(c.Tasks));
            rig.JoinAndStart();
            MatchEngine engine = rig.Engine;
            var origin = new Dictionary<byte, Vec2>();
            var moved = new HashSet<byte>();
            foreach (PlayerState p in engine.Players)
            {
                origin[p.Seat] = p.Position;
            }

            // the deadline counts active time only (meetings and the intro do not), so allow for those
            int maxSteps = (int)((rig.Content.Config.DeadlineS * 3f + 300f) / RoomRig.Dt);
            for (int i = 0; i < maxSteps && engine.Phase != MatchPhase.Ended; i++)
            {
                rig.Step();
                foreach (PlayerState p in engine.Players)
                {
                    if (p.Seat != 0 && (p.Position - origin[p.Seat]).Length > 1.5f)
                    {
                        moved.Add(p.Seat);
                    }
                }
            }

            return new Result
            {
                Engine = engine,
                Rig = rig,
                Ended = engine.Phase == MatchPhase.Ended,
                BotsThatMoved = moved.Count,
                TaskStepsDone = engine.Tasks.DoneSteps,
            };
        }

        private static string Why(Result r) =>
            r.Summary + ", phase " + r.Engine.Phase + ", match time " + r.Engine.MatchTime + ", active " + r.Engine.ActiveTime;

        [Fact]
        public void Room_with_one_human_and_seven_bots_plays_to_game_over_with_the_bot_brains()
        {
            var log = new CountingLog();
            Result r = PlayMatch(tasks => new BotBrains(new BotsSettings(), RoomRig.EmptyModelsDir(), tasks, log));

            Assert.True(r.Ended, Why(r));
            Assert.NotNull(r.Engine.Result);
            Assert.True(r.BotsThatMoved >= 6, r.Summary);
            Assert.True(r.TaskStepsDone > 0, r.Summary);
            Assert.Equal(0, log.Warnings);
        }

        [Fact]
        public void A_brain_that_throws_falls_back_to_the_scripted_brain_and_logs_once_per_seat()
        {
            var log = new CountingLog();
            var throwers = new List<ThrowingBrain>();
            Result r = PlayMatch(tasks => new BotBrains(
                new BotsSettings(),
                RoomRig.EmptyModelsDir(),
                tasks,
                log,
                primary: (role, rng) =>
                {
                    var t = new ThrowingBrain();
                    throwers.Add(t);
                    return t;
                }));

            Assert.True(r.Ended, Why(r));
            Assert.Equal(7, throwers.Count);
            foreach (ThrowingBrain t in throwers)
            {
                Assert.Equal(1, t.Calls);
            }

            Assert.Equal(7, log.Warnings);
            Assert.True(r.BotsThatMoved >= 6, r.Summary);
            Assert.True(r.TaskStepsDone > 0, r.Summary);
        }

        [Fact]
        public void A_minority_of_brains_that_keep_overrunning_the_budget_are_replaced_by_the_scripted_brain()
        {
            var log = new CountingLog();
            int built = 0;
            Result r = PlayMatch(tasks =>
            {
                var settings = new BotsSettings();
                return new BotBrains(
                    settings,
                    RoomRig.EmptyModelsDir(),
                    tasks,
                    log,
                    primary: (role, rng) => built++ < 3 ? new SlowBrain() : BrainFactory.Scripted(role, settings, rng, tasks),
                    slowMs: 25.0, // well above a real brain (about 0.1 ms) and below SlowBrain (40 ms)
                    slowStrikes: 3);
            });

            Assert.True(r.Ended, Why(r));
            Assert.Equal(3, log.Warnings);
            Assert.True(r.BotsThatMoved >= 6, r.Summary);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_human_who_drops_or_leaves_mid_match_is_replaced_by_a_moving_bot_and_a_rejoin_stops_it(bool leaves)
        {
            var rig = new RoomRig(c => new BotBrains(new BotsSettings(), RoomRig.EmptyModelsDir(), c.Tasks, new CountingLog()));
            rig.JoinAndStart();
            Seat seat = rig.Room.Seats[0];
            string token = seat.Token!;
            Assert.Null(seat.Brain);

            if (leaves)
            {
                rig.Send(rig.Human, "leave");
            }
            else
            {
                rig.Room.NotifyDisconnect(rig.Human);
            }

            rig.Step();
            Assert.Equal(leaves ? SeatControl.Bot : SeatControl.BotHoldingForHuman, seat.Control);
            Assert.IsType<GuardedBrain>(seat.Brain);
            Vec2 before = rig.Engine.Find(0)!.Position;
            rig.Run(20);
            Assert.True((rig.Engine.Find(0)!.Position - before).Length > 1.5f, "the bot never moved the seat");

            if (leaves)
            {
                return; // a seat given up on purpose has no token to come back with
            }

            rig.Human = RoomRig.NewConnection();
            rig.Room.Post(new JoinInput(rig.Human, "Human", token, false));
            rig.Step();
            Assert.Equal(SeatControl.Human, seat.Control);
            Assert.Null(seat.Brain);
            Vec2 back = rig.Engine.Find(0)!.Position;
            rig.Run(15);
            Assert.Equal(back, rig.Engine.Find(0)!.Position);
        }

        [Fact]
        public void A_brain_that_cannot_be_built_at_match_start_leaves_the_seat_idle_and_the_room_running()
        {
            var rig = new RoomRig(c => new ExplodingBrains());
            rig.JoinAndStart();
            rig.Run(5);

            Assert.NotNull(rig.Room.Engine);
            Assert.IsType<IdleBot>(rig.Room.Seats[3].Brain);
        }

        [Fact]
        public void The_bot_time_stat_shows_in_a_match_and_is_empty_again_in_the_lobby()
        {
            var rig = new RoomRig(c => new BotBrains(new BotsSettings(), RoomRig.EmptyModelsDir(), c.Tasks, new CountingLog()));
            rig.JoinAndStart();
            rig.Run(30);

            Assert.True(rig.Room.BotTickP95Ms() > 0);
            using JsonDocument during = JsonDocument.Parse(JsonSerializer.Serialize(rig.Manager.Stats(new[] { rig.Room })));
            Assert.True(during.RootElement.GetProperty("bots_p95_ms").GetDouble() > 0);

            MatchEngine engine = rig.Engine;
            int maxSteps = (int)((rig.Content.Config.DeadlineS * 3f + 300f) / RoomRig.Dt);
            for (int i = 0; i < maxSteps && engine.Phase != MatchPhase.Ended; i++)
            {
                rig.Step();
            }

            Assert.Equal(MatchPhase.Ended, engine.Phase);
            rig.Run(rig.Content.Config.ReturnToLobbyS + 2);
            Assert.Null(rig.Room.Engine);
            Assert.Equal(0.0, rig.Room.BotTickP95Ms());
            using JsonDocument after = JsonDocument.Parse(JsonSerializer.Serialize(rig.Manager.Stats(new[] { rig.Room })));
            Assert.Equal(0.0, after.RootElement.GetProperty("bots_p95_ms").GetDouble());
        }
    }
}
