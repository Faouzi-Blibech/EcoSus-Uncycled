using System;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots;
using Uncycled.Server.Bots;
using Uncycled.Server.Rooms;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>A room whose last human is gone follows the room lifecycle (docs/architecture/02-server.md): lobby after game over, and closed when no human is left.</summary>
    public class RoomEmptyTests
    {
        private static RoomRig PlayUntilGameOver(out bool ended)
        {
            var rig = new RoomRig(c => new BotBrains(new BotsSettings(), RoomRig.EmptyModelsDir(), c.Tasks, new CountingLog()));
            rig.JoinAndStart();
            int maxSteps = (int)((rig.Content.Config.DeadlineS * 3f + 300f) / RoomRig.Dt);
            ended = false;
            for (int i = 0; i < maxSteps && !ended; i++)
            {
                rig.Step();
                ended = rig.Room.Engine?.Phase == MatchPhase.Ended;
            }

            return rig;
        }

        [Theory]
        [InlineData("leave")]
        [InlineData("drop")]
        public void A_room_whose_last_human_is_gone_at_game_over_closes_at_once_and_does_not_stay_in_the_match(string how)
        {
            RoomRig rig = PlayUntilGameOver(out bool ended);
            Assert.True(ended, "the match never ended");
            if (how == "leave")
            {
                rig.Send(rig.Human, "leave");
            }
            else
            {
                rig.Room.NotifyDisconnect(rig.Human);
            }

            rig.Step();

            Assert.Equal(0, rig.Room.HumanCount);
            Assert.True(rig.Room.IsClosed, "nobody is left to see the result: the room closes now, not after the idle limits");
        }

        [Fact]
        public void A_room_with_a_human_at_game_over_returns_to_the_lobby_and_stays_open()
        {
            RoomRig rig = PlayUntilGameOver(out bool ended);
            Assert.True(ended, "the match never ended");

            rig.Run(rig.Content.Config.ReturnToLobbyS + 2);

            Assert.False(rig.Room.InMatch, "the room must return to the lobby after game over");
            Assert.False(rig.Room.IsClosed);
            Assert.Equal(1, rig.Room.HumanCount);
        }
    }
}
