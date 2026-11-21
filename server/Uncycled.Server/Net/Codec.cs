using System.Text.Json;
using Sim.Core;
using Sim.Match;

namespace Uncycled.Server.Net;

/// <summary>One parsed client message: the "t" and the (cloned) root object.</summary>
public sealed class ClientMessage
{
    public ClientMessage(string t, JsonElement root)
    {
        T = t;
        Root = root;
    }

    public string T { get; }

    public JsonElement Root { get; }
}

/// <summary>Client message parsing and the small server messages that do not need room state.</summary>
public static class Codec
{
    public const int ProtocolVersion = 1;
    public const string ServerBuild = "0.1.0";
    public const int MaxMessageBytes = 4096;
    public const int MaxNameLength = 16;
    public const int MaxChatTextRawChars = 1024;

    private static readonly JsonDocumentOptions ParseOptions = new JsonDocumentOptions { MaxDepth = 8 };

    // ---------- inbound ----------

    public static bool TryParse(ReadOnlyMemory<byte> utf8, out ClientMessage? msg)
    {
        msg = null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(utf8, ParseOptions);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string? t = GetString(root, "t");
            if (string.IsNullOrEmpty(t))
            {
                return false;
            }

            msg = new ClientMessage(t, root.Clone());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string? GetString(JsonElement obj, string name)
    {
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out JsonElement e)
            && e.ValueKind == JsonValueKind.String)
        {
            return e.GetString();
        }

