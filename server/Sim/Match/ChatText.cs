using System.Globalization;
using System.Text;

namespace Sim.Match
{
    /// <summary>
    /// Cleans a typed chat line: trims, collapses whitespace to single spaces, and removes, on every
    /// Unicode plane, control, format (bidi, tags), private-use and unassigned code points, lone surrogates
    /// and the invisible fillers that make blank-looking lines. At most MaxMarksPerBase combining
    /// marks are kept after a base character. A zero-width joiner is kept only between two emoji symbols
    /// (family, profession and similar sequences). Never truncates.
    /// </summary>
    public static class ChatText
    {
        /// <summary>Most combining marks (Mn, Me) kept after one base character; the rest are dropped.</summary>
        public const int MaxMarksPerBase = 2;

        private const char ZeroWidthJoiner = (char)0x200D;
        private const char VariationSelector16 = (char)0xFE0F;

        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return "";
            }

            var sb = new StringBuilder(raw.Length);
            bool pendingSpace = false;
            int marks = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = sb.Length > 0;
                    marks = 0;
                    continue;
                }

                int len = 1;
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= raw.Length || !char.IsLowSurrogate(raw[i + 1]))
                    {
                        continue; // lone high surrogate
                    }

                    len = 2;
                }
                else if (char.IsLowSurrogate(c))
                {
                    continue;
                }

                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(raw, i);
                int at = i;
                i += len - 1;
                switch (cat)
                {
                    case UnicodeCategory.Surrogate:
                    case UnicodeCategory.Control:
                    case UnicodeCategory.PrivateUse:
                    case UnicodeCategory.OtherNotAssigned:
                        continue;
                    case UnicodeCategory.Format:
                        if (len == 1 && c == ZeroWidthJoiner && !pendingSpace && JoinsTwoEmoji(raw, at, sb))
                        {
                            sb.Append(c);
                            marks = 0;
                        }

                        continue;
                    case UnicodeCategory.NonSpacingMark:
                    case UnicodeCategory.EnclosingMark:
                        if (marks >= MaxMarksPerBase)
                        {
                            continue;
                        }

                        marks++;
                        break;
                    default:
                        if (len == 1 && IsFiller(c))
                        {
                            continue;
                        }

                        marks = 0;
                        break;
                }

                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }

                sb.Append(raw, at, len);
            }

            return sb.ToString();
        }

        /// <summary>Length in Unicode code points (what a Godot LineEdit counts); a surrogate pair is one.</summary>
        public static int CodePointCount(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return 0;
            }

            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    i++;
                }

                n++;
            }

            return n;
        }

        /// <summary>Invisible letters and blanks that render as nothing (Hangul fillers, braille blank).</summary>
        internal static bool IsFiller(char c)
        {
            return c == (char)0x115F || c == (char)0x1160 || c == (char)0x3164 || c == (char)0xFFA0 || c == (char)0x2800;
        }

        private static bool JoinsTwoEmoji(string raw, int joinerAt, StringBuilder kept)
        {
            if (kept.Length == 0 || joinerAt + 1 >= raw.Length)
            {
                return false;
            }

            char last = kept[kept.Length - 1];
            bool prevOk;
            if (last == VariationSelector16)
            {
                prevOk = true;
            }
            else if (char.IsLowSurrogate(last) && kept.Length >= 2)
            {
                prevOk = IsEmojiSymbol(kept[kept.Length - 2].ToString() + last);
            }
            else
            {
                prevOk = IsEmojiSymbol(last.ToString());
            }

            if (!prevOk)
            {
                return false;
            }

            char next = raw[joinerAt + 1];
            if (char.IsHighSurrogate(next) && joinerAt + 2 < raw.Length && char.IsLowSurrogate(raw[joinerAt + 2]))
            {
                return IsEmojiSymbol(next.ToString() + raw[joinerAt + 2]);
            }

            return IsEmojiSymbol(next.ToString());
        }

        private static bool IsEmojiSymbol(string oneCodePoint)
        {
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(oneCodePoint, 0);
            return cat == UnicodeCategory.OtherSymbol || cat == UnicodeCategory.ModifierSymbol;
        }
    }
}
