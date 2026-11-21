using System;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sim.Core;
using Sim.Match;
using Uncycled.Server.Bots;
using Uncycled.Server.Content;
using Uncycled.Server.Net;
using Uncycled.Server.Rooms;

namespace Uncycled.Server.Tests
{
    internal sealed class CountingLog : ILogger
    {
        public int Warnings;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                Warnings++;
            }
        }
    }

    internal sealed class NullSocket : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => WebSocketState.Open;

        public override string? SubProtocol => null;

        public override void Abort()
        {
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            Task.FromException<WebSocketReceiveResult>(new NotSupportedException());

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// A real Room driven by hand: Step is called with simulated time, so a whole match runs in
    /// about a second. One human (seat 0) joins and starts; the host's start fills the rest with bots.
    /// </summary>
    internal sealed class RoomRig
    {
        public const float Dt = 0.05f;

        private double _now;

        public RoomRig(Func<ContentBundle, BotBrains> makeBrains)
        {
            Content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            Brains = makeBrains(Content);
            Manager = new RoomManager(Content, 4, NullLoggerFactory.Instance, Brains);
            Room = new Room("TEST", Content, Manager, NullLogger.Instance, Brains);
            Human = NewConnection();
        }

        public ContentBundle Content { get; }

        public BotBrains Brains { get; }

        public RoomManager Manager { get; }

        public Room Room { get; }

        public Connection Human { get; set; }

        public MatchEngine Engine => Room.Engine ?? throw new InvalidOperationException("no match is running");

        public static Connection NewConnection() => new Connection(new NullSocket(), NullLogger.Instance);

        public static string EmptyModelsDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uncycled-models-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void Step() => Room.Step(_now += Dt, Dt);

        public void Run(double seconds)
        {
            int n = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < n; i++)
            {
                Step();
            }
        }

        public void Send(Connection conn, string type)
        {
            using JsonDocument doc = JsonDocument.Parse("{\"t\":\"" + type + "\"}");
            Room.Post(new MsgInput(conn, new ClientMessage(type, doc.RootElement.Clone())));
        }

        /// <summary>The human creates the room and starts the match; runs until the round is playing.</summary>
        public void JoinAndStart()
        {
            Room.Post(new JoinInput(Human, "Human", null, true));
            Step();
            Send(Human, "start");
            Step();
            for (int i = 0; i < 2400 && Engine.Phase != MatchPhase.Playing; i++)
            {
                Step();
            }
        }
    }
}
