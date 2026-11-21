using System;
using System.Collections.Generic;
using System.Text;

namespace Uncycled.Bots.Meeting
{
    /// <summary>
    /// Reads a typed chat line like a person who only takes what is clear: each sentence becomes the act of the
    /// chat phrase it says (same phrase ids and kinds, so every rule that reads phrases reads typed lines the same
    /// way). An accusation needs exactly one seat named (display name, whole word, case-insensitive; never the
    /// speaker) together with a word of that act, and no negation before the word or the name; a sentence ending in
    /// "?" gives no act; a denial, an alibi and a skip need no seat; an unknown name or two seats give no act.
    /// A line that names the act but no single zone is read as the plain accusation (sus), because the eyewitness
    /// phrases carry a zone. Only English words are shipped (content/chat_words.json); other languages are data.
    /// </summary>
    public static class TextReader
    {
        /// <summary>Characters read from a line; the engine's own limit is lower (tuning meeting.text_max_chars).</summary>
        public const int MaxChars = 240;

        private static readonly Dictionary<string, string> Kinds = new Dictionary<string, string>
        {
            { "sus", "accuse" }, { "saw_dump", "accuse" }, { "saw_hit", "accuse" }, { "near_pile", "accuse" },
            { "i_was", "alibi" }, { "with", "alibi" }, { "not_me", "react" }, { "lying", "react" },
            { "vote", "vote" }, { "skip_no_proof", "vote" },
        };

        private static readonly string[] AccuseOrder = { "saw_dump", "saw_hit", "near_pile", "accuse" };

        public static IReadOnlyList<ChatAct> Read(string text, byte speaker, IReadOnlyDictionary<byte, string> livingNames, ChatWords words, float time)
        {
            var acts = new List<ChatAct>();
            if (string.IsNullOrWhiteSpace(text) || words == null || livingNames == null)
            {
                return acts;
            }

            if (text.Length > MaxChars)
            {
                text = text.Substring(0, MaxChars);
            }

            // longest name first, so "bot 3" is not also read as a shorter name inside it
            var names = new List<(byte Seat, string[] Tokens)>();
            foreach (KeyValuePair<byte, string> kv in livingNames)
            {
                string[] t = Tokenize(kv.Value);
                if (t.Length > 0)
                {
                    names.Add((kv.Key, t));
                }
            }

            names.Sort((a, b) => b.Tokens.Length.CompareTo(a.Tokens.Length));

            foreach (string[] clause in Clauses(text))
            {
                ReadClause(clause, speaker, names, words, time, acts);
            }

            return acts;
        }

        /// <summary>Lower-case words of a text: letters, digits, apostrophes and inner hyphens; anything else separates.</summary>
        internal static string[] Tokenize(string text)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            foreach (char raw in text)
            {
                char c = raw == '’' || raw == '‘' || raw == '`' ? '\'' : char.ToLowerInvariant(raw);
                if (char.IsLetterOrDigit(c) || c == '\'' || c == '-' || c == '*')
                {
                    sb.Append(c);
                }
                else
                {
                    Flush(sb, tokens);
                }
            }

            Flush(sb, tokens);
            return tokens.ToArray();
        }

        private static void Flush(StringBuilder sb, List<string> tokens)
        {
            if (sb.Length == 0)
            {
                return;
            }

            string token = sb.ToString().Trim('-', '\'');
            sb.Clear();
            if (token.Length > 0)
            {
                tokens.Add(token);
            }
        }

