using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Sim.Core;

namespace Uncycled.Server.Net;

/// <summary>Small helpers on top of Utf8JsonWriter. Floats are rounded and never NaN/Infinity.</summary>
public static class JsonOut
{
    /// <summary>
    /// Non-ASCII letters of the basic plane are written as they are (2-3 bytes each) instead of six-byte escapes; quotes, backslashes, control characters and the markup characters &lt; &gt; &amp; are still
    /// escaped. Characters outside the basic plane (emoji) stay escaped.
    /// </summary>
    private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string Build(Action<Utf8JsonWriter> body)
    {
        var buf = new ArrayBufferWriter<byte>(512);
        using (var w = new Utf8JsonWriter(buf, WriterOptions))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    public static float Safe(float v) => float.IsFinite(v) ? v : 0f;

    /// <summary>Writes a float property (3 decimals, 0 when not finite).</summary>
    public static void F(this Utf8JsonWriter w, string name, float v)
    {
        w.WriteNumber(name, Math.Round((double)Safe(v), 3));
    }

    /// <summary>Writes a position as [x, y] (2 decimals).</summary>
    public static void Pos(this Utf8JsonWriter w, string name, Vec2 p)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(Math.Round((double)Safe(p.X), 2));
        w.WriteNumberValue(Math.Round((double)Safe(p.Y), 2));
        w.WriteEndArray();
    }

    public static void Ids(this Utf8JsonWriter w, string name, IEnumerable<byte> ids)
    {
        w.WriteStartArray(name);
        foreach (byte id in ids)
        {
            w.WriteNumberValue((int)id);
        }

        w.WriteEndArray();
    }
}
