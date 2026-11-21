using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Uncycled.Server.Bots;
using Uncycled.Server.Content;
using Uncycled.Server.Net;

namespace Uncycled.Server.Rooms;

/// <summary>Owns every room: 4-letter codes, the room cap, /stats numbers.</summary>
public sealed class RoomManager
{
    /// <summary>20 letters, no vowels (no accidental words) and no Y.</summary>
    public const string CodeAlphabet = "BCDFGHJKLMNPQRSTVWXZ";

    public const int CodeLength = 4;

    private readonly ConcurrentDictionary<string, Room> _rooms = new ConcurrentDictionary<string, Room>();
    private readonly ContentBundle _content;
    private readonly ILoggerFactory _loggers;
    private readonly BotBrains _brains;
    private readonly int _maxRooms;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private int _connections;

    public RoomManager(ContentBundle content, int maxRooms, ILoggerFactory loggers, BotBrains brains)
    {
        _brains = brains;
        _content = content;
        _maxRooms = maxRooms;
        _loggers = loggers;
    }

    public static string NewCode()
    {
        var chars = new char[CodeLength];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        return new string(chars);
    }

    public Room? Find(string code)
    {
        if (code.Length != CodeLength)
        {
            return null;
        }

        return _rooms.TryGetValue(code, out Room? room) ? room : null;
    }

    /// <summary>Creates a room and queues the host's join. Returns an error code, or null on success.</summary>
    public string? TryCreate(Connection host, string name)
    {
        if (_rooms.Count >= _maxRooms)
        {
            return "server_full";
        }

        for (int attempt = 0; attempt < 20; attempt++)
        {
            string code = NewCode();
            var room = new Room(code, _content, this, _loggers.CreateLogger("Room " + code), _brains);
            if (!_rooms.TryAdd(code, room))
            {
                continue;
            }

            room.Post(new JoinInput(host, name, null, true));
            room.Start();
            return null;
        }

        return "server_busy";
    }

    internal void Remove(Room room)
    {
        _rooms.TryRemove(new KeyValuePair<string, Room>(room.Code, room));
    }

    public void ConnectionOpened() => Interlocked.Increment(ref _connections);

    public void ConnectionClosed() => Interlocked.Decrement(ref _connections);

    public object Stats() => Stats(_rooms.Values);

    internal object Stats(IEnumerable<Room> rooms)
    {
        int humans = 0;
        int bots = 0;
        int inMatch = 0;
        double p95 = 0;
        double botP95 = 0;
        int count = 0;
        foreach (Room r in rooms)
        {
            count++;
            humans += r.HumanCount;
            bots += r.BotCount;
            if (r.InMatch)
            {
                inMatch++;
            }

            p95 = Math.Max(p95, r.TickP95Ms());
            botP95 = Math.Max(botP95, r.BotTickP95Ms());
        }

        return new
        {
            rooms = count,
            rooms_in_match = inMatch,
            humans,
            bots,
            connections = Volatile.Read(ref _connections),
            tick_p95_ms = Math.Round(p95, 3),
            bots_p95_ms = Math.Round(botP95, 3),
            uptime_s = Math.Round(_uptime.Elapsed.TotalSeconds, 1),
        };
    }
}
