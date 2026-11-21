using Sim.Core;
using Sim.Match;
using Xunit;
using static Sim.Tests.TestWorld;

namespace Sim.Tests
{
    /// <summary>Design 5.1: a dark room cuts a citizen's vision by vision.dark_mult.</summary>
    public class EngineDarkTests
    {
        private static readonly Vec2 InDark = new Vec2(14.5f, 2.5f);
        private static readonly Vec2 OutOfDark = new Vec2(10.5f, 2.5f);

        [Fact]
        public void Citizen_vision_is_halved_inside_a_dark_room_only()
        {
            MatchEngine e = PlayingWithDark();
            byte cit = Citizen(e);

            e.TestTeleport(cit, OutOfDark);
            Assert.Equal(4f, e.VisionOf(P(e, cit)), 3);
            e.TestTeleport(cit, InDark);
            Assert.Equal(2f, e.VisionOf(P(e, cit)), 3);
            Assert.Equal(2f, e.ViewFor(cit).Me.Vision, 3);
        }

        [Fact]
        public void Polluter_vision_is_unchanged_inside_a_dark_room()
        {
            MatchEngine e = PlayingWithDark();
            byte pol = Polluter(e);

            e.TestTeleport(pol, InDark);
            Assert.Equal(5f, e.VisionOf(P(e, pol)), 3);
        }

        [Fact]
        public void Dark_mult_comes_from_the_config()
        {
            MatchEngine e = PlayingWithDark(new MatchConfig { DarkMult = 0.25f });
            byte cit = Citizen(e);

            e.TestTeleport(cit, InDark);
            Assert.Equal(1f, e.VisionOf(P(e, cit)), 3);
        }

        [Fact]
        public void A_citizen_in_the_dark_does_not_see_a_seat_three_tiles_away_but_does_outside()
        {
            MatchEngine e = PlayingWithDark();
            byte cit = Citizen(e);
            byte other = Citizen(e, 1);
            ParkOthers(e, cit, other);
            e.TestTeleport(other, new Vec2(11.5f, 2.5f)); // outside the room, 3 tiles from InDark

            e.TestTeleport(cit, InDark);
            Assert.False(e.CanSee(cit, P(e, other).Position));
            e.TestTeleport(cit, OutOfDark);
            Assert.True(e.CanSee(cit, P(e, other).Position));
        }

        [Theory]
        [InlineData(1f, 2f)]   // smog only eased to 2.5: the dark value (2) is lower
        [InlineData(2.1f, 1f)] // full smog (1): lower than the dark value
        public void Smog_and_dark_together_use_the_lower_value(float smogSeconds, float expected)
        {
            MatchEngine e = PlayingWithDark(new MatchConfig { SabotageStartCooldownS = 0f });
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            e.TestTeleport(cit, InDark);
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Smog)).IsOk);

            Advance(e, smogSeconds);

            Assert.Equal(expected, e.VisionOf(P(e, cit)), 1);
        }

        [Fact]
        public void No_dark_rooms_means_no_change()
        {
            MatchEngine e = Playing();
            byte cit = Citizen(e);
            e.TestTeleport(cit, InDark);
            Assert.Equal(4f, e.VisionOf(P(e, cit)), 3);
        }

        /// <summary>
        /// The rect is half-open, [X, X + Width) x [Y, Y + Height), like the client's Rect2.has_point
        /// (MapRoot.is_dark): the same tile edge must be dark or light on both sides.
        /// </summary>
        [Theory]
        [InlineData(12f, 1f, true)]       // top-left corner: inside
        [InlineData(17.99f, 4.99f, true)] // just inside the far corner
        [InlineData(18f, 2f, false)]      // right edge: outside
        [InlineData(14f, 5f, false)]      // bottom edge: outside
        [InlineData(11.99f, 2f, false)]   // just left of the rect
        public void A_dark_rect_is_half_open(float x, float y, bool dark)
        {
            Assert.Equal(dark, Map(new[] { DarkRoom }).IsDark(new Vec2(x, y)));
        }
    }
}
