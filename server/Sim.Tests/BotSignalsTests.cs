using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Xunit;
using static Sim.Tests.TestWorld;

namespace Sim.Tests
{
    /// <summary>What a bot Cleaner needs to see for the v2 rules: the machine jam in its view and a re-pollute in the witness log.</summary>
    public class BotSignalsTests
    {
        private static MatchConfig Open() => new MatchConfig { TrasherStartCooldownS = 0f };

        [Fact]
        public void View_MachineJammed_IsTrueForEverySeatWhileTheMachineIsJammed()
        {
            MatchEngine e = Playing(Open());
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            e.Tick(0f);
            Assert.False(e.ViewFor(cit).MachineJammed);

            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Machine)).IsOk);

            Assert.True(e.ViewFor(cit).MachineJammed);
            Assert.True(e.ViewFor(pol).MachineJammed);
        }

        [Fact]
        public void Repollute_LeavesAWitnessRecordForEverySeatThatSawIt_AndNoneForTheBlind()
        {
            MatchEngine e = Playing(Open());
            byte pol = Polluter(e);
            byte seen = Citizen(e, 0);
            byte far = Citizen(e, 1);
            ParkOthers(e, pol, seen, far);
            StandAt(e, pol, "bins");
            e.TestTeleport(seen, new Vec2(3.5f, 5.5f));
            e.Spots.Find("bins")!.State = SpotState.Clean;
            e.Tick(0f);

            Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk);

            WitnessRecord r = Assert.Single(e.Witnesses.SeenBy(seen));
            Assert.Equal(ActKind.Repollute, r.Act);
            Assert.Equal(pol, r.Actor);
            Assert.Equal(e.Spots.Find("bins")!.Position, r.ActorPosition);
            Assert.Null(r.PileId);
            Assert.Empty(e.Witnesses.SeenBy(far));
        }
    }
}