        /// <summary>The comma-separated clauses of every sentence that does not end in a question mark.</summary>
        private static List<string[]> Clauses(string text)
        {
            var result = new List<string[]>();
            var pending = new List<string>();
            var current = new StringBuilder();
            foreach (char c in text)
            {
                if (c == ',')
                {
                    pending.Add(current.ToString());
                    current.Clear();
                }
                else if (c == '.' || c == '!' || c == '?' || c == ';' || c == '\n' || c == '\r')
                {
                    pending.Add(current.ToString());
                    current.Clear();
                    if (c != '?')
                    {
                        AddClauses(result, pending);
                    }

                    pending.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            pending.Add(current.ToString());
            AddClauses(result, pending);
            return result;
        }

        private static void AddClauses(List<string[]> result, List<string> texts)
        {
            foreach (string s in texts)
            {
                string[] tokens = Tokenize(s);
                if (tokens.Length > 0)
                {
                    result.Add(tokens);
                }
            }
        }

        private static void ReadClause(string[] t, byte speaker, List<(byte Seat, string[] Tokens)> names, ChatWords words, float time, List<ChatAct> acts)
        {
            var taken = new bool[t.Length]; // tokens that are part of a name
            var seats = new List<byte>();
            int nameStart = int.MaxValue;
            foreach ((byte seat, string[] tokens) in names)
            {
                foreach (int at in Occurrences(t, tokens, taken))
                {
                    for (int i = at; i < at + tokens.Length; i++)
                    {
                        taken[i] = true;
                    }

                    if (!seats.Contains(seat))
                    {
                        seats.Add(seat);
                    }

                    nameStart = Math.Min(nameStart, at);
                }
            }

            var zones = new List<string>();
            foreach (KeyValuePair<string, string[][]> zone in words.ZoneTokens)
            {
                foreach (string[] alias in zone.Value)
                {
                    if (Occurrences(t, alias, taken).Count > 0)
                    {
                        zones.Add(zone.Key);
                        break;
                    }
                }
            }

            string? oneZone = zones.Count == 1 ? zones[0] : null;
            byte? oneSeat = seats.Count == 1 && seats[0] != speaker ? seats[0] : (byte?)null;

            if (Find(t, words.Tokens("deny"), taken) >= 0)
            {
                Add(acts, speaker, "not_me", null, null, time);
            }

            int skip = Find(t, words.Tokens("skip"), taken);
            if (skip >= 0 && !Negated(t, words, skip))
            {
                Add(acts, speaker, "skip_no_proof", null, null, time);
            }

            int with = Find(t, words.Tokens("with"), taken);
            if (with >= 0)
            {
                if (oneSeat != null && oneZone != null && !Negated(t, words, with))
                {
                    Add(acts, speaker, "with", oneSeat, oneZone, time);
                }
            }
            else if (oneZone != null && Find(t, words.Tokens("alibi"), taken) >= 0)
            {
                Add(acts, speaker, "i_was", null, oneZone, time);
            }

            if (oneSeat == null)
            {
                return;
            }

            int lying = Find(t, words.Tokens("lying"), taken);
            if (lying >= 0 && !Negated(t, words, Math.Max(lying, nameStart)))
            {
                Add(acts, speaker, "lying", oneSeat, null, time);
            }

            int vote = Find(t, words.Tokens("vote"), taken);
            if (vote >= 0 && !Negated(t, words, Math.Max(vote, nameStart)))
            {
                Add(acts, speaker, "vote", oneSeat, null, time);
            }

            foreach (string kind in AccuseOrder)
            {
                int at = Find(t, words.Tokens(kind), taken);
                if (at < 0)
                {
                    continue;
                }

                // the strongest act named in the clause decides; a negated one cancels the accusation
                if (!Negated(t, words, Math.Max(at, nameStart)))
                {
                    bool eyewitness = kind != "accuse" && oneZone != null;
                    Add(acts, speaker, eyewitness ? kind : "sus", oneSeat, eyewitness ? oneZone : null, time);
                }

                break;
            }
        }

        private static void Add(List<ChatAct> acts, byte speaker, string id, byte? seat, string? zone, float time)
        {
            foreach (ChatAct a in acts)
            {
                if (a.PhraseId == id && a.Seat == seat && a.Zone == zone)
                {
                    return;
                }
            }

            acts.Add(new ChatAct(speaker, id, Kinds[id], seat, zone, time));
        }

        /// <summary>True when a negation word stands before the token at limit.</summary>
        private static bool Negated(string[] t, ChatWords words, int limit)
        {
            foreach (string[] n in words.Tokens("negation"))
            {
                for (int i = 0; i < limit && i < t.Length; i++)
                {
                    if (n.Length == 1 && t[i] == n[0])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Index of the first token where one of the words (each possibly several tokens) starts, not on a name token; -1 when none.</summary>
        private static int Find(string[] t, string[][] list, bool[] taken)
        {
            int best = -1;
            foreach (string[] phrase in list)
            {
                List<int> at = Occurrences(t, phrase, taken);
                if (at.Count > 0 && (best < 0 || at[0] < best))
                {
                    best = at[0];
                }
            }

            return best;
        }

        /// <summary>Start indexes of the token sequence in t; matches that overlap a taken token (a name) are skipped.</summary>
        private static List<int> Occurrences(string[] t, string[] seq, bool[] taken)
        {
            var found = new List<int>();
            if (seq.Length == 0)
            {
                return found;
            }

            for (int i = 0; i + seq.Length <= t.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < seq.Length && match; j++)
                {
                    match = t[i + j] == seq[j] && !taken[i + j];
                }

                if (match)
                {
                    found.Add(i);
                }
            }

            return found;
        }
    }
}