        return null;
    }

    public static bool TryGetInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out JsonElement e)
            && e.ValueKind == JsonValueKind.Number
            && e.TryGetInt32(out value);
    }

    public static bool TryGetLong(JsonElement obj, string name, out long value)
    {
        value = 0;
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out JsonElement e)
            && e.ValueKind == JsonValueKind.Number
            && e.TryGetInt64(out value);
    }

    public static bool GetBool(JsonElement obj, string name, bool fallback)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement e))
        {
            if (e.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (e.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }

        return fallback;
    }

    private static bool TryFloat(JsonElement e, out float value)
    {
        value = 0f;
        return e.ValueKind == JsonValueKind.Number && e.TryGetSingle(out value) && float.IsFinite(value);
    }

    private static bool TryByte(JsonElement obj, string name, out byte value)
    {
        value = 0;
        if (TryGetInt(obj, name, out int i) && i >= 0 && i <= 255)
        {
            value = (byte)i;
            return true;
        }

        return false;
    }

    /// <summary>Trimmed display name, or null when it is empty, too long or has control characters.</summary>
    public static string? CleanName(string? raw)
    {
        if (raw == null)
        {
            return null;
        }

        string s = raw.Trim();
        if (s.Length == 0 || s.Length > MaxNameLength)
        {
            return null;
        }

        foreach (char c in s)
        {
            if (char.IsControl(c))
            {
                return null;
            }
        }

        return s;
    }

    /// <summary>
    /// Turns a game message into an engine command. On failure error is "unknown_type", "bad_field"
    /// or "bad_coords" (non-finite position: the caller closes the connection).
    /// </summary>
    public static bool TryToCommand(ClientMessage m, out ICommand? cmd, out string? error)
    {
        cmd = null;
        error = null;
        JsonElement r = m.Root;
        switch (m.T)
        {
            case "move":
            {
                if (!r.TryGetProperty("pos", out JsonElement pos)
                    || pos.ValueKind != JsonValueKind.Array
                    || pos.GetArrayLength() != 2)
                {
                    error = "bad_field";
                    return false;
                }

                if (!TryFloat(pos[0], out float x) || !TryFloat(pos[1], out float y))
                {
                    error = "bad_coords";
                    return false;
                }

                if (!TryGetLong(r, "seq", out long seq))
                {
                    error = "bad_field";
                    return false;
                }

                cmd = new MoveCommand(new Vec2(x, y), seq);
                return true;
            }

            case "use":
            {
                string? console = GetString(r, "console");
                if (string.IsNullOrEmpty(console) || console.Length > 64)
                {
                    error = "bad_field";
                    return false;
                }

                cmd = new UseCommand(console);
                return true;
            }

            case "task_step":
            {
                if (!TryGetInt(r, "task", out int taskId))
                {
                    error = "bad_field";
                    return false;
                }

                cmd = new TaskStepCommand(taskId);
                return true;
            }

            case "hit":
            {
                if (!TryByte(r, "target", out byte target))
                {
                    error = "bad_field";
                    return false;
                }

                cmd = new HitCommand(target);
                return true;
            }

            case "dump":
                cmd = new DumpCommand();
                return true;

            case "call_meeting":
                cmd = new CallMeetingCommand();
                return true;

            case "report":
            case "clean_start":
            case "clean_cancel":
            {
                if (!TryGetInt(r, "pile", out int pileId))
                {
                    error = "bad_field";
                    return false;
                }

                if (m.T == "report")
                {
                    cmd = new ReportCommand(pileId);
                }
                else if (m.T == "clean_start")
                {
                    cmd = new CleanStartCommand(pileId);
                }
                else
                {
                    cmd = new CleanCancelCommand(pileId);
                }

                return true;
            }

            case "sabotage":
            {
                string? kind = GetString(r, "kind");
                if (kind == "smog")
                {
                    cmd = new SabotageCommand(SabotageKind.Smog);
                    return true;
                }

                if (kind == "fire")
                {
                    cmd = new SabotageCommand(SabotageKind.Fire);
                    return true;
                }

                error = "bad_field";
                return false;
            }

            case "fix":
            {
                string? console = GetString(r, "console");
                if (string.IsNullOrEmpty(console) || console.Length > 64)
                {
                    error = "bad_field";
                    return false;
                }

                cmd = new FixCommand(console, GetBool(r, "holding", false));
                return true;
            }

            case "vote":
            {
                if (GetBool(r, "skip", false))
                {
                    cmd = new VoteCommand(null);
                    return true;
                }

                if (!TryByte(r, "target", out byte votee))
                {
                    error = "bad_field";
                    return false;
                }

                cmd = new VoteCommand(votee);
                return true;
            }

            case "chat_phrase":
            {
                string? id = GetString(r, "id");
                if (string.IsNullOrEmpty(id) || id.Length > 64)
                {
                    error = "bad_field";
                    return false;
                }

                var args = new List<ChatArg>();
                if (r.TryGetProperty("args", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    if (arr.GetArrayLength() > 8)
                    {
                        error = "bad_field";
                        return false;
                    }

                    foreach (JsonElement a in arr.EnumerateArray())
                    {
                        if (a.ValueKind == JsonValueKind.Number && a.TryGetInt32(out int seat) && seat >= 0 && seat <= 255)
                        {
                            args.Add(ChatArg.ForSeat((byte)seat));
                        }
                        else if (a.ValueKind == JsonValueKind.String)
                        {
                            string zone = a.GetString() ?? "";
                            if (zone.Length == 0 || zone.Length > 64)
                            {
                                error = "bad_field";
                                return false;
                            }

                            args.Add(ChatArg.ForZone(zone));
                        }
                        else
                        {
                            error = "bad_field";
                            return false;
                        }
                    }
                }

                cmd = new ChatPhraseCommand(id, args);
                return true;
            }

            case "chat_text":
            {
                // size guard only: the engine normalises, filters and rejects over-length text
                string? text = GetString(r, "text");
                if (text == null || text.Length > MaxChatTextRawChars)
                {
                    error = "bad_field";
                    return false;
                }

                cmd = new ChatTextCommand(text);
                return true;
            }

            default:
                error = "unknown_type";
                return false;
        }
    }

    // ---------- outbound (no room state needed) ----------

    public static string HelloOk() =>
        JsonOut.Build(w =>
        {
            w.WriteString("t", "hello_ok");
            w.WriteString("server_build", ServerBuild);
        });

    public static string Error(string code, string? detail = null) =>
        JsonOut.Build(w =>
        {
            w.WriteString("t", "error");
            w.WriteString("code", code);
            if (detail != null)
            {
                w.WriteString("detail", detail);
            }
        });

    public static string Pong(double clientTime, double serverTime) =>
        JsonOut.Build(w =>
        {
            w.WriteString("t", "pong");
            w.WriteNumber("c", clientTime);
            w.WriteNumber("s", Math.Round(serverTime, 3));
        });

    public static string JoinFailed(string reason) =>
        JsonOut.Build(w =>
        {
            w.WriteString("t", "join_failed");
            w.WriteString("reason", reason);
        });

    public static string Rejected(string action, string reason) =>
        JsonOut.Build(w =>
        {
            w.WriteString("t", "action_rejected");
            w.WriteString("action", action);
            w.WriteString("reason", reason);
        });
}
