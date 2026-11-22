using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Uncycled.Bots.Meeting;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// A Cleaner that calls an emergency meeting because it saw the machine get jammed or a gas warning start says what it saw
    /// (saw_jam / saw_gas with the zone) and names the seats it saw nearby (saw_near), so the others can accuse and vote.
    /// </summary>
    public class MeetingExplainTests
    {
        private sealed class CallSink : IDecisionSink, IEmergencyCallSink
        {
            public List<(string Match, byte Seat, string Trigger, string? Zone)> Calls { get; } = new List<(string, byte, string, string?)>();

            public void Sent(MeetingState state, MeetingDecision sent)
            {
            }

            public void EmergencyCall(string matchId, byte seat, string trigger, string? zone) => Calls.Add((matchId, seat, trigger, zone));
        }

        private sealed class Run
        {
            public TestMatch M = null!;
            public byte Cit;
            public byte Near;
            public byte Pol;
            public TalkingBrain Brain = null!;
            public CallSink Sink = null!;
            public List<ChatEvent> CitChat = new List<ChatEvent>();
            public byte? Vote;
            public bool Voted;
            public bool Met;
        }

        private static Vec2 MachinePos(TestMatch m) => m.Map.ConsolesOfKind(ConsoleKind.Machine)[0].Position;

        private static Run Start(Func<TestMatch, byte, byte, byte, (Vec2 CitAt, Vec2 NearAt)> place, Action<TestMatch, byte> trigger)
        {
            var r = new Run { M = TestMatch.Create(2) };
            TestMatch m = r.M;
            m.TickFor(Math.Max(m.Config.EmergencyMinSinceMeetingS, m.Config.TrasherStartCooldownS) + 1f);
            r.Pol = m.FirstSeatOf(Role.Polluter);
            r.Cit = m.FirstSeatOf(Role.Citizen);
            r.Near = m.Engine.Players.First(p => p.Role == Role.Citizen && p.Seat != r.Cit).Seat;

            var settings = new BotsSettings { DecisionIntervalS = 0.2f, Skill = 1f, EmergencyOnWitnessChance = 1f };
            IBotBrain inner = BrainFactory.Scripted(Role.Citizen, settings, new XorShiftRandom(7), m.Content.Tasks);
            var talker = new MeetingTalker(new RuleDecider(), new RuleDecider(), new MeetingFloor(), new Random(1));
            r.Sink = new CallSink();
            talker.Sink = r.Sink;
            talker.MatchId = "m1";
            var log = new MeetingLog(m.Content.Phrases!);
            r.Brain = new TalkingBrain(inner, talker, log);

            Action<ICommand> send = c =>
            {
                if (c is VoteCommand v)
                {
                    r.Vote = v.Target;
                    r.Voted = true;
                }

                m.Engine.Apply(r.Cit, c);
            };

            // warm the brain: it assumes a meeting just ended when it is made mid-match
            for (float t = 0f; t < m.Config.EmergencyMinSinceMeetingS + 1f; t += TestMatch.Dt)
            {
                r.Brain.Think(m.Engine, r.Cit, TestMatch.Dt, send);
                m.Tick();
            }

            (Vec2 citAt, Vec2 nearAt) = place(m, r.Cit, r.Near, r.Pol);
            m.Engine.TestTeleport(r.Cit, citAt);
            m.Engine.TestTeleport(r.Near, nearAt);
            trigger(m, r.Pol);

            int seen = m.Events.Count;
            for (int i = 0; i < 4000 && m.Engine.Phase != MatchPhase.Ended; i++)
            {
                r.Brain.Think(m.Engine, r.Cit, TestMatch.Dt, send);
                m.Tick();
                for (; seen < m.Events.Count; seen++)
                {
                    if (m.Events[seen] is ChatEvent c)
                    {
                        log.Add(c, m.Engine.MatchTime);
                        if (c.Seat == r.Cit)
                        {
                            r.CitChat.Add(c);
                        }
                    }
                }

                r.Met |= m.Engine.Phase == MatchPhase.Meeting;
                if (r.Met && m.Engine.Phase != MatchPhase.Meeting)
                {
                    break;
                }
            }

            return r;
        }

        private static (Vec2, Vec2) NearTheMachine(TestMatch m, byte cit, byte near, byte pol)
        {
            var h = new MapDistances_Helper(m);
            Vec2 machine = MachinePos(m);
            return (h.WalkableNear(machine, 3f), h.WalkableNear(machine, 1.6f));
        }

        [Fact]
        public void Caller_SawTheJamWithASeatNearby_SaysWhatItSawNamesTheSeatAndVotesIt()
        {
            Run r = Start(NearTheMachine, (m, pol) => Assert.True(m.Engine.Apply(pol, new SabotageCommand(SabotageKind.Machine)).IsOk));

            string zone = r.M.Map.ZoneAt(MachinePos(r.M));
            Assert.NotEqual("", zone);
            Assert.True(r.Met, "the Cleaner calls the meeting");
            Assert.NotEmpty(r.CitChat);
            Assert.Equal("saw_jam", r.CitChat[0].PhraseId);
            Assert.Equal(zone, r.CitChat[0].Args.Single(a => a.Kind == ChatArgKind.Zone).Zone);
            ChatEvent near = r.CitChat.First(c => c.PhraseId == "saw_near");
            Assert.Equal(r.Near, near.Args.Single(a => a.Kind == ChatArgKind.Seat).Seat);
            Assert.Equal((byte?)r.Near, r.Vote);

            // the server log can say who called and why
            var call = Assert.Single(r.Sink.Calls);
            Assert.Equal((r.Cit, "jam", (string?)zone), (call.Seat, call.Trigger, call.Zone));
        }

        [Fact]
        public void Caller_SawTheGasWarningStart_SaysWhichRoomAndWhoWasInIt()
        {
            ZoneDef room = null!;
            Run r = Start(
                (m, cit, near, pol) =>
                {
                    room = m.Map.GasRooms.First(z => z.Id == "gas_market");
                    MapDistances d = MapDistances.For(m.Map);
                    Vec2 c = d.NearestWalkable(new Vec2(room.X + room.Width / 2f, room.Y + room.Height / 2f));
                    return (c, new MapDistances_Helper(m).WalkableNear(c, 1.5f));
                },
                (m, pol) =>
                {
                    m.Engine.Find(pol)!.Ability = TrasherAbility.Gas;
                    Assert.True(m.Engine.Apply(pol, new GasCommand("gas_market")).IsOk);
                });

            Assert.True(r.Met, "the Cleaner calls the meeting");
            Assert.NotEmpty(r.CitChat);
            Assert.Equal("saw_gas", r.CitChat[0].PhraseId);
            Assert.Equal("market", r.CitChat[0].Args.Single(a => a.Kind == ChatArgKind.Zone).Zone);
            Assert.Equal("gas", Assert.Single(r.Sink.Calls).Trigger);
            Assert.Contains(r.Brain.Memory!.Alarms, a => a.Kind == "gas" && a.Zone == "market");
        }

        [Fact]
        public void Cleaner_FarFromTheMachine_HoldsNoJamEvidence()
        {
            Run r = Start(
                (m, cit, near, pol) =>
                {
                    MapDistances dist = MapDistances.For(m.Map);
                    Vec2 best = default;
                    float far = -1f;
                    foreach (ZoneDef z in m.Map.Zones)
                    {
                        Vec2 c = dist.NearestWalkable(new Vec2(z.X + z.Width / 2f, z.Y + z.Height / 2f));
                        float d = Vec2.Distance(c, MachinePos(m));
                        if (d > far)
                        {
                            far = d;
                            best = c;
                        }
                    }

                    return (best, new MapDistances_Helper(m).WalkableNear(MachinePos(m), 1.6f));
                },
                (m, pol) => Assert.True(m.Engine.Apply(pol, new SabotageCommand(SabotageKind.Machine)).IsOk));

            Assert.Empty(r.Brain.Memory!.Alarms);
            Assert.DoesNotContain(r.CitChat, c => c.PhraseId == "saw_jam" || c.PhraseId == "saw_near");
            Assert.Empty(r.Sink.Calls);
        }

        [Fact]
        public void Grounding_AJamLineWithoutAJamSighting_BecomesHmm()
        {
            var state = new MeetingState { Self = 0, Role = Role.Citizen, Living = new byte[] { 0, 1, 2 }, Zones = new[] { "recycling" }, Stage = MeetingStage.Discussion };

            MeetingDecision d = Grounding.Check(MeetingDecision.Say("rule", "saw_jam", null, "recycling"), state);

            Assert.Equal("hmm", d.PhraseId);
        }

        [Fact]
        public void Listener_HearsASeatNamedNearTheJam_ReachesTheAccusationThresholdNotTheVote()
        {
            TestMatch m = TestMatch.Create();
            var mem = new BotMemory(m.Map, m.Config);
            SeatView view = m.Engine.ViewFor(0);
            mem.Observe(view, 100f, new List<WitnessRecord>());
            var model = new SuspicionModel();
            model.Update(mem, view);
            var log = new MeetingLog(m.Content.Phrases!);
            log.Add(new ChatEvent(1, "saw_near", new[] { ChatArg.ForSeat(6), ChatArg.ForZone("recycling") }), 5f);

            model.ApplyChat(log, mem, 0);

            Assert.Equal(SuspicionModel.NearPileWeight, model.Score(6));
            Assert.True(model.Score(6) < SuspicionModel.VoteThreshold);
        }
    }
}
