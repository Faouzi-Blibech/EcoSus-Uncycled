using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Uncycled.Bots.Meeting
{
    /// <summary>
    /// The words the bots look for in a typed chat line (content/chat_words.json): per act, a list of words or
    /// short word groups, and per map zone the names players use for it. Every language key under "lang" is merged,
    /// so another language is added as data only.
    /// </summary>
    public sealed class ChatWords
    {
        private static readonly IReadOnlyList<string> NoWords = new string[0];
        private static readonly string[][] NoTokens = new string[0][];

        private readonly Dictionary<string, List<string>> _lists;
        private readonly Dictionary<string, string[][]> _tokens;

        private ChatWords(Dictionary<string, List<string>> lists, Dictionary<string, IReadOnlyList<string>> zones)
        {
            _lists = lists;
            ZoneAliases = zones;
            _tokens = new Dictionary<string, string[][]>();
            foreach (KeyValuePair<string, List<string>> l in lists)
            {
                _tokens[l.Key] = Tokenized(l.Value);
            }

            ZoneTokens = new Dictionary<string, string[][]>();
            foreach (KeyValuePair<string, IReadOnlyList<string>> z in zones)
            {
                ZoneTokens[z.Key] = Tokenized(z.Value);
            }
        }

        /// <summary>No words: the reader finds no act in any line.</summary>
        public static ChatWords Empty { get; } = new ChatWords(new Dictionary<string, List<string>>(), new Dictionary<string, IReadOnlyList<string>>());

        /// <summary>Zone id to the lower-case names players give it.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> ZoneAliases { get; }

        /// <summary>The words of one list (accuse, saw_dump, saw_hit, near_pile, alibi, with, deny, lying, vote, skip, negation) over all languages, lower-case; empty when the list is missing.</summary>
        public IReadOnlyList<string> List(string name) => _lists.TryGetValue(name, out List<string>? l) ? l : NoWords;

        /// <summary>The same words with only the zones of the loaded map, so a typed line never names a zone no phrase could.</summary>
        public ChatWords ForMap(IEnumerable<string> zoneIds)
        {
            var ids = new HashSet<string>(zoneIds);
            var zones = new Dictionary<string, IReadOnlyList<string>>();
            foreach (KeyValuePair<string, IReadOnlyList<string>> z in ZoneAliases)
            {
                if (ids.Contains(z.Key))
                {
                    zones[z.Key] = z.Value;
                }
            }

            return new ChatWords(_lists, zones);
        }

        /// <summary>Zone id to its aliases split into words, for the reader.</summary>
        internal Dictionary<string, string[][]> ZoneTokens { get; }

        /// <summary>One list split into words (an entry of several words is several tokens), for the reader.</summary>
        internal string[][] Tokens(string name) => _tokens.TryGetValue(name, out string[][]? t) ? t : NoTokens;

        private static string[][] Tokenized(IReadOnlyList<string> entries)
        {
            var result = new List<string[]>();
            foreach (string e in entries)
            {
                string[] t = TextReader.Tokenize(e);
                if (t.Length > 0)
                {
                    result.Add(t);
                }
            }

            return result.ToArray();
        }

        public static ChatWords Parse(string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                var lists = new Dictionary<string, List<string>>();
                if (root.TryGetProperty("lang", out JsonElement lang) && lang.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty language in lang.EnumerateObject())
                    {
                        foreach (JsonProperty list in language.Value.EnumerateObject())
                        {
                            if (!lists.TryGetValue(list.Name, out List<string>? words))
                            {
                                words = new List<string>();
                                lists[list.Name] = words;
                            }

                            foreach (string w in Strings(list.Value))
                            {
                                words.Add(w);
                            }
                        }
                    }
                }

                var zones = new Dictionary<string, IReadOnlyList<string>>();
                if (root.TryGetProperty("zones", out JsonElement z) && z.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty zone in z.EnumerateObject())
                    {
                        zones[zone.Name] = Strings(zone.Value);
                    }
                }

                return new ChatWords(lists, zones);
            }
        }

        private static List<string> Strings(JsonElement array)
        {
            var result = new List<string>();
            if (array.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement e in array.EnumerateArray())
                {
                    string? s = e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        result.Add(s!.Trim().ToLowerInvariant());
                    }
                }
            }

            return result;
        }
    }
}
