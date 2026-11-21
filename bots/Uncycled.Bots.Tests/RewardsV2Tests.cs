using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Uncycled.Train;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>v2 rewards per role (RewardsV2), driven by synthetic engine events.</summary>
    public sealed class RewardsV2Tests
    {
        /// <summary>Learner seats 0-3 are Cleaners and 4-5 are Trashers; which of them count as learners follows the learner role.</summary>
        private sealed class Rig
        {
            private readonly RewardWeights _w = new RewardWeights();
            private readonly RewardsV2 _rewards;
            private readonly HashSet<byte> _learners;

            public Rig(Role learner, params byte[] learnerSeats)
            {
                _rewards = new RewardsV2(_w, learner);
                _learners = new HashSet<byte>(learnerSeats);
                Seen = _ => false;
            }

            public Dictionary<byte, float> Paid { get; } = new Dictionary<byte, float>();

            public Dictionary<byte, Vec2> Position { get; } = new Dictionary<byte, Vec2>();

            public Func<CueEvent, bool> Seen { get; set; }

            public RewardsV2 Rewards => _rewards;

            public void Tick(ushort fixHolders, params GameEvent[] events)
            {
                _rewards.Tick(events, s => _learners.Contains(s), Seen, s => Position.TryGetValue(s, out Vec2 p) ? p : Vec2.Zero, fixHolders, (s, v) =>
                {
                    Paid[s] = (Paid.TryGetValue(s, out float old) ? old : 0f) + v;
                });
            }

            public float Of(byte seat) => Paid.TryGetValue(seat, out float v) ? v : 0f;
        }

        private static void Near(float expected, float actual) => Assert.Equal((double)expected, (double)actual, 4);

        [Fact]
        public void Deliver_PaysPerBag()
        {
            var rig = new Rig(Role.Citizen, 0, 1);
            rig.Tick(0, new DeliveredEvent(0, 3, 0.4f));
            Near(0.3f, rig.Of(0));
            Near(0f, rig.Of(1));
        }

        [Fact]
        public void Spot_PaysSmallAmount()
        {
            var rig = new Rig(Role.Citizen, 0, 1);
            rig.Tick(0, new SpotCleanedEvent(1, "s1", 1));
            Near(0.02f, rig.Of(1));
            Near(0f, rig.Of(0));
        }

        [Fact]
        public void MachineFix_PaysOnlyHolders()
        {
            var rig = new Rig(Role.Citizen, 0, 1);
            rig.Tick(1 << 0, new SabotageEndedEvent(SabotageKind.Machine, SabotageEndResult.Fixed));
            Near(0.05f, rig.Of(0));
            Near(0f, rig.Of(1));
        }

        [Fact]
        public void MachineFix_NotPaidWhenTheJamExpires()
        {
            var rig = new Rig(Role.Citizen, 0, 1);
            rig.Tick(1 << 0, new SabotageEndedEvent(SabotageKind.Machine, SabotageEndResult.Expired));
            Near(0f, rig.Of(0));
        }

        [Fact]
        public void Heal_PaysTheHealer()
        {
            var rig = new Rig(Role.Citizen, 0, 1);
            rig.Tick(0, new HealedEvent(2, 0));
            Near(0.05f, rig.Of(0));
            Near(0f, rig.Of(1));
        }

        [Theory]
        [InlineData(OutCause.Gas)]
        [InlineData(OutCause.Virus)]
        public void CleanerOutByGas_IsPenalised(OutCause cause)
        {
            var rig = new Rig(Role.Citizen, 0, 1);
            rig.Tick(0, new YouAreOutEvent(1, cause));
            Near(-0.1f, rig.Of(1));
            Near(0f, rig.Of(0));
        }

        [Fact]
        public void CleanerOutByVote_IsNotOut()
        {
            var rig = new Rig(Role.Citizen, 0, 1);
            rig.Tick(0, new YouAreOutEvent(0, OutCause.Vote), new EjectionEvent(0, false));
            // ejectwrong to every learner Cleaner, the ejected one included, and nothing from the "out" weight on top
            Near(-0.1f, rig.Of(0));
            Near(-0.1f, rig.Of(1));
        }

        [Fact]
        public void Repollute_UnwitnessedPays_WitnessedDoesNot()
        {
            var rig = new Rig(Role.Polluter, 4, 5);
            rig.Tick(0, new RepolluteOkEvent(4, 10f, 1));
            Near(0.05f, rig.Of(4));

            var watched = new CueEvent(new List<byte> { 0 }, CueKind.Repollute, 5, Vec2.Zero);
            rig.Seen = cue => cue.Actor == 5;
            rig.Tick(0, watched, new RepolluteOkEvent(5, 10f, 1));
            Near(0f, rig.Of(5));

            // a cue the engine sent only to the partner Trasher does not count as seen
            rig.Seen = _ => false;
            rig.Tick(0, new CueEvent(new List<byte> { 4 }, CueKind.Repollute, 5, Vec2.Zero), new RepolluteOkEvent(5, 10f, 2));
            Near(0.05f, rig.Of(5));
        }

        [Fact]
        public void VirusVictim_PaysThePlanter_NotOtherTrashers()
        {
            var rig = new Rig(Role.Polluter, 4, 5);
            rig.Position[4] = new Vec2(10f, 10f);
            rig.Position[5] = new Vec2(40f, 10f);
            rig.Tick(0, new VirusPlantedEvent(4, "a"), new VirusPlantedEvent(5, "b"));

            rig.Position[1] = new Vec2(10.5f, 10f);
            rig.Tick(0, new InfectedEvent(1, 60f));
            Near(0f, rig.Of(4));

            rig.Tick(0, new YouAreOutEvent(1, OutCause.Virus));
            Near(0.1f, rig.Of(4));
            Near(0f, rig.Of(5));
        }

        [Fact]
        public void VirusVictim_NothingPaidWhenNoPlantedSpotIsNear()
        {
            var rig = new Rig(Role.Polluter, 4, 5);
            rig.Position[4] = new Vec2(10f, 10f);
            rig.Tick(0, new VirusPlantedEvent(4, "a"));
            rig.Position[1] = new Vec2(60f, 60f);
            rig.Tick(0, new InfectedEvent(1, 60f), new YouAreOutEvent(1, OutCause.Virus));
            Near(0f, rig.Of(4));
            Near(0f, rig.Of(5));
        }

        [Fact]
        public void GasVictim_PaysTheCaster_UntilGasDone()
        {
            var rig = new Rig(Role.Polluter, 4, 5);
            rig.Rewards.NoteAccepted(4, new GasCommand("room1"));
            rig.Tick(0, new YouAreOutEvent(1, OutCause.Gas), new YouAreOutEvent(2, OutCause.Gas), new GasDoneEvent("room1"));
            Near(0.2f, rig.Of(4));
            Near(0f, rig.Of(5));

            // a later gas death that is no part of that gas pays nobody
            rig.Tick(0, new YouAreOutEvent(3, OutCause.Gas));
            Near(0.2f, rig.Of(4));
        }

        [Fact]
        public void TrasherVotedOut_PaysEjectedAndEjectRight()
        {
            var trasher = new Rig(Role.Polluter, 4, 5);
            trasher.Tick(0, new EjectionEvent(4, true));
            Near(-0.3f, trasher.Of(4));
            Near(0f, trasher.Of(5));

            var cleaner = new Rig(Role.Citizen, 0, 1);
            cleaner.Tick(0, new EjectionEvent(4, true));
            Near(0.1f, cleaner.Of(0));
            Near(0.1f, cleaner.Of(1));
        }

        [Fact]
        public void CleanerVotedOut_PaysCleanerOutAndEjectWrong()
        {
            var trasher = new Rig(Role.Polluter, 4, 5);
            trasher.Tick(0, new EjectionEvent(0, false));
            Near(0.1f, trasher.Of(4));
            Near(0.1f, trasher.Of(5));

            var cleaner = new Rig(Role.Citizen, 0, 1);
            cleaner.Tick(0, new EjectionEvent(0, false));
            Near(-0.1f, cleaner.Of(0));
            Near(-0.1f, cleaner.Of(1));
        }

        [Fact]
        public void Jam_PaysOnlyAnAcceptedMachineSabotage()
        {
            var rig = new Rig(Role.Polluter, 4, 5);
            rig.Rewards.NoteAccepted(4, new SabotageCommand(SabotageKind.Machine));
            rig.Rewards.NoteAccepted(5, new SabotageCommand(SabotageKind.Smog));
            rig.Tick(0, new SabotageStartedEvent(SabotageKind.Machine, null));
            Near(0.02f, rig.Of(4));
            Near(0f, rig.Of(5));

            // paid once
            rig.Tick(0);
            Near(0.02f, rig.Of(4));
        }

        [Fact]
        public void RoleRewards_AreNotPaidToTheOtherRole()
        {
            var rig = new Rig(Role.Citizen, 0, 1);
            rig.Tick(0, new RepolluteOkEvent(4, 10f, 1));
            Near(0f, rig.Of(4));
            var trasher = new Rig(Role.Polluter, 4, 5);
            trasher.Tick(0, new DeliveredEvent(0, 2, 0.1f));
            Near(0f, trasher.Of(0));
        }

        [Fact]
        public void Weights_ComeFromFlags()
        {
            HostOptions o = HostOptions.Parse("--rw-deliver 0.2 --rw-ejectright 0.3 --rw-ejectwrong -0.4 --rw-spot 0.01 --rw-heal 0.06 --rw-out -0.2 --rw-cleanerout 0.15 --rw-repollute 0.07 --rw-victim 0.12 --rw-jam 0.03 --map x --seats 10".Split(' '));
            Near(0.2f, o.Rewards.Deliver);
            Near(0.3f, o.Rewards.Ejectright);
            Near(-0.4f, o.Rewards.Ejectwrong);
            Near(0.01f, o.Rewards.Spot);
            Near(0.06f, o.Rewards.Heal);
            Near(-0.2f, o.Rewards.Out);
            Near(0.15f, o.Rewards.Cleanerout);
            Near(0.07f, o.Rewards.Repollute);
            Near(0.12f, o.Rewards.Victim);
            Near(0.03f, o.Rewards.Jam);
            Assert.Equal("x", o.MapId);
            Assert.Equal(10, o.Seats);
        }

        [Fact]
        public void Weights_DefaultsAreTheBriefValues()
        {
            RewardWeights w = HostOptions.Parse(Array.Empty<string>()).Rewards;
            Near(0.1f, w.Report);
            Near(0.1f, w.Deliver);
            Near(0.02f, w.Spot);
            Near(0.05f, w.Fix);
            Near(-0.3f, w.Ejected);
            Near(-0.1f, w.Ejectwrong);
            Near(-0.1f, w.Out);
            Assert.Equal(ContentLoaderDefaultMap, HostOptions.Parse(Array.Empty<string>()).MapId);
            Assert.Null(HostOptions.Parse(Array.Empty<string>()).Seats);
        }

        private const string ContentLoaderDefaultMap = Uncycled.Server.Content.ContentLoader.DefaultMapId;

        [Fact]
        public void Seats_ResizeTheContentConfig()
        {
            HostOptions o = HostOptions.Parse(new[] { "--seats", "6" });
            Uncycled.Server.Content.ContentBundle content = o.LoadContent();
            Assert.Equal(6, content.Config.Seats);
            Assert.Equal(content.Config.TrashersFor(6), content.Config.Polluters);
        }
    }
}
