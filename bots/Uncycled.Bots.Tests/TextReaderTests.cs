using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Uncycled.Bots.Meeting;
using Xunit;
using TextReader = Uncycled.Bots.Meeting.TextReader;

namespace Uncycled.Bots.Tests
{
    /// <summary>The English simple-meaning reader: a typed line becomes the acts of the phrase it says, only when it is clear.</summary>
    public class TextReaderTests
    {
        private const byte Speaker = 0;

        // 0 is the speaker (Yuki); "Bot N" is the display name the room gives a bot, so "bot 3" is that seat.
        private static readonly Dictionary<byte, string> Names = new Dictionary<byte, string>
        {
            { 0, "Yuki" }, { 1, "Sami" }, { 2, "Bot 2" }, { 3, "Bot 3" }, { 4, "Bot 4" }, { 5, "Bot 5" }, { 6, "Hedi" },
        };

        private static ChatWords Words()
        {
            string dir = Uncycled.Server.Content.ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
            return ChatWords.Parse(File.ReadAllText(Path.Combine(dir, "chat_words.json")));
        }

        private static string Read(string text, byte speaker = Speaker)
        {
            IReadOnlyList<ChatAct> acts = TextReader.Read(text, speaker, Names, Words(), 12f);
            return string.Join(";", acts.Select(a => a.PhraseId + "(" + (a.Seat?.ToString() ?? "-") + "," + (a.Zone ?? "-") + ")"));
        }

        [Theory]
        // the clear readings
        [InlineData("bot 3 is sus", "sus(3,-)")]
        [InlineData("Bot 3 is SUS!", "sus(3,-)")]
        [InlineData("Sami is sus", "sus(1,-)")]
        [InlineData("I saw Hedi dump in the market", "saw_dump(6,market)")]
        [InlineData("I saw Hedi dump", "sus(6,-)")]
        [InlineData("I saw Sami re-pollute in the market", "saw_dump(1,market)")]
        [InlineData("i saw sami repollute a spot in the garden", "saw_dump(1,garden)")]
        [InlineData("Sami repolluted the depot", "saw_dump(1,depot)")]
        [InlineData("hedi made it dirty again in the depot", "saw_dump(6,depot)")]
        [InlineData("I saw Hedi dirtied the market spot", "saw_dump(6,market)")]
        [InlineData("bot 4 trashed the garden", "saw_dump(4,garden)")]
        [InlineData("I saw Bot 5 poison someone in the depot", "saw_hit(5,depot)")]
        [InlineData("Hedi was near the body in the garden", "near_pile(6,garden)")]
        [InlineData("I was in the depot", "i_was(-,depot)")]
        [InlineData("I was with Sami in the garden", "with(1,garden)")]
        [InlineData("not me", "not_me(-,-)")]
        [InlineData("It wasn't me", "not_me(-,-)")]
        [InlineData("bot 2 is lying", "lying(2,-)")]
        [InlineData("vote bot 5", "vote(5,-)")]
        [InlineData("skip", "skip_no_proof(-,-)")]
        [InlineData("I was in the depot. Bot 3 is sus.", "i_was(-,depot);sus(3,-)")]
        [InlineData("bot 3 is sus, not bot 4", "sus(3,-)")]
        // not clear: no act
        [InlineData("it's not bot 3", "")]
        [InlineData("bot 3 is not sus", "")]
        [InlineData("I never saw Hedi dump in the market", "")]
        [InlineData("was it bot 3?", "")]
        [InlineData("is bot 3 sus?", "")]
        [InlineData("bot 3 or bot 4", "")]
        [InlineData("bot 3 and bot 4 are sus", "")]
        [InlineData("bot 7 is sus", "")]
        [InlineData("bot 30 is sus", "")]
        [InlineData("somebody is sus", "")]
        [InlineData("yuki is sus", "")]
        [InlineData("vote yuki", "")]
        [InlineData("***", "")]
        [InlineData("*** is sus", "")]
        [InlineData("", "")]
        [InlineData("hello everyone", "")]
        public void Read_Table(string text, string expected)
        {
            Assert.Equal(expected, Read(text));
        }

