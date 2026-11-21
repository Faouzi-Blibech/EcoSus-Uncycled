using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sim.Match
{
    /// <summary>
    /// Replaces listed words in a chat line with "***". Whole-word, case-insensitive and
    /// accent-folded; a listed word also matches with repeated letters ("fuuuck": every letter run
    /// at least as long as in the word) and with digits for letters (0=o 1=i 3=e 4=a 5=s 7=t).
    /// Data: {"version":1,"words":{"en":["..."],"fr":["..."]}}; any language key is merged.
    /// Listed entries are single words (letters and digits only).
    /// </summary>
    public sealed class ChatFilter
    {
        public const string Mask = "***";

        private readonly Dictionary<string, List<int[]>> _byLetters;

        private ChatFilter(Dictionary<string, List<int[]>> byLetters)
        {
            _byLetters = byLetters;
        }

        /// <summary>A filter with no words: changes nothing.</summary>
        public static ChatFilter Empty { get; } = new ChatFilter(new Dictionary<string, List<int[]>>());

        public static ChatFilter Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            var table = new Dictionary<string, List<int[]>>(StringComparer.Ordinal);
            foreach (JsonProperty lang in doc.RootElement.GetProperty("words").EnumerateObject())
            {
                foreach (JsonElement w in lang.Value.EnumerateArray())
                {
                    string folded = Fold(w.GetString() ?? "", false);
                    if (folded.Length == 0)
                    {
                        continue;
                    }

                    string key = Runs(folded, out int[] counts);
                    if (!table.TryGetValue(key, out List<int[]>? list))
                    {
                        list = new List<int[]>();
                        table[key] = list;
                    }

                    list.Add(counts);
                }
            }

            return new ChatFilter(table);
        }

        public string Apply(string text)
        {
            if (string.IsNullOrEmpty(text) || _byLetters.Count == 0)
            {
                return text ?? "";
            }

            StringBuilder? sb = null;
            int copied = 0;
            int i = 0;
            while (i < text.Length)
            {
                if (!IsTokenChar(text[i]))
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < text.Length && IsTokenChar(text[i]))
                {
                    i++;
                }

                if (Matches(text.Substring(start, i - start)))
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder(text.Length);
                    }

                    sb.Append(text, copied, start - copied).Append(Mask);
                    copied = i;
                }
            }

            if (sb == null)
            {
                return text;
            }

            sb.Append(text, copied, text.Length - copied);
            return sb.ToString();
        }

        private bool Matches(string token)
        {
            string key = Runs(Fold(token, true), out int[] counts);
            if (!_byLetters.TryGetValue(key, out List<int[]>? candidates))
            {
                return false;
            }

            foreach (int[] min in candidates)
            {
                bool ok = true;
                for (int r = 0; r < min.Length; r++)
                {
                    if (counts[r] < min[r])
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTokenChar(char c)
        {
            return char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;
        }

        /// <summary>Lower-cases, drops accents and keeps letters and digits; optionally maps leet digits to letters.</summary>
        private static string Fold(string s, bool leet)
        {
            string d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char raw in d)
            {
                if (char.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark || !char.IsLetterOrDigit(raw))
                {
                    continue;
                }

                char c = char.ToLowerInvariant(raw);
                sb.Append(leet ? Leet(c) : c);
            }

            return sb.ToString();
        }

        private static char Leet(char c)
        {
            switch (c)
            {
                case '0': return 'o';
                case '1': return 'i';
                case '3': return 'e';
                case '4': return 'a';
                case '5': return 's';
                case '7': return 't';
                default: return c;
            }
        }

        /// <summary>Collapses repeated letters: "fuuuck" gives key "fuck" and counts 1,3,1,1.</summary>
        private static string Runs(string s, out int[] counts)
        {
            var key = new StringBuilder(s.Length);
            var list = new List<int>(s.Length);
            foreach (char c in s)
            {
                if (key.Length > 0 && key[key.Length - 1] == c)
                {
                    list[list.Count - 1]++;
                }
                else
                {
                    key.Append(c);
                    list.Add(1);
                }
            }

            counts = list.ToArray();
            return key.ToString();
        }
    }
}
