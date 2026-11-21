using System.Buffers;
using System.Text;
using System.Text.Json;
using Sim.Core;

namespace Uncycled.Server.Net;

/// <summary>Small helpers on top of Utf8JsonWriter. Floats are rounded and never NaN/Infinity.</summary>
public static class JsonOut
{
    public static string Build(Action<Utf8JsonWriter> body)
    {
        var buf = new ArrayBufferWriter<byte>(512);
        using (var w = new Utf8JsonWriter(buf))
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
