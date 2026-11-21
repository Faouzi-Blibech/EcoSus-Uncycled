using Sim.Core;
using Sim.Uncycled;
using Sim.Match;
using Xunit;
using static Sim.Tests.TestWorld;

namespace Sim.Tests
{
    /// <summary>The gas warning is public (the HUD shows it), so every seat's SeatView carries it.</summary>
    public class GasViewTests
    {
        private static System.Collections.Generic.List<byte> Seats(MatchEngine e)
        {
            var l = new System.Collections.Generic.List<byte>();
            foreach (Sim.Core.PlayerState p in e.Players)
            {
                l.Add(p.Seat);
            }

            return l;
        }

        private static MatchConfig Cfg()
        {
            return new MatchConfig
            {
                TrasherStartCooldownS = 0f,
                UnlockAfter = 1,
                EmergencyMinSinceMeetingS = 0f,
                DiscussionS = 30f,
            };
        }

        private static byte GasTrasher(MatchEngine e)
        {
            byte pol = Polluter(e);
            ParkOthers(e, pol);
            StandAt(e, pol, "bins");
            e.Tick(0f);
            e.Spots.Find("bins")!.State = SpotState.Clean;
            Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk);
            Assert.True(e.Apply(pol, new PickAbilityCommand(TrasherAbility.Gas)).IsOk);
            e.TestTeleport(pol, Corner);
            e.Tick(0f);
            return pol;
        }

        [Fact]
        public void GasView_IsNullWithoutWarning()
        {
            MatchEngine e = Playing(Cfg());
            foreach (byte s in Seats(e))
            {
                Assert.Null(e.ViewFor(s).Gas);
            }
        }

        [Fact]
        public void GasView_ShowsRoomAndSecondsLeft_ToEverySeat()
        {
            var cfg = Cfg();
            MatchEngine e = Playing(cfg);
            byte pol = GasTrasher(e);
            Assert.True(e.Apply(pol, new GasCommand("gas_w")).IsOk);

            foreach (byte s in Seats(e))
            {
                GasView? g = e.ViewFor(s).Gas;
                Assert.NotNull(g);
                Assert.Equal("gas_w", g!.Room);
                Assert.Equal(cfg.GasWarningS, g.InS, 3);
            }

            Advance(e, 1f);
            foreach (byte s in Seats(e))
            {
                Assert.Equal(cfg.GasWarningS - 1f, e.ViewFor(s).Gas!.InS, 2);
            }
        }

        [Fact]
        public void GasView_ClearsWhenGasEnds()
        {
            var cfg = Cfg();
            MatchEngine e = Playing(cfg);
            byte pol = GasTrasher(e);
            Assert.True(e.Apply(pol, new GasCommand("gas_w")).IsOk);

            Advance(e, cfg.GasWarningS + 0.5f);

            foreach (byte s in Seats(e))
            {
                Assert.Null(e.ViewFor(s).Gas);
            }
        }
    }
}
