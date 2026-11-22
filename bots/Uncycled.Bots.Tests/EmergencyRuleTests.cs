using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Uncycled.Bots.Neural;
using Uncycled.Server.Content;
using Uncycled.Train;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// A Cleaner bot that sees a Trasher act (a re-pollute it witnesses, the machine jam going up in its sight, a gas warning going up with the room in its sight) walks to the
    /// emergency table and calls a meeting with the chance emergency_on_witness_chance, one roll per act, only while the
    /// seat has emergency calls left and the meeting cooldown allows, after its reaction delay. Scripted Cleaners, the
    /// live NeuralBrain and the training host's network seats all follow it; Trashers are not forced.
    /// </summary>
    public sealed class EmergencyRuleTests
    {
        private static readonly string ContentDir = ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));

        private sealed class StayPolicy : IPolicy
        {
            public int Act(float[] obs, bool[] mask, float temperature, IRandom rng) => Array.IndexOf(PolicyActionsV2.Names.ToArray(), "Stay");
        }

        private sealed class NoBrain : IBotBrain
        {
            public void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send)
            {
            }
        }

        private static TestMatch Ready()
        {
            TestMatch m = TestMatch.Create(2);
            m.TickFor(Math.Max(m.Config.EmergencyMinSinceMeetingS, m.Config.TrasherStartCooldownS) + 1f);
            return m;
        }

        private static BrainRig OneScripted(TestMatch m, byte seat, float chance)
        {
            var rig = new BrainRig(m, 5, s => s.EmergencyOnWitnessChance = chance);
            for (byte s = 0; s < m.Config.Seats; s++)
            {
                if (s != seat)
                {
                    rig.Skip.Add(s);
                }
            }

            return rig;
        }

        /// <summary>A brain made mid-match assumes a meeting just ended: let its cooldown run out before the act under test.</summary>
        private static void Warm(BrainRig rig, TestMatch m) => rig.RunUntil(() => false, m.Config.EmergencyMinSinceMeetingS + 1f);

        private static Vec2 MachinePos(TestMatch m) => m.Map.ConsolesOfKind(ConsoleKind.Machine)[0].Position;

        private static ZoneDef GasRoom(TestMatch m) => m.Map.GasRooms.First(z => z.Id == "gas_market");

        /// <summary>A walkable spot well outside the sight of a place: the zone centre farthest from it (the test checks it is beyond the vision).</summary>
        private static Vec2 OutOfSightOf(TestMatch m, Func<Vec2, float> distanceToPlace)
        {
            MapDistances dist = MapDistances.For(m.Map);
            Vec2 best = default;
            float far = -1f;
            foreach (ZoneDef z in m.Map.Zones)
            {
                Vec2 c = dist.NearestWalkable(new Vec2(z.X + z.Width / 2f, z.Y + z.Height / 2f));
                float d = distanceToPlace(c);
                if (d > far)
                {
                    far = d;
                    best = c;
                }
            }

            Assert.True(far > m.Config.VisionCitizen + 1f, "the map has no spot out of sight of the place, farthest " + far);
            return best;
        }

        private static float DistanceToRoom(ZoneDef z, Vec2 p)
        {
            float dx = Math.Max(Math.Max(z.X - p.X, 0f), p.X - (z.X + z.Width));
            float dy = Math.Max(Math.Max(z.Y - p.Y, 0f), p.Y - (z.Y + z.Height));
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static void WitnessRepollute(TestMatch m, byte trasher, byte witness)
        {
            PlayerState p = m.Engine.Find(trasher)!;
            m.Engine.Witnesses.Add(new WitnessRecord(ActKind.Repollute, trasher, witness, m.Engine.MatchTime, m.Map.ZoneAt(p.Position), p.Position, null, null));
        }

        private static List<SentCommand> AcceptedCalls(IEnumerable<SentCommand> sent, byte seat) =>
            sent.Where(s => s.Seat == seat && s.Command is CallMeetingCommand && s.Result.IsOk).ToList();

        // ---------- the scripted Cleaner ----------

        [Fact]
        public void ScriptedCleaner_WitnessesRepollute_CallsAnEmergencyWhenTheRollPasses()
        {
            TestMatch m = Ready();
            byte cit = m.FirstSeatOf(Role.Citizen);
            BrainRig rig = OneScripted(m, cit, 1f);
            Warm(rig, m);
            WitnessRepollute(m, m.FirstSeatOf(Role.Polluter), cit);

            rig.RunUntil(() => m.Engine.Phase == MatchPhase.Meeting, 40f);

            Assert.Equal(MatchPhase.Meeting, m.Engine.Phase);
            Assert.Single(AcceptedCalls(rig.Sent, cit));
        }

        [Fact]
        public void ScriptedCleaner_WitnessesRepollute_CallsNothingWhenTheRollFails()
        {
            TestMatch m = Ready();
            byte cit = m.FirstSeatOf(Role.Citizen);
            BrainRig rig = OneScripted(m, cit, 0f);
            Warm(rig, m);
            WitnessRepollute(m, m.FirstSeatOf(Role.Polluter), cit);

            rig.RunUntil(() => m.Engine.Phase == MatchPhase.Meeting, 40f);

            Assert.Equal(MatchPhase.Playing, m.Engine.Phase);
            Assert.DoesNotContain(rig.Sent, s => s.Command is CallMeetingCommand);
        }

        [Fact]
        public void ScriptedCleaner_SeesTheMachineJamGoUp_CallsAnEmergency()
        {
            TestMatch m = Ready();
            byte cit = m.FirstSeatOf(Role.Citizen);
            BrainRig rig = OneScripted(m, cit, 1f);
            Warm(rig, m);
            m.Engine.TestTeleport(cit, new MapDistances_Helper(m).WalkableNear(MachinePos(m), 3f));
            Assert.True(m.Engine.Apply(m.FirstSeatOf(Role.Polluter), new SabotageCommand(SabotageKind.Machine)).IsOk);

            rig.RunUntil(() => m.Engine.Phase == MatchPhase.Meeting, 40f);

            Assert.Equal(MatchPhase.Meeting, m.Engine.Phase);
            Assert.Single(AcceptedCalls(rig.Sent, cit));
        }

        [Fact]
        public void ScriptedCleaner_FarFromTheMachine_IgnoresTheJam()
        {
            TestMatch m = Ready();
            byte cit = m.FirstSeatOf(Role.Citizen);
            BrainRig rig = OneScripted(m, cit, 1f);
            Warm(rig, m);
            m.Engine.TestTeleport(cit, OutOfSightOf(m, p => Vec2.Distance(p, MachinePos(m))));
            Assert.True(m.Engine.Apply(m.FirstSeatOf(Role.Polluter), new SabotageCommand(SabotageKind.Machine)).IsOk);

            rig.RunUntil(() => false, 25f); // the jam is public, the seat does not see the machine: the roll is never taken (it may go and fix it)

            Assert.DoesNotContain(rig.Sent, s => s.Command is CallMeetingCommand);
        }

        [Fact]
        public void ScriptedCleaner_InTheWarnedRoom_CallsAnEmergency()
        {
            TestMatch m = Ready();
            byte cit = m.FirstSeatOf(Role.Citizen);
            byte pol = m.FirstSeatOf(Role.Polluter);
            m.Engine.Find(pol)!.Ability = TrasherAbility.Gas;
            BrainRig rig = OneScripted(m, cit, 1f);
            Warm(rig, m);
            ZoneDef room = GasRoom(m);
            m.Engine.TestTeleport(cit, MapDistances.For(m.Map).NearestWalkable(new Vec2(room.X + room.Width / 2f, room.Y + room.Height / 2f)));
            Assert.True(m.Engine.Apply(pol, new GasCommand("gas_market")).IsOk);

            rig.RunUntil(() => m.Engine.Phase == MatchPhase.Meeting, 40f);

            Assert.Equal(MatchPhase.Meeting, m.Engine.Phase);
            Assert.Single(AcceptedCalls(rig.Sent, cit));
        }

        [Fact]
        public void ScriptedCleaner_OutOfSightOfTheGasRoom_IgnoresTheWarning()
        {
            TestMatch m = Ready();
            byte cit = m.FirstSeatOf(Role.Citizen);
            byte pol = m.FirstSeatOf(Role.Polluter);
            m.Engine.Find(pol)!.Ability = TrasherAbility.Gas;
            BrainRig rig = OneScripted(m, cit, 1f);
            Warm(rig, m);
            ZoneDef room = GasRoom(m);
            m.Engine.TestTeleport(cit, OutOfSightOf(m, p => DistanceToRoom(room, p)));
            Assert.True(m.Engine.Apply(pol, new GasCommand("gas_market")).IsOk);

            rig.RunUntil(() => false, m.Config.GasWarningS - 1f);

            Assert.DoesNotContain(rig.Sent, s => s.Command is CallMeetingCommand);
        }

        [Fact]
        public void ScriptedCleaner_WithNoEmergenciesLeft_CallsNothing()
        {
            TestMatch m = TestMatch.Create(2);
            byte cit = m.FirstSeatOf(Role.Citizen);
            foreach (byte victim in m.Engine.Players.Where(p => p.Role == Role.Citizen && p.Seat != cit).Select(p => p.Seat).Take(2).ToList())
            {
                m.EjectByVote(victim); // each ejection spends one of the match's emergency calls
            }

            Assert.Equal(0, m.Engine.EmergenciesLeft);
            BrainRig rig = OneScripted(m, cit, 1f);
            Warm(rig, m);
            WitnessRepollute(m, m.FirstSeatOf(Role.Polluter), cit);

            rig.RunUntil(() => false, 40f);

            Assert.DoesNotContain(rig.Sent, s => s.Command is CallMeetingCommand);
        }

        [Fact]
        public void ScriptedCleaner_WithinTheMeetingCooldown_CallsNothing()
        {
            TestMatch m = Ready();
            byte cit = m.FirstSeatOf(Role.Citizen);
            m.EjectByVote(m.Engine.Players.First(p => p.Role == Role.Citizen && p.Seat != cit).Seat);
            Assert.True(m.Engine.EmergenciesLeft > 0);
            BrainRig rig = OneScripted(m, cit, 1f);
            rig.RunUntil(() => false, 0.5f);
            WitnessRepollute(m, m.FirstSeatOf(Role.Polluter), cit);

            rig.RunUntil(() => false, m.Config.EmergencyMinSinceMeetingS - 3f);

            Assert.DoesNotContain(rig.Sent, s => s.Command is CallMeetingCommand);
        }

        [Fact]
        public void ScriptedTrasher_IsNotForced()
        {
            TestMatch m = Ready();
            byte pol = m.FirstSeatOf(Role.Polluter);
            BrainRig rig = OneScripted(m, pol, 1f);
            Warm(rig, m);
            m.Engine.Apply(pol, new SabotageCommand(SabotageKind.Machine)); // refused when the Trasher bot already jammed it itself
            Assert.True(m.Engine.Machine.Jammed);

            rig.RunUntil(() => false, 20f);

            Assert.DoesNotContain(rig.Sent, s => s.Command is CallMeetingCommand);
        }

        // ---------- the live NeuralBrain ----------

        private static List<CallMeetingCommand> RunNeural(TestMatch m, byte seat, float chance, Action? afterFirstThink, float seconds)
        {
            var settings = new BotsSettings { DecisionIntervalS = 0.2f, Skill = 1f, EmergencyOnWitnessChance = chance };
            var brain = new NeuralBrain(new StayPolicy(), new NoBrain(), settings, new XorShiftRandom(3), m.Content.Tasks);
            var calls = new List<CallMeetingCommand>();
            int warm = (int)((m.Config.EmergencyMinSinceMeetingS + 1f) / TestMatch.Dt); // a brain made mid-match assumes a meeting just ended
            for (int i = 0; i < warm + (int)(seconds / TestMatch.Dt); i++)
            {
                brain.Think(m.Engine, seat, TestMatch.Dt, cmd =>
                {
                    if (m.Engine.Apply(seat, cmd).IsOk && cmd is CallMeetingCommand c)
                    {
                        calls.Add(c);
                    }
                });
                m.Tick();
                if (i == warm)
                {
                    afterFirstThink?.Invoke();
                }
            }

            return calls;
        }

        [Fact]
        public void NeuralCleaner_WitnessesRepollute_CallsAnEmergencyWhateverTheNetworkWants()
        {
            TestMatch m = Ready();
            byte cit = m.FirstSeatOf(Role.Citizen);
            byte pol = m.FirstSeatOf(Role.Polluter);

            List<CallMeetingCommand> calls = RunNeural(m, cit, 1f, () => WitnessRepollute(m, pol, cit), 30f);

            Assert.Single(calls);
            Assert.Equal(MatchPhase.Meeting, m.Engine.Phase);
        }

        [Fact]
        public void NeuralCleaner_WhenTheRollFails_CallsNothing()
        {
            TestMatch m = Ready();
            byte cit = m.FirstSeatOf(Role.Citizen);
            byte pol = m.FirstSeatOf(Role.Polluter);

            List<CallMeetingCommand> calls = RunNeural(m, cit, 0f, () => WitnessRepollute(m, pol, cit), 30f);

            Assert.Empty(calls);
        }

        [Fact]
        public void NeuralCleaner_SeesTheMachineJamGoUp_CallsAnEmergency()
        {
            TestMatch m = Ready();
            byte cit = m.FirstSeatOf(Role.Citizen);
            byte pol = m.FirstSeatOf(Role.Polluter);

            List<CallMeetingCommand> calls = RunNeural(m, cit, 1f, () =>
            {
                m.Engine.TestTeleport(cit, new MapDistances_Helper(m).WalkableNear(MachinePos(m), 3f));
                Assert.True(m.Engine.Apply(pol, new SabotageCommand(SabotageKind.Machine)).IsOk);
            }, 30f);

            Assert.Single(calls);
        }

        [Fact]
        public void NeuralCleaner_FarFromTheMachine_IgnoresTheJam()
        {
            TestMatch m = Ready();
            byte cit = m.FirstSeatOf(Role.Citizen);
            byte pol = m.FirstSeatOf(Role.Polluter);

            List<CallMeetingCommand> calls = RunNeural(m, cit, 1f, () =>
            {
                m.Engine.TestTeleport(cit, OutOfSightOf(m, p => Vec2.Distance(p, MachinePos(m))));
                Assert.True(m.Engine.Apply(pol, new SabotageCommand(SabotageKind.Machine)).IsOk);
            }, 25f);

            Assert.Empty(calls);
        }

        [Fact]
        public void NeuralTrasher_IsNotForced()
        {
            TestMatch m = Ready();
            byte pol = m.FirstSeatOf(Role.Polluter);

            List<CallMeetingCommand> calls = RunNeural(m, pol, 1f, () => Assert.True(m.Engine.Apply(pol, new SabotageCommand(SabotageKind.Machine)).IsOk), 20f);

            Assert.Empty(calls);
        }

        // ---------- the training host ----------

        private static MatchRunner Runner(string learner, float chance)
        {
            HostOptions o = HostOptions.Parse(new[] { "--content", ContentDir, "--matches", "1", "--seed", "7", "--threads", "1",
                "--learner", learner, "--rw-win", "0" });
            o.Settings.EmergencyOnWitnessChance = chance;
            ContentBundle content = ContentLoader.Load(o.ContentDir);
            return new MatchRunner(o, 0, content, HostBrains.Scripted(o.Settings, content));
        }

        private static bool StayOnly(byte[] frames, MatchRunner r, int agent)
        {
            int stay = Array.IndexOf(PolicyActionsV2.Names.ToArray(), "Stay");
            int at = agent * r.FrameSize + r.MaskOffset;
            for (int i = 0; i < PolicyActionsV2.Count; i++)
            {
                if ((frames[at + i] != 0) != (i == stay))
                {
                    return false;
                }
            }

            return true;
        }

        private static byte[] AdvanceToQuiet(MatchRunner r)
        {
            var buf = new byte[r.AgentCount * r.FrameSize];
            for (int d = 0; d < 600 && !(r.Phase == MatchPhase.Playing && r.MatchTime > 20f); d++)
            {
                r.WriteFrames(buf);
                r.Advance(new int[r.AgentCount]);
            }

            Assert.Equal(MatchPhase.Playing, r.Phase);
            return buf;
        }

        [Fact]
        public void HostCleaner_WitnessesRepollute_GetsPlaceholderFramesAndCallsAnyway()
        {
            MatchRunner r = Runner("citizen", 1f);
            byte[] buf = AdvanceToQuiet(r);
            byte seat = r.AgentSeat(0);
            Assert.True(r.Engine.EmergenciesLeft > 0);
            r.WriteFrames(buf); // one frame so the rule has seen the seat before the act
            r.Advance(new int[r.AgentCount]);
            int meetingsBefore = r.MeetingsHeld;
            byte pol = r.Engine.Players.First(p => p.Role == Role.Polluter).Seat;
            PlayerState trasher = r.Engine.Find(pol)!;
            r.Engine.Witnesses.Add(new WitnessRecord(ActKind.Repollute, pol, seat, r.Engine.MatchTime, r.Engine.Map.ZoneAt(trasher.Position), trasher.Position, null, null));
            int go = Array.IndexOf(PolicyActionsV2.Names.ToArray(), "GoZoneNear0");

            int held = 0;
            for (int d = 0; d < 120 && r.MeetingsHeld == meetingsBefore; d++)
            {
                r.WriteFrames(buf);
                var actions = new int[r.AgentCount];
                if (!r.AgentCommitted(0) && StayOnly(buf, r, 0))
                {
                    held++;
                    Assert.Equal(0f, BitConverter.ToSingle(buf, r.RewardOffset));
                    actions[0] = go; // ignored for a placeholder frame: walking off would break the walk to the table
                }

                r.Advance(actions);
            }

            Assert.True(held > 0, "the forced decisions must reach the trainer as Stay-only placeholder frames");
            Assert.True(r.MeetingsHeld > meetingsBefore, "the rule must call the emergency meeting");
        }

        [Fact]
        public void HostCleaner_WhenTheRollFails_IsNeverHeld()
        {
            MatchRunner r = Runner("citizen", 0f);
            byte[] buf = AdvanceToQuiet(r);
            byte seat = r.AgentSeat(0);
            r.WriteFrames(buf);
            r.Advance(new int[r.AgentCount]);
            byte pol = r.Engine.Players.First(p => p.Role == Role.Polluter).Seat;
            PlayerState trasher = r.Engine.Find(pol)!;
            r.Engine.Witnesses.Add(new WitnessRecord(ActKind.Repollute, pol, seat, r.Engine.MatchTime, r.Engine.Map.ZoneAt(trasher.Position), trasher.Position, null, null));
            int meetingsBefore = r.MeetingsHeld;

            for (int d = 0; d < 60; d++)
            {
                r.WriteFrames(buf);
                Assert.False(!r.AgentCommitted(0) && StayOnly(buf, r, 0), "frame " + d + " is a placeholder");
                r.Advance(new int[r.AgentCount]);
            }

            Assert.Equal(meetingsBefore, r.MeetingsHeld);
        }

        [Fact]
        public void HostTrasher_IsNotForced()
        {
            MatchRunner r = Runner("polluter", 1f);
            byte[] buf = AdvanceToQuiet(r);
            byte seat = r.AgentSeat(0);
            r.WriteFrames(buf);
            r.Advance(new int[r.AgentCount]);
            PlayerState trasher = r.Engine.Find(seat)!;
            r.Engine.Witnesses.Add(new WitnessRecord(ActKind.Repollute, seat, seat, r.Engine.MatchTime, r.Engine.Map.ZoneAt(trasher.Position), trasher.Position, null, null));
            Assert.True(r.Engine.Apply(seat, new SabotageCommand(SabotageKind.Machine)).IsOk);

            int placeholders = 0;
            for (int d = 0; d < 40; d++)
            {
                r.WriteFrames(buf);
                if (!r.AgentCommitted(0) && StayOnly(buf, r, 0))
                {
                    placeholders++;
                }

                r.Advance(new int[r.AgentCount]);
            }

            Assert.Equal(0, placeholders);
        }

        // ---------- the setting ----------

        [Fact]
        public void Setting_DefaultsToSeventyPercent_AndIsReadFromTheJsonKey()
        {
            Assert.Equal(0.7f, new BotsSettings().EmergencyOnWitnessChance);
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{ \"emergency_on_witness_chance\": 0.25 }");
                Assert.Equal(0.25f, BotsSettings.Load(path).EmergencyOnWitnessChance);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
