using System.Globalization;
using System.Text;

namespace Sim.Match
{
    /// <summary>
    /// Cleans a typed chat line: trims, collapses whitespace to single spaces, and removes control,
    /// format and bidi characters and lone surrogates. A zero-width joiner is kept only between two
    /// emoji symbols (family, profession and similar sequences). Never truncates.
    /// </summary>
    public static class ChatText
    {
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
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }

                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < raw.Length && char.IsLowSurrogate(raw[i + 1]))
                    {
                        Flush(sb, ref pendingSpace);
                        sb.Append(c).Append(raw[i + 1]);
                        i++;
                    }

                    continue;
                }

                UnicodeCategory cat = char.GetUnicodeCategory(c);
                switch (cat)
                {
                    case UnicodeCategory.Surrogate: // a lone low surrogate
                    case UnicodeCategory.Control:
                    case UnicodeCategory.PrivateUse:
                        continue;
                    case UnicodeCategory.Format:
                        if (c == ZeroWidthJoiner && JoinsTwoEmoji(raw, i, sb))
                        {
                            sb.Append(c);
                        }

                        continue;
                }

                Flush(sb, ref pendingSpace);
                sb.Append(c);
            }

            return sb.ToString();
        }

        private static void Flush(StringBuilder sb, ref bool pendingSpace)
        {
            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
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
