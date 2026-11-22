using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Uncycled.Bots;
using Uncycled.Server.Bots;
using Uncycled.Server.Net;
using Uncycled.Server.Rooms;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>Design 4 and 12: the host picks the room size in the lobby, bots fill up to it, the size sets the Trasher count.</summary>
    public class RoomSizeTests
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

        /// <summary>One more human joins the room (the first one creates it and is the host).</summary>
        private static Client Join(RoomRig rig, List<Client> into)
        {
            var c = new Client();
            rig.Room.Post(new JoinInput(c.Conn, "P" + into.Count, null, into.Count == 0));
            rig.Step();
            into.Add(c);
            return c;
        }

        private static List<Client> JoinMany(RoomRig rig, int humans)
        {
            var list = new List<Client>();
            for (int i = 0; i < humans; i++)
            {
                Join(rig, list);
            }

            return list;
        }

        private static string? RejectReason(Client c, string action) =>
            c.Last("action_rejected", e => e.GetProperty("action").GetString() == action)?.GetProperty("reason").GetString();

        private static int[] Sizes(JsonElement e) => e.GetProperty("sizes").EnumerateArray().Select(x => x.GetInt32()).ToArray();

        [Fact]
        public void SetSize_OnlyHostInLobby()
        {
            RoomRig rig = NewRig();
            List<Client> cs = JoinMany(rig, 2);
            Assert.Equal(4, rig.Room.Size);

            Send(rig, cs[1], "{\"t\":\"set_size\",\"size\":6}");
            Assert.Equal(4, rig.Room.Size);
            Assert.Equal("not_allowed", RejectReason(cs[1], "set_size"));

            Send(rig, cs[0], "{\"t\":\"set_size\",\"size\":6}");
            Assert.Equal(6, rig.Room.Size);
            JsonElement? lobby = cs[1].Last("lobby", e => e.GetProperty("size").GetInt32() == 6);
            Assert.NotNull(lobby);
            Assert.Equal(new[] { 4, 5, 6, 7, 8 }, Sizes(lobby!.Value));
        }

        [Fact]
        public void SetSize_BelowHumans_Rejected()
        {
            RoomRig rig = NewRig();
            List<Client> cs = JoinMany(rig, 6);

            Send(rig, cs[0], "{\"t\":\"set_size\",\"size\":5}");

            Assert.Equal(6, rig.Room.Size);
            Assert.Equal("target_invalid", RejectReason(cs[0], "set_size"));
        }

        [Fact]
        public void SetSize_NotInTheSizeList_Rejected()
        {
            RoomRig rig = NewRig();
            List<Client> cs = JoinMany(rig, 1);

            Send(rig, cs[0], "{\"t\":\"set_size\",\"size\":9}");

            Assert.Equal(4, rig.Room.Size);
            Assert.Equal("target_invalid", RejectReason(cs[0], "set_size"));
        }

        [Fact]
        public void Welcome_CarriesSizeAndSizes()
        {
            RoomRig rig = NewRig();
            List<Client> cs = JoinMany(rig, 1);

            JsonElement? w = cs[0].Last("welcome");

            Assert.NotNull(w);
            Assert.Equal(4, w!.Value.GetProperty("size").GetInt32());
            Assert.Equal(new[] { 4, 5, 6, 7, 8 }, Sizes(w.Value));
        }

        /// <summary>Playtest 2026-11-22: the size is a minimum, not a cap. Humans past it raise it; only
        /// the largest size turns players away.</summary>
        [Fact]
        public void Join_PastTheSize_RaisesIt_FullOnlyAtTheLargest()
        {
            RoomRig rig = NewRig();
            List<Client> cs = JoinMany(rig, 5);
            Assert.Equal(5, rig.Room.HumanCount);
            Assert.Equal(5, rig.Room.Size);
            Assert.NotNull(cs[0].Last("lobby", e => e.GetProperty("size").GetInt32() == 5));

            for (int i = 5; i < 8; i++)
            {
                Join(rig, cs);
            }

            Assert.Equal(8, rig.Room.Size);
            var ninth = new Client();
            rig.Room.Post(new JoinInput(ninth.Conn, "Late", null, false));
            rig.Step();

            Assert.Equal("full", ninth.Last("join_failed")?.GetProperty("reason").GetString());
            Assert.Equal(8, rig.Room.HumanCount);
        }

        [Theory]
        [InlineData(4, 3, 4)]
        [InlineData(4, 4, 4)]
        [InlineData(4, 5, 5)]
        [InlineData(6, 2, 6)]
        [InlineData(4, 8, 8)]
        public void SizeForHumans_KeepsTheSizeWhileTheyFit(int current, int humans, int expected) =>
            Assert.Equal(expected, Room.SizeForHumans(new[] { 4, 5, 6, 7, 8 }, current, humans));

        [Fact]
        public void SizeForHumans_PicksTheSmallestListedSizeThatHoldsThem() =>
            Assert.Equal(8, Room.SizeForHumans(new[] { 4, 8, 10 }, 4, 5));

        [Theory]
        [InlineData(4, 1)]
        [InlineData(5, 1)]
        [InlineData(6, 2)]
        [InlineData(8, 2)]
        public void Start_FillsBotsToSize(int size, int trashers)
        {
            RoomRig rig = NewRig();
            List<Client> cs = JoinMany(rig, 1);
            Send(rig, cs[0], "{\"t\":\"set_size\",\"size\":" + size + "}");

            Send(rig, cs[0], "{\"t\":\"start\"}");

            Assert.Equal(size, rig.Room.Seats.Count(s => s.Control != SeatControl.Empty));
            Assert.Equal(size - 1, rig.Room.BotCount);
            int active = 0;
            int polluters = 0;
            foreach (Seat s in rig.Room.Seats)
            {
                Sim.Core.PlayerState? p = rig.Engine.Find(s.Id);
                if (p != null)
                {
                    active++;
                    if (p.IsPolluter)
                    {
                        polluters++;
                    }
                }
            }

            Assert.Equal(size, active);
            Assert.Equal(trashers, polluters);
        }

        [Theory]
        [InlineData("polluter", true)]
        [InlineData("citizen", false)]
        public void DevHostRole_GivesTheHostThatRole(string role, bool polluter)
        {
            for (int i = 0; i < 12; i++)
            {
                RoomRig rig = NewRig();
                rig.Room.DevHostRole = role;
                List<Client> cs = JoinMany(rig, 1);

                Send(rig, cs[0], "{\"t\":\"start\"}");

                Assert.Equal(polluter, rig.Engine.Find(0)!.IsPolluter);
                Assert.Equal(1, rig.Room.Seats.Count(s => rig.Engine.Find(s.Id)?.IsPolluter == true));
            }
        }

        [Fact]
        public void DevHostRole_IsOffByDefault()
        {
            RoomRig rig = NewRig();
            Assert.Null(rig.Room.DevHostRole);
        }
    }
}
