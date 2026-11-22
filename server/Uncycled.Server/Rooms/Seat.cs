using Uncycled.Bots;
using Uncycled.Server.Net;

namespace Uncycled.Server.Rooms;

/// <summary>Who drives a seat (docs/architecture/04-game-rules.md section 2).</summary>
public enum SeatControl
{
    Empty,
    Human,
    Bot,
    BotHoldingForHuman,
}

/// <summary>One seat of a room. Touched only by the room loop.</summary>
public sealed class Seat
{
    public Seat(byte id)
    {
        Id = id;
    }

    public byte Id { get; }

    public string Name { get; set; } = "";

    public byte Color { get; set; }

    /// <summary>Picked skin id from content/skins.json; "" is the default character.</summary>
    public string Skin { get; set; } = "";

    public SeatControl Control { get; set; } = SeatControl.Empty;

    /// <summary>Reconnect token. Null for bots and empty seats.</summary>
    public string? Token { get; set; }

    public Connection? Conn { get; set; }

    public IBotBrain? Brain { get; set; }

    public void Clear()
    {
        Name = "";
        Color = 0;
        Skin = "";
        Control = SeatControl.Empty;
        Token = null;
        Conn = null;
        Brain = null;
    }
}

public abstract class RoomInput
{
}

public sealed class JoinInput : RoomInput
{
    public JoinInput(Connection conn, string name, string? token, bool isCreate)
    {
        Conn = conn;
        Name = name;
        Token = token;
        IsCreate = isCreate;
    }

    public Connection Conn { get; }

    public string Name { get; }

    public string? Token { get; }

    public bool IsCreate { get; }
}

public sealed class MsgInput : RoomInput
{
    public MsgInput(Connection conn, ClientMessage msg)
    {
        Conn = conn;
        Msg = msg;
    }

    public Connection Conn { get; }

    public ClientMessage Msg { get; }
}
