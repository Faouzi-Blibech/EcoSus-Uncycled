using System.Diagnostics;
using System.Net.WebSockets;
using Uncycled.Server.Rooms;

namespace Uncycled.Server.Net;

/// <summary>The /ws endpoint: accept, receive loop (message cap, rate limit, hello), hand off to rooms.</summary>
public static class WsHandler
{
    private static readonly Stopwatch Uptime = Stopwatch.StartNew();

    public static async Task HandleAsync(HttpContext ctx, RoomManager rooms, ILogger log)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using WebSocket ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var conn = new Connection(ws, log);
        Task sendTask = conn.RunSendLoopAsync();
        rooms.ConnectionOpened();
        try
        {
            await ReceiveLoopAsync(conn, ws, rooms, ctx.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // client went away or message timed out
        }
        catch (WebSocketException)
        {
            // abrupt close
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Connection {Id}: receive loop failed", conn.Id);
        }
        finally
        {
            rooms.ConnectionClosed();
            conn.MarkClosed();
            conn.MarkReceiveDone();
            Room? room = conn.Room;
            if (room != null)
            {
                room.NotifyDisconnect(conn);
            }

            conn.CompleteSend();
            try
            {
                await sendTask;
            }
            catch (Exception)
            {
                // send loop already logged
            }
        }
    }

    private static async Task ReceiveLoopAsync(Connection conn, WebSocket ws, RoomManager rooms, CancellationToken ct)
    {
        byte[] buf = new byte[Codec.MaxMessageBytes];
        bool greeted = false;

        while (ws.State == WebSocketState.Open)
        {
            WebSocketReceiveResult r = await ws.ReceiveAsync(new ArraySegment<byte>(buf, 0, buf.Length), ct);
            if (r.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            int len = r.Count;
            CancellationTokenSource? timeout = null;
            try
            {
                while (!r.EndOfMessage)
                {
                    if (len >= buf.Length)
                    {
                        conn.Close(WebSocketCloseStatus.MessageTooBig, "message too big");
                        return;
                    }

                    if (timeout == null)
                    {
                        timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    }

                    r = await ws.ReceiveAsync(new ArraySegment<byte>(buf, len, buf.Length - len), timeout.Token);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    len += r.Count;
                }
            }
            finally
            {
                timeout?.Dispose();
            }

            if (r.MessageType != WebSocketMessageType.Text)
            {
                conn.Close(WebSocketCloseStatus.InvalidMessageType, "text only");
                return;
            }

            if (!conn.TryConsumeRate())
            {
                if (conn.ShouldWarnRate())
                {
                    conn.Enqueue(Codec.Error("rate_limited"));
                }

                continue;
            }

            if (!Codec.TryParse(new ReadOnlyMemory<byte>(buf, 0, len), out ClientMessage? msg) || msg == null)
            {
                conn.Enqueue(Codec.Error("bad_json"));
                continue;
            }

            if (!greeted)
            {
                if (msg.T != "hello")
                {
                    conn.Enqueue(Codec.Error("expected_hello"));
                    continue;
                }

                if (!Codec.TryGetInt(msg.Root, "proto", out int proto) || proto != Codec.ProtocolVersion)
                {
                    conn.Enqueue(Codec.Error("proto_mismatch", "server speaks protocol " + Codec.ProtocolVersion));
                    conn.Close(WebSocketCloseStatus.PolicyViolation, "proto_mismatch");
                    return;
                }

                greeted = true;
                conn.Enqueue(Codec.HelloOk());
                continue;
            }

            if (!Dispatch(conn, msg, rooms))
            {
                return;
            }
        }
    }

    /// <summary>Returns false when the connection must be dropped.</summary>
    private static bool Dispatch(Connection conn, ClientMessage msg, RoomManager rooms)
    {
        switch (msg.T)
        {
            case "hello":
                return true;

            case "ping":
            {
                double c = 0;
                if (msg.Root.TryGetProperty("c", out System.Text.Json.JsonElement ce)
                    && ce.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    ce.TryGetDouble(out c);
                }

                conn.Enqueue(Codec.Pong(c, Uptime.Elapsed.TotalSeconds));
                return true;
            }

            case "create":
            {
                string? name = Codec.CleanName(Codec.GetString(msg.Root, "name"));
                if (name == null)
                {
                    conn.Enqueue(Codec.JoinFailed("bad_name"));
                    return true;
                }

                if (!conn.TryBeginJoin())
                {
                    conn.Enqueue(Codec.Error("already_in_room"));
                    return true;
                }

                string? failure = rooms.TryCreate(conn, name);
                if (failure != null)
                {
                    conn.EndJoin();
                    conn.Enqueue(Codec.Error(failure));
                }

                return true;
            }

            case "join":
            {
                string? name = Codec.CleanName(Codec.GetString(msg.Root, "name"));
                if (name == null)
                {
                    conn.Enqueue(Codec.JoinFailed("bad_name"));
                    return true;
                }

                if (!conn.TryBeginJoin())
                {
                    conn.Enqueue(Codec.Error("already_in_room"));
                    return true;
                }

                string code = (Codec.GetString(msg.Root, "code") ?? "").Trim().ToUpperInvariant();
                string? token = Codec.GetString(msg.Root, "token");
                Room? room = rooms.Find(code);
                if (room == null || !room.Post(new JoinInput(conn, name, token, false)))
                {
                    conn.EndJoin();
                    conn.Enqueue(Codec.JoinFailed("no_room"));
                }

                return true;
            }

            default:
            {
                Room? room = conn.Room;
                if (room == null)
                {
                    conn.Enqueue(Codec.Error("not_in_room"));
                    return true;
                }

                if (!room.Post(new MsgInput(conn, msg)) && msg.T != "move")
                {
                    conn.Enqueue(Codec.Error("busy"));
                }

                return true;
            }
        }
    }
}
