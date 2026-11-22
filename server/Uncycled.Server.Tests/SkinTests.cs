using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Uncycled.Bots;
using Uncycled.Server.Bots;
using Uncycled.Server.Content;
using Uncycled.Server.Net;
using Uncycled.Server.Rooms;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>Protocol 3: set_skin in the lobby, skin in the seats of lobby, welcome and match_started.
    /// Valid ids come from content/skins.json (data): the tests never name a skin.</summary>
    public class SkinTests
    {
        private sealed class Client
        {
            public Client()
            {
                Socket = new FakeSocket();
                Conn = new Connection(Socket, NullLogger.Instance);
                _ = Conn.RunSendLoopAsync();
            }

            public FakeSocket Socket { get; }

            public Connection Conn { get; }

            public JsonElement? Last(string type, Func<JsonElement, bool>? where = null)
            {
                DateTime end = DateTime.UtcNow.AddSeconds(3);
                while (DateTime.UtcNow < end)
                {
                    List<JsonDocument> all = Socket.Sent(type);
                    for (int i = all.Count - 1; i >= 0; i--)
                    {
                        if (where == null || where(all[i].RootElement))
                        {
                            return all[i].RootElement;
                        }
                    }

                    Thread.Sleep(10);
                }

                return null;
            }
        }

        private static RoomRig NewRig() =>
            new RoomRig(c => new BotBrains(new BotsSettings(), RoomRig.EmptyModelsDir(), c.Tasks, NullLogger.Instance));

        private static void Send(RoomRig rig, Client c, string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            string t = doc.RootElement.GetProperty("t").GetString()!;
            rig.Room.Post(new MsgInput(c.Conn, new ClientMessage(t, doc.RootElement.Clone())));
            rig.Step();
        }

        private static Client Join(RoomRig rig, bool isCreate)
        {
            var c = new Client();
            rig.Room.Post(new JoinInput(c.Conn, isCreate ? "Host" : "Guest", null, isCreate));
            rig.Step();
            return c;
        }

        private static string AnySkin(RoomRig rig)
        {
            Assert.NotEmpty(rig.Content.SkinIds);
            return rig.Content.SkinIds.OrderBy(s => s, StringComparer.Ordinal).First();
        }

        private static string? SeatSkin(JsonElement msg, int seat) =>
            msg.GetProperty("seats").EnumerateArray()
                .Where(s => s.GetProperty("seat").GetInt32() == seat)
                .Select(s => s.GetProperty("skin").GetString())
                .FirstOrDefault();

        /// <summary>Seat 0's skin in the newest lobby message the client got.</summary>
        private static string? LobbySkin(Client c) => c.Last("lobby") is JsonElement l ? SeatSkin(l, 0) : null;

        private static string? RejectReason(Client c) =>
            c.Last("action_rejected", e => e.GetProperty("action").GetString() == "set_skin")?.GetProperty("reason").GetString();

        [Fact]
        public void SetSkin_InLobby_ShowsInLobbySeats()
        {
            RoomRig rig = NewRig();
            Client host = Join(rig, true);
            Client guest = Join(rig, false);
            string skin = AnySkin(rig);

            Send(rig, host, "{\"t\":\"set_skin\",\"skin\":\"" + skin + "\"}");

            JsonElement? lobby = guest.Last("lobby", e => SeatSkin(e, 0) == skin);
            Assert.NotNull(lobby);
            Assert.Equal("", SeatSkin(lobby!.Value, 1));
        }

        [Fact]
        public void SetSkin_UnknownId_TargetInvalid()
        {
            RoomRig rig = NewRig();
            Client host = Join(rig, true);

            Send(rig, host, "{\"t\":\"set_skin\",\"skin\":\"no_such_skin_id\"}");

            Assert.Equal("target_invalid", RejectReason(host));
            Assert.Equal("", LobbySkin(host));
        }

        [Fact]
        public void SetSkin_Empty_BackToDefault()
        {
            RoomRig rig = NewRig();
            Client host = Join(rig, true);
            Send(rig, host, "{\"t\":\"set_skin\",\"skin\":\"" + AnySkin(rig) + "\"}");

            Send(rig, host, "{\"t\":\"set_skin\",\"skin\":\"\"}");

            Assert.Equal("", LobbySkin(host));
        }

        [Fact]
        public void SetSkin_NotAString_BadField()
        {
            RoomRig rig = NewRig();
            Client host = Join(rig, true);

            Send(rig, host, "{\"t\":\"set_skin\",\"skin\":5}");

            Assert.NotNull(host.Last("error", e => e.GetRawText().Contains("set_skin")));
            Assert.Equal("", LobbySkin(host));
        }

        [Fact]
        public void SetSkin_DuringMatch_WrongPhase_AndMatchStartedCarriesSkin()
        {
            RoomRig rig = NewRig();
            Client host = Join(rig, true);
            string skin = AnySkin(rig);
            Send(rig, host, "{\"t\":\"set_skin\",\"skin\":\"" + skin + "\"}");

            Send(rig, host, "{\"t\":\"start\"}");
            JsonElement? started = host.Last("match_started");
            Assert.NotNull(started);
            Assert.Equal(skin, SeatSkin(started!.Value, 0));

            Send(rig, host, "{\"t\":\"set_skin\",\"skin\":\"\"}");
            Assert.Equal("wrong_phase", RejectReason(host));
        }

        [Fact]
        public void Welcome_SeatsCarrySkin()
        {
            RoomRig rig = NewRig();
            Client host = Join(rig, true);
            string skin = AnySkin(rig);
            Send(rig, host, "{\"t\":\"set_skin\",\"skin\":\"" + skin + "\"}");

            Client guest = Join(rig, false);

            JsonElement? welcome = guest.Last("welcome");
            Assert.NotNull(welcome);
            Assert.Equal(skin, SeatSkin(welcome!.Value, 0));
        }

        [Theory]
        [InlineData("{\"skins\":[{\"id\":\"a\"},{\"id\":\"b\",\"streak\":3}]}", "a,b")]
        [InlineData("{\"skins\":[{\"id\":\"\"},{\"name\":\"no id\"},{\"id\":7},\"x\"]}", "")]
        [InlineData("{\"other\":[]}", "")]
        public void ReadSkinIds_TakesEveryId(string json, string expected)
        {
            IReadOnlySet<string> ids = ContentLoader.ReadSkinIds(json);
            Assert.Equal(expected, string.Join(",", ids.OrderBy(s => s, StringComparer.Ordinal)));
        }
    }
}