        [Fact]
        public void Read_SeatsOfTheNameAreReadWhole_NotInsideAnotherName()
        {
            var names = new Dictionary<byte, string> { { 0, "Yuki" }, { 1, "Bot 1" }, { 2, "Bot 10" } };

            IReadOnlyList<ChatAct> acts = TextReader.Read("bot 10 is sus", 0, names, Words(), 1f);

            Assert.Single(acts);
            Assert.Equal((byte?)2, acts[0].Seat);
        }

        [Fact]
        public void Read_CarriesTheSpeakerKindAndTime()
        {
            IReadOnlyList<ChatAct> acts = TextReader.Read("I saw Sami re-pollute in the market", 4, Names, Words(), 33f);

            ChatAct a = Assert.Single(acts);
            Assert.Equal((byte)4, a.Speaker);
            Assert.Equal("accuse", a.Kind);
            Assert.Equal(33f, a.Time);
        }

        [Fact]
        public void Read_EveryActHasTheKindOfItsPhrase()
        {
            string dir = Uncycled.Server.Content.ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
            Sim.Match.PhraseCatalogue catalogue = Sim.Match.PhraseCatalogue.Parse(File.ReadAllText(Path.Combine(dir, "phrases.json")));
            string[] lines =
            {
                "bot 3 is sus", "I saw Hedi dump in the market", "I saw Bot 5 poison someone in the depot", "Hedi was near the body in the garden",
                "I was in the depot", "I was with Sami in the garden", "not me", "bot 2 is lying", "vote bot 5", "skip",
            };

            foreach (string line in lines)
            {
                foreach (ChatAct a in TextReader.Read(line, Speaker, Names, Words(), 1f))
                {
                    Assert.True(catalogue.TryGet(a.PhraseId, out Sim.Match.PhraseInfo info), a.PhraseId + " is not a phrase");
                    Assert.Equal(info.Kind, a.Kind);
                }
            }
        }

        [Fact]
        public void Read_JunkOf500Chars_IsNoActAndFast()
        {
            ChatWords words = Words();
            string junk = string.Concat(Enumerable.Repeat("xq zv 12 !! ", 42)).Substring(0, 500);
            TextReader.Read(junk, Speaker, Names, words, 1f); // warm up

            var sw = Stopwatch.StartNew();
            const int runs = 200;
            for (int i = 0; i < runs; i++)
            {
                Assert.Empty(TextReader.Read(junk, Speaker, Names, words, 1f));
            }

            Assert.True(sw.Elapsed.TotalMilliseconds / runs < 1.0, "one read took " + sw.Elapsed.TotalMilliseconds / runs + " ms");
        }

        [Fact]
        public void ChatWords_EveryMapZoneHasAnEntry()
        {
            string dir = Uncycled.Server.Content.ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
            ChatWords words = Words();
            string[] maps = Directory.GetFiles(Path.Combine(dir, "maps"), "*.json");
            Assert.NotEmpty(maps);
            foreach (string map in maps)
            {
                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(map)))
                {
                    foreach (JsonElement zone in doc.RootElement.GetProperty("zones").EnumerateArray())
                    {
                        string id = zone.GetProperty("id").GetString()!;
                        Assert.True(words.ZoneAliases.TryGetValue(id, out IReadOnlyList<string>? aliases) && aliases.Count > 0, Path.GetFileName(map) + ": zone " + id + " has no entry in chat_words.json");
                    }
                }
            }
        }

        [Fact]
        public void ChatWords_Parse_ReadsEveryListAndTheZones()
        {
            ChatWords words = ChatWords.Parse("{\"version\":1,\"lang\":{\"en\":{\"accuse\":[\"sus\"]},\"xx\":{\"accuse\":[\"foo\"]}},\"zones\":{\"depot\":[\"depot\"]}}");

            Assert.Equal(new[] { "sus", "foo" }, words.List("accuse"));
            Assert.Empty(words.List("vote"));
            Assert.Equal(new[] { "depot" }, words.ZoneAliases["depot"]);
        }
    }
}
