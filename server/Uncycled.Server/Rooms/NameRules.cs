using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Uncycled.Server.Net;

namespace Uncycled.Server.Rooms;

/// <summary>Display-name rules for a room (design 5.9).</summary>
public static class NameRules
{
    /// <summary>
    /// The name a joining player gets: kept when no taken name equals it (case-insensitive), else
    /// with " 2", " 3", ... (the first free number), cut to fit Codec.MaxNameLength on a text-element
    /// boundary (an emoji or a joined emoji sequence is never split).
    /// </summary>
    public static string Unique(string name, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string t in taken)
        {
            used.Add(t);
        }

        if (!used.Contains(name))
        {
            return name;
        }

        for (int n = 2; ; n++)
        {
            string suffix = " " + n;
            string candidate = Fit(name, Codec.MaxNameLength - suffix.Length).TrimEnd() + suffix;
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>The longest prefix of name made of whole text elements and at most maxUnits UTF-16 units.</summary>
    private static string Fit(string name, int maxUnits)
    {
        if (name.Length <= maxUnits)
        {
            return name;
        }

        var sb = new StringBuilder();
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(name);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            if (sb.Length + element.Length > maxUnits)
            {
                break;
            }

            sb.Append(element);
        }

        return sb.ToString();
    }
}
