using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Bots;
using Uncycled.Server.Bots;
using Uncycled.Server.Content;
using Uncycled.Server.Net;
using Uncycled.Server.Rooms;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>Wire, content and protocol side of typed chat and ghost chat.</summary>
    public class ChatTextWireTests
    {
        private static ClientMessage Parse(string json)
        {
            Assert.True(Codec.TryParse(Encoding.UTF8.GetBytes(json), out ClientMessage? msg));
            return msg!;
        }

        private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

        private static readonly IReadOnlyList<ChatArg> NoArgs = new List<ChatArg>();

        [Fact]
        public void Codec_DecodesChatText()
        {
            Assert.True(Codec.TryToCommand(Parse("{\"t\":\"chat_text\",\"text\":\"I was at the bins\"}"), out ICommand? cmd, out string? error));
            Assert.Null(error);
            Assert.Equal("I was at the bins", Assert.IsType<ChatTextCommand>(cmd).Text);
        }

        [Theory]
        [InlineData("{\"t\":\"chat_text\"}")]
        [InlineData("{\"t\":\"chat_text\",\"text\":42}")]
        [InlineData("{\"t\":\"chat_text\",\"text\":null}")]
        public void Codec_RejectsChatTextWithoutAString(string json)
        {
            Assert.False(Codec.TryToCommand(Parse(json), out _, out string? error));
            Assert.Equal("bad_field", error);
        }

        [Fact]
        public void Codec_RejectsOverlongRawText()
        {
            string ok = "{\"t\":\"chat_text\",\"text\":\"" + new string('a', 1024) + "\"}";
            Assert.True(Codec.TryToCommand(Parse(ok), out _, out _));

            string tooLong = "{\"t\":\"chat_text\",\"text\":\"" + new string('a', 1025) + "\"}";
            Assert.False(Codec.TryToCommand(Parse(tooLong), out ICommand? cmd, out string? error));
            Assert.Null(cmd);
            Assert.Equal("bad_field", error);
        }

        [Fact]
        public void Codec_DoesNotTrimOrJudgeTheTextItself()
        {
            // the engine normalises and rejects (too_long, empty); the codec only guards the raw size
            Assert.True(Codec.TryToCommand(Parse("{\"t\":\"chat_text\",\"text\":\"  hi \\u0000 \"}"), out ICommand? cmd, out _));
            Assert.Equal("  hi \0 ", Assert.IsType<ChatTextCommand>(cmd).Text);
        }

        [Fact]
        public void Wire_ChatTextEventShape()
        {
            var ev = new ChatEvent(3, null, NoArgs, "I was at the bins", false, null);
            JsonElement j = Json(Wire.Event(ev)!);
            Assert.Equal("chat", j.GetProperty("t").GetString());
            Assert.Equal(3, j.GetProperty("seat").GetInt32());
            Assert.Equal("I was at the bins", j.GetProperty("text").GetString());
            Assert.False(j.TryGetProperty("id", out _));
            Assert.False(j.TryGetProperty("args", out _));
            Assert.False(j.TryGetProperty("ghost", out _));
        }

        [Fact]
        public void Wire_PhraseEventKeepsIdAndArgsAndHasNoText()
        {
            var ev = new ChatEvent(2, "saw_dump", new List<ChatArg> { ChatArg.ForSeat(4), ChatArg.ForZone("market") });
            JsonElement j = Json(Wire.Event(ev)!);
            Assert.Equal("saw_dump", j.GetProperty("id").GetString());
            Assert.Equal(4, j.GetProperty("args")[0].GetInt32());
            Assert.Equal("market", j.GetProperty("args")[1].GetString());
            Assert.False(j.TryGetProperty("text", out _));
            Assert.False(j.TryGetProperty("ghost", out _));
        }

        [Fact]
        public void Wire_GhostFlag()
        {
            var typed = new ChatEvent(5, null, NoArgs, "boo", true, new List<byte> { 5, 6 });
            JsonElement t = Json(Wire.Event(typed)!);
            Assert.True(t.GetProperty("ghost").GetBoolean());
            Assert.Equal("boo", t.GetProperty("text").GetString());
            Assert.False(t.TryGetProperty("id", out _));

            var phrase = new ChatEvent(5, "hmm", NoArgs, null, true, new List<byte> { 5, 6 });
            JsonElement p = Json(Wire.Event(phrase)!);
            Assert.True(p.GetProperty("ghost").GetBoolean());
            Assert.Equal("hmm", p.GetProperty("id").GetString());
            Assert.Equal(0, p.GetProperty("args").GetArrayLength());
            Assert.False(p.TryGetProperty("text", out _));
        }

        private static SeatView WithMeetingAndGhost(SeatView v, int myText, IReadOnlyList<ChatEvent> ghost)
        {
            var m = new MeetingView(MeetingStage.Discussion, 20f, 0, MeetingCause.Emergency, new List<ClueInfo>(), new List<byte>(), false, null, 0f, 2, new List<byte>(), myText);
            return new SeatView(v.Seat, v.Phase, v.PhaseTimeLeft, v.MatchTime, v.DeadlineS, v.DeadlineClock, v.DeadlineClockValue, v.Meter, v.EmergenciesLeft, v.Me, v.Players, v.Piles, v.Sabotage, m, v.GameOver, ghost);
        }

        [Fact]
        public void Welcome_CarriesMyTextAndMyChat()
        {
            MatchEngine engine = SnapshotLeakTests.BuildEngine(3UL);
            engine.Tick(0.05f);
            SeatView view = WithMeetingAndGhost(engine.ViewFor(0), 3, new List<ChatEvent>());

            JsonElement w = Json(Wire.Welcome("BCDF", 0, "tok", 0, SnapshotLeakTests.MinimalBundle(), "meeting", view, new List<Seat>(), _ => false));
            JsonElement meeting = w.GetProperty("state").GetProperty("meeting");
            Assert.Equal(3, meeting.GetProperty("my_text").GetInt32());
            Assert.Equal(2, meeting.GetProperty("my_chat").GetInt32());
            Assert.False(w.TryGetProperty("ghost_chat", out _), "a living seat's welcome has no ghost_chat");
        }

        [Fact]
        public void Welcome_OutSeatGetsGhostChatLines()
        {
            MatchEngine engine = SnapshotLeakTests.BuildEngine(3UL);
            engine.Tick(0.05f);
            SeatView alive = engine.ViewFor(0);
            var me = new MyView(alive.Me.Seat, alive.Me.Role, alive.Me.Partners, alive.Me.Signature, PlayerStatus.Out, alive.Me.Position, alive.Me.Vision, alive.Me.Poison, alive.Me.Cooldowns, alive.Me.Tasks);
            var lines = new List<ChatEvent>
            {
                new ChatEvent(4, null, NoArgs, "first", true, new List<byte> { 0, 4 }),
                new ChatEvent(0, "hmm", NoArgs, null, true, new List<byte> { 0, 4 }),
            };
            var view = new SeatView(alive.Seat, alive.Phase, alive.PhaseTimeLeft, alive.MatchTime, alive.DeadlineS, alive.DeadlineClock, alive.DeadlineClockValue, alive.Meter, alive.EmergenciesLeft, me, alive.Players, alive.Piles, alive.Sabotage, alive.Meeting, alive.GameOver, lines);

            JsonElement w = Json(Wire.Welcome("BCDF", 0, "tok", 0, SnapshotLeakTests.MinimalBundle(), "playing", view, new List<Seat>(), _ => false));
            JsonElement ghost = w.GetProperty("ghost_chat");
            Assert.Equal(2, ghost.GetArrayLength());
            Assert.Equal("chat", ghost[0].GetProperty("t").GetString());
            Assert.Equal(4, ghost[0].GetProperty("seat").GetInt32());
            Assert.Equal("first", ghost[0].GetProperty("text").GetString());
            Assert.True(ghost[0].GetProperty("ghost").GetBoolean());
            Assert.Equal("hmm", ghost[1].GetProperty("id").GetString());
            Assert.True(ghost[1].GetProperty("ghost").GetBoolean());
        }

        [Fact]
        public void Content_LoadsChatFilterAndTuningKeys()
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            Assert.Equal("***", content.Filter.Apply("fuck"));
            Assert.Equal("meet me at the cafe", content.Filter.Apply("meet me at the cafe"));
            Assert.Equal("what the *** man", content.Filter.Apply("what the shit man"));

            using JsonDocument tuning = JsonDocument.Parse(content.TuningJson);
            JsonElement meeting = tuning.RootElement.GetProperty("meeting");
            Assert.Equal(5, meeting.GetProperty("phrases_per_meeting").GetInt32());
            Assert.Equal(120, meeting.GetProperty("text_max_chars").GetInt32());
            Assert.Equal(5, meeting.GetProperty("text_per_meeting").GetInt32());
            Assert.Equal(2, meeting.GetProperty("chat_min_gap_s").GetDouble());

            using JsonDocument custom = JsonDocument.Parse(
                "{\"meeting\":{\"phrases_per_meeting\":3,\"text_max_chars\":50,\"text_per_meeting\":2,\"chat_min_gap_s\":4.5}}");
            MatchConfig cfg = ContentLoader.ReadConfig(custom.RootElement);
            Assert.Equal(3, cfg.PhrasesPerMeeting);
            Assert.Equal(50, cfg.TextMaxChars);
            Assert.Equal(2, cfg.TextPerMeeting);
            Assert.Equal(4.5f, cfg.ChatMinGapS);
        }

        [Fact]
        public void Content_FilterListHasNoInnocentSubstringTraps()
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            foreach (string word in new[] { "class", "assassin", "Scunthorpe", "cocktail", "peacock", "bass", "password", "classic", "hello", "analysis", "grape", "therapist" })
            {
                Assert.Equal(word, content.Filter.Apply(word));
            }
        }

        [Fact]
        public void Protocol_VersionIsOne()
        {
            Assert.Equal(1, Codec.ProtocolVersion);
        }

        // ---------- ghost leak through a real room ----------

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

            public string Token => Json(Socket.AllSent().First(s => s.Contains("\"t\":\"welcome\""))).GetProperty("token").GetString()!;
        }

        private static void Send(RoomRig rig, Human h, string json)
        {
            JsonElement root = JsonDocument.Parse(json).RootElement.Clone();
            string t = root.GetProperty("t").GetString()!;
            rig.Room.Post(new MsgInput(h.Conn, new ClientMessage(t, root)));
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

            return false;
        }

        [Fact]
        public void Ghost_LivingSeatNeverReceivesGhostChat()
        {
            var rig = new RoomRig(c => new BotBrains(new BotsSettings(), RoomRig.EmptyModelsDir(), c.Tasks, NullLogger.Instance));
            var alive = new Human();
            var dead = new Human();
            rig.Room.Post(new JoinInput(alive.Conn, "Alive", null, true));
            rig.Step();
            rig.Room.Post(new JoinInput(dead.Conn, "Dead", null, false));
            rig.Step();
            Send(rig, alive, "{\"t\":\"start\"}");
            rig.Step();
            for (int i = 0; i < 2400 && rig.Engine.Phase != MatchPhase.Playing; i++)
            {
                rig.Step();
            }

            Assert.Equal(MatchPhase.Playing, rig.Engine.Phase);
            PlayerState deadState = rig.Engine.Players.First(p => p.Seat == 1);
            typeof(PlayerState).GetProperty(nameof(PlayerState.Status))!.GetSetMethod(true)!.Invoke(deadState, new object[] { PlayerStatus.Out });

            Send(rig, dead, "{\"t\":\"chat_text\",\"text\":\"ghost secret\"}");
            rig.Step();
            rig.Step();
            Assert.True(WaitUntil(() => dead.Socket.Sent("chat").Count == 1), "the out seat gets its own ghost line");
            JsonElement echo = dead.Socket.Sent("chat")[0].RootElement;
            Assert.True(echo.GetProperty("ghost").GetBoolean());
            Assert.Equal("ghost secret", echo.GetProperty("text").GetString());

            rig.Run(3.0); // snapshots keep flowing to everyone
            Thread.Sleep(300);
            Assert.NotEmpty(alive.Socket.Sent("snapshot"));
            Assert.All(alive.Socket.AllSent(), s => Assert.DoesNotContain("ghost secret", s));
            Assert.Empty(alive.Socket.Sent("chat"));

            // a rejoin: the living seat's welcome carries no ghost data, the out seat's carries the line
            var aliveBack = new Human();
            rig.Room.Post(new JoinInput(aliveBack.Conn, "Alive", alive.Token, false));
            rig.Step();
            var deadBack = new Human();
            rig.Room.Post(new JoinInput(deadBack.Conn, "Dead", dead.Token, false));
            rig.Step();
            Assert.True(WaitUntil(() => aliveBack.Socket.Sent("welcome").Count == 1 && deadBack.Socket.Sent("welcome").Count == 1));

            string aliveWelcome = aliveBack.Socket.AllSent().First(s => s.Contains("\"t\":\"welcome\""));
            Assert.DoesNotContain("ghost secret", aliveWelcome);
            Assert.DoesNotContain("ghost_chat", aliveWelcome);

            JsonElement deadWelcome = Json(deadBack.Socket.AllSent().First(s => s.Contains("\"t\":\"welcome\"")));
            JsonElement lines = deadWelcome.GetProperty("ghost_chat");
            Assert.Equal(1, lines.GetArrayLength());
            Assert.Equal("ghost secret", lines[0].GetProperty("text").GetString());
        }
    }
}
