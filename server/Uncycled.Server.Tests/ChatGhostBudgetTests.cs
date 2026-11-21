using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Server.Content;
using Uncycled.Server.Rooms;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>Size bound of welcome.ghost_chat, the JSON encoding of chat text, and the shipped word filter.</summary>
    public class ChatGhostBudgetTests
    {
        private static readonly IReadOnlyList<ChatArg> NoArgs = new List<ChatArg>();

        private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

        private static SeatView OutViewWith(IReadOnlyList<ChatEvent> lines)
        {
            MatchEngine engine = SnapshotLeakTests.BuildEngine(3UL);
            engine.Tick(0.05f);
            SeatView alive = engine.ViewFor(0);
            var me = new MyView(alive.Me.Seat, alive.Me.Role, alive.Me.Partners, alive.Me.Signature, PlayerStatus.Out, alive.Me.Position, alive.Me.Vision, alive.Me.Poison, alive.Me.Cooldowns, alive.Me.Tasks);
            return new SeatView(alive.Seat, alive.Phase, alive.PhaseTimeLeft, alive.MatchTime, alive.DeadlineS, alive.DeadlineClock, alive.DeadlineClockValue, alive.Meter, alive.EmergenciesLeft, me, alive.Players, alive.Piles, alive.Sabotage, alive.Meeting, alive.GameOver, lines);
        }

        private static List<ChatEvent> GhostLines(int count, Func<int, string> text)
        {
            var lines = new List<ChatEvent>();
            for (int i = 0; i < count; i++)
            {
                lines.Add(new ChatEvent((byte)(i % 8), null, NoArgs, text(i), true, new List<byte> { 0 }));
            }

            return lines;
        }

        private static string Welcome(ContentBundle bundle, SeatView view) =>
            Wire.Welcome("BCDF", 0, "tok", 0, bundle, "playing", view, new List<Seat>(), _ => false);

        [Fact]
        public void Welcome_GhostChatOfEmojiLinesStaysUnderTheClientBuffer()
        {
            // 100 lines of 120 emoji: about 148 KB if every line were sent with every character escaped
            string emoji = string.Concat(Enumerable.Repeat("\U0001F600", 118));
            SeatView view = OutViewWith(GhostLines(100, i => i.ToString("D2") + emoji));

            string welcome = Welcome(SnapshotLeakTests.MinimalBundle(), view);
            Assert.True(Encoding.UTF8.GetByteCount(welcome) < 64 * 1024, "welcome bytes " + Encoding.UTF8.GetByteCount(welcome));

            JsonElement ghost = Json(welcome).GetProperty("ghost_chat");
            int n = ghost.GetArrayLength();
            Assert.InRange(n, 1, 50);
            Assert.True(Encoding.UTF8.GetByteCount(ghost.GetRawText()) <= 16384);
            // the newest lines are kept, oldest first
            Assert.StartsWith("99", ghost[n - 1].GetProperty("text").GetString());
            Assert.StartsWith((100 - n).ToString("D2"), ghost[0].GetProperty("text").GetString());
        }

        [Fact]
        public void Welcome_GhostChatHonoursTheTuningLimits()
        {
            MatchEngine engine = SnapshotLeakTests.BuildEngine(1UL);
            SeatView view = OutViewWith(GhostLines(5, i => "line " + i));

            var byLines = new ContentBundle(new MatchConfig { GhostWelcomeMaxLines = 2 }, "{}", new List<TaskTypeDef>(), "[]", engine.Map);
            JsonElement ghost = Json(Welcome(byLines, view)).GetProperty("ghost_chat");
            Assert.Equal(2, ghost.GetArrayLength());
            Assert.Equal("line 3", ghost[0].GetProperty("text").GetString());
            Assert.Equal("line 4", ghost[1].GetProperty("text").GetString());

            var byBytes = new ContentBundle(new MatchConfig { GhostWelcomeMaxBytes = 100 }, "{}", new List<TaskTypeDef>(), "[]", engine.Map);
            JsonElement g2 = Json(Welcome(byBytes, view)).GetProperty("ghost_chat");
            Assert.InRange(g2.GetArrayLength(), 1, 2);
            Assert.True(Encoding.UTF8.GetByteCount(g2.GetRawText()) <= 100);
            Assert.Equal("line 4", g2[g2.GetArrayLength() - 1].GetProperty("text").GetString());
        }

        [Fact]
        public void Wire_NonAsciiTextIsNotEscapedButMarkupCharactersAre()
        {
            const string text = "café مرحبا <b>&";
            string raw = Wire.Event(new ChatEvent(1, null, NoArgs, text, false, null))!;
            Assert.Equal(text, Json(raw).GetProperty("text").GetString());
            Assert.Contains("café", raw);
            Assert.Contains("م", raw);
            Assert.DoesNotContain("<", raw);
            Assert.DoesNotContain("&", raw);
        }

        [Fact]
        public void Content_LoadsGhostTuningKeys()
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            using JsonDocument tuning = JsonDocument.Parse(content.TuningJson);
            JsonElement meeting = tuning.RootElement.GetProperty("meeting");
            Assert.Equal(100, meeting.GetProperty("ghost_chat_keep").GetInt32());
            Assert.Equal(50, meeting.GetProperty("ghost_welcome_max_lines").GetInt32());
            Assert.Equal(16384, meeting.GetProperty("ghost_welcome_max_bytes").GetInt32());

            using JsonDocument custom = JsonDocument.Parse("{\"meeting\":{\"ghost_chat_keep\":7,\"ghost_welcome_max_lines\":3,\"ghost_welcome_max_bytes\":999}}");
            MatchConfig cfg = ContentLoader.ReadConfig(custom.RootElement);
            Assert.Equal(7, cfg.GhostChatKeep);
            Assert.Equal(3, cfg.GhostWelcomeMaxLines);
            Assert.Equal(999, cfg.GhostWelcomeMaxBytes);
        }

        [Theory]
        [InlineData("fuckin")]
        [InlineData("fucking")]
        [InlineData("fucker")]
        [InlineData("fuckers")]
        [InlineData("fucked")]
        [InlineData("cunts")]
        [InlineData("dicks")]
        [InlineData("cocks")]
        [InlineData("shitting")]
        [InlineData("shitty")]
        [InlineData("motherfucking")]
        [InlineData("bitches")]
        [InlineData("fck")]
        [InlineData("fuk")]
        [InlineData("stfu")]
        [InlineData("wtf")]
        [InlineData("WTF")]
        public void Content_FilterMasksInflectionsAndShortForms(string word)
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            Assert.Equal("you ***", content.Filter.Apply("you " + word));
        }

        [Theory]
        [InlineData("scrap")]
        [InlineData("scrapped")]
        [InlineData("Scunthorpe")]
        [InlineData("cocktail")]
        [InlineData("dickens")]
        [InlineData("assess")]
        [InlineData("raccoon")]
        [InlineData("prickly")]
        [InlineData("therapist")]
        [InlineData("trash")]
        [InlineData("dump")]
        [InlineData("bin")]
        [InlineData("retard")]
        [InlineData("douche")]
        public void Content_FilterLeavesOrdinaryWordsAlone(string word)
        {
            // retard (late) and douche (shower) are ordinary French words and the client is EN + FR
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            Assert.Equal("a " + word + " b", content.Filter.Apply("a " + word + " b"));
        }
    }
}
