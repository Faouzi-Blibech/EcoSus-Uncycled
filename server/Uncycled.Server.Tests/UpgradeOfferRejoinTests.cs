using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots;
using Uncycled.Server.Bots;
using Uncycled.Server.Net;
using Uncycled.Server.Rooms;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>An open Recycling Machine offer is never lost: a rejoin gets it again right after the welcome.</summary>
    public class UpgradeOfferRejoinTests
    {
        private sealed class Human
        {
            public Human()
            {
                Socket = new FakeSocket();
                Conn = new Connection(Socket, NullLogger.Instance);
                _ = Conn.RunSendLoopAsync();
            }

            public FakeSocket Socket { get; }

            public Connection Conn { get; }

            public string Token => JsonDocument.Parse(Socket.AllSent().First(s => s.Contains("\"t\":\"welcome\""))).RootElement.GetProperty("token").GetString()!;
        }

        private static bool WaitUntil(Func<bool> probe)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < end)
            {
                if (probe())
                {
                    return true;
                }

                Thread.Sleep(20);
            }

            return probe();
        }

        private static void Set(PlayerState p, string property, object value) =>
            typeof(PlayerState).GetProperty(property)!.GetSetMethod(true)!.Invoke(p, new[] { value });

        [Fact]
        public void Rejoin_WithAnOpenOffer_ResendsItAfterTheWelcome_AndNotWithoutOne()
        {
            var rig = new RoomRig(c => new BotBrains(new BotsSettings(), RoomRig.EmptyModelsDir(), c.Tasks, NullLogger.Instance));
            var first = new Human();
            rig.Room.Post(new JoinInput(first.Conn, "Cleaner", null, true));
            rig.Step();
            rig.Room.Post(new MsgInput(first.Conn, new ClientMessage("start", JsonDocument.Parse("{\"t\":\"start\"}").RootElement.Clone())));
            rig.Step();
            for (int i = 0; i < 2400 && rig.Engine.Phase != MatchPhase.Playing; i++)
            {
                rig.Step();
            }

            Assert.Equal(MatchPhase.Playing, rig.Engine.Phase);

            // seat 0 is a Cleaner with two bags standing at the machine
            MatchEngine engine = rig.Engine;
            PlayerState me = engine.Players.First(p => p.Seat == 0);
            Set(me, nameof(PlayerState.Role), Role.Citizen);
            Set(me, nameof(PlayerState.Bags), 2);
            Set(me, nameof(PlayerState.BagCap), 3);
            Set(me, nameof(PlayerState.Position), engine.Map.FindConsole("machine")!.Position);

            // a rejoin before any delivery: no offer follows the welcome
            Assert.True(WaitUntil(() => first.Socket.Sent("welcome").Count == 1));
            var early = new Human();
            rig.Room.Post(new JoinInput(early.Conn, "Cleaner", first.Token, false));
            rig.Step();
            Assert.True(WaitUntil(() => early.Socket.Sent("welcome").Count == 1));
            Assert.Empty(early.Socket.Sent("upgrade_offer"));

            Assert.True(engine.Apply(0, new UseCommand("machine")).IsOk);
            Assert.NotNull(engine.OpenUpgradeOffer(0));

            // the connection drops and the seat comes back: the same offer arrives after the welcome
            var back = new Human();
            rig.Room.Post(new JoinInput(back.Conn, "Cleaner", early.Token, false));
            rig.Step();
            Assert.True(WaitUntil(() => back.Socket.Sent("upgrade_offer").Count >= 1));
            string[] sent = back.Socket.AllSent().Where(s => s.Contains("\"t\":\"welcome\"") || s.Contains("\"t\":\"upgrade_offer\"")).ToArray();
            Assert.Contains("\"t\":\"welcome\"", sent[0]);
            JsonElement offer = back.Socket.Sent("upgrade_offer")[0].RootElement;
            Assert.Equal(new[] { "backpack", "tool", "heal", "mask" }, offer.GetProperty("items").EnumerateArray().Select(i => i.GetString()!).ToArray());
        }
    }
}
