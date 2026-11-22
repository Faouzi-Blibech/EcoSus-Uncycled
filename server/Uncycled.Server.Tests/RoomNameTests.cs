using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Uncycled.Bots;
using Uncycled.Server.Bots;
using Uncycled.Server.Content;
using Uncycled.Server.Net;
using Uncycled.Server.Rooms;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>A socket that records what the server sends; the room and the connection run for real.</summary>
    internal sealed class FakeSocket : WebSocket
    {
        private readonly object _lock = new object();
        private readonly List<string> _sent = new List<string>();
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => _state;

        public override string? SubProtocol => null;

        /// <summary>Every message sent so far, raw.</summary>
        public List<string> AllSent()
        {
            lock (_lock)
            {
                return new List<string>(_sent);
            }
        }

        public List<JsonDocument> Sent(string type)
        {
            var list = new List<JsonDocument>();
            lock (_lock)
            {
                foreach (string s in _sent)
                {
                    JsonDocument d = JsonDocument.Parse(s);
                    if (d.RootElement.GetProperty("t").GetString() == type)
                    {
                        list.Add(d);
                    }
                }
            }

            return list;
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _sent.Add(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            }

            return Task.CompletedTask;
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override void Abort() => _state = WebSocketState.Aborted;

        public override void Dispose() => _state = WebSocketState.Closed;
    }

    /// <summary>Design 5.9 through the real Room: the join path must apply the name rule.</summary>
    public class RoomNameTests
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

            public JsonElement Welcome(int index = 0)
            {
                JsonDocument? doc = WaitFor(() =>
                {
                    List<JsonDocument> all = Socket.Sent("welcome");
                    return all.Count > index ? all[index] : null;
                });
                Assert.NotNull(doc);
                return doc!.RootElement;
            }

            /// <summary>The name welcome.seats lists for the seat the welcome was addressed to.</summary>
            public string OwnName(JsonElement welcome)
            {
                int seat = welcome.GetProperty("seat").GetInt32();
                foreach (JsonElement s in welcome.GetProperty("seats").EnumerateArray())
                {
                    if (s.GetProperty("seat").GetInt32() == seat)
                    {
                        return s.GetProperty("name").GetString()!;
                    }
                }

                throw new InvalidOperationException("own seat not listed");
            }
        }

        private static T? WaitFor<T>(Func<T?> probe)
            where T : class
        {
            DateTime end = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < end)
            {
                T? r = probe();
                if (r != null)
                {
                    return r;
                }

                Thread.Sleep(20);
            }

            return null;
        }

        private static Room NewRoom()
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            var brains = new BotBrains(new BotsSettings(), RoomRig.EmptyModelsDir(), content.Tasks, NullLogger.Instance);
            var manager = new RoomManager(content, 4, NullLoggerFactory.Instance, brains);
            var room = new Room("BCDF", content, manager, NullLogger.Instance, brains);
            room.Start();
            return room;
        }

        [Fact]
        public void A_joining_name_that_is_taken_gets_a_suffix_and_a_token_rejoin_keeps_its_name()
        {
            Room room = NewRoom();
            var host = new Client();
            var second = new Client();
            var third = new Client();

            room.Post(new JoinInput(host.Conn, "Sam", null, true));
            Assert.Equal("Sam", host.OwnName(host.Welcome()));
            room.Post(new JoinInput(second.Conn, "sam", null, false));
            JsonElement w2 = second.Welcome();
            Assert.Equal("sam 2", second.OwnName(w2));
            room.Post(new JoinInput(third.Conn, "SAM", null, false));
            Assert.Equal("SAM 3", third.OwnName(third.Welcome()));

            // the second player drops and comes back with the token under another typed name: the seat keeps its name
            string token = w2.GetProperty("token").GetString()!;
            var back = new Client();
            room.Post(new JoinInput(back.Conn, "Zed", token, false));
            Assert.Equal("sam 2", back.OwnName(back.Welcome()));
        }

        [Fact]
        public void A_bot_added_at_start_does_not_take_a_humans_name()
        {
            Room room = NewRoom();
            var host = new Client();
            room.Post(new JoinInput(host.Conn, "Bot 2", null, true)); // seat 0; the bot of seat 1 would be "Bot 2"
            host.Welcome();

            using JsonDocument start = JsonDocument.Parse("{\"t\":\"start\"}");
            room.Post(new MsgInput(host.Conn, new ClientMessage("start", start.RootElement.Clone())));

            JsonDocument? started = WaitFor(() =>
            {
                List<JsonDocument> all = host.Socket.Sent("match_started");
                return all.Count > 0 ? all[0] : null;
            });
            Assert.NotNull(started);
            var names = new List<string>();
            foreach (JsonElement s in started!.RootElement.GetProperty("seats").EnumerateArray())
            {
                names.Add(s.GetProperty("name").GetString()!);
            }

            Assert.Equal(room.Size, names.Count);
            Assert.Equal(names.Count, new HashSet<string>(names, StringComparer.OrdinalIgnoreCase).Count);
            Assert.Contains("Bot 2", names);
        }
    }
}
