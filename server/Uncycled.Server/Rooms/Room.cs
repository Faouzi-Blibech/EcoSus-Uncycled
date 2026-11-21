using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots;
using Uncycled.Server.Bots;
using Uncycled.Server.Content;
using Uncycled.Server.Net;

namespace Uncycled.Server.Rooms;

/// <summary>
/// One room = one actor. A single loop (20 Hz) owns all room and match state; network code only
/// posts RoomInput messages or calls NotifyDisconnect.
/// </summary>
public sealed class Room
{
    private const int MaxInputsPerTick = 256;
    private const int MaxColors = 12;
    private const double LobbyIdleCloseS = 60.0;
    private const double MatchIdleCloseS = 120.0;
    private const int TickSamples = 128;
    private static readonly TimeSpan TickPeriod = TimeSpan.FromMilliseconds(50);

    private readonly ContentBundle _content;
    private readonly RoomManager _manager;
    private readonly ILogger _log;
    private readonly Channel<RoomInput> _inbox;
    private readonly ConcurrentQueue<Connection> _dead = new ConcurrentQueue<Connection>();
    private readonly Seat[] _seats;
    private readonly MoveCommand?[] _moves;
    private readonly Stopwatch _clock = new Stopwatch();
    private readonly double[] _tickMs = new double[TickSamples];
    private readonly double[] _botMs = new double[TickSamples];
    private readonly BotBrains _brains;
    private readonly List<GuardedBrain> _guarded = new List<GuardedBrain>();

    private MatchEngine? _engine;
    private long _tick;
    private int _hostSeat = -1;
    private double _backToLobbyAt = -1;
    private RoomMeeting? _meeting;
    private int _matchNo;
    private double _noHumansSince = -1;
    private int _tickSampleIndex;
    private int _botSampleIndex;
    private volatile bool _closed;

    public Room(string code, ContentBundle content, RoomManager manager, ILogger log, BotBrains brains)
    {
        _brains = brains;
        Code = code;
        _content = content;
        _manager = manager;
        _log = log;
        _inbox = Channel.CreateBounded<RoomInput>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });
        int n = content.Config.Seats;
        _seats = new Seat[n];
        for (int i = 0; i < n; i++)
        {
            _seats[i] = new Seat((byte)i);
        }

        _moves = new MoveCommand?[n];
    }

    public string Code { get; }

    // ---------- thread-safe surface ----------

    /// <summary>Queues an input. False when the inbox is full or the room is closed.</summary>
    public bool Post(RoomInput input) => _inbox.Writer.TryWrite(input);

    /// <summary>Called by the socket handler when a connection that joined this room is gone.</summary>
    public void NotifyDisconnect(Connection conn) => _dead.Enqueue(conn);

    public void Start() => _ = Task.Run(RunAsync);

    // Rough numbers for /stats; read from other threads without locks on purpose.
    public int HumanCount => CountSeats(SeatControl.Human);

    public int BotCount => CountSeats(SeatControl.Bot) + CountSeats(SeatControl.BotHoldingForHuman);

    public bool InMatch => _engine != null;

    internal IReadOnlyList<Seat> Seats => _seats;

    /// <summary>The running match, for tests. Null in the lobby.</summary>
    internal MatchEngine? Engine => _engine;

    /// <summary>True once the room decided to close (idle limit); the loop stops after the current tick. For tests.</summary>
    internal bool IsClosed => _closed;

    /// <summary>The chat log of the current match meeting, for tests. Null in the lobby or when bots do not talk.</summary>
    internal Uncycled.Bots.Meeting.MeetingLog? MeetingChat => _meeting?.Log;

    public double TickP95Ms()
    {
        double[] copy = (double[])_tickMs.Clone();
        Array.Sort(copy);
        int idx = (int)(copy.Length * 0.95);
        return copy[Math.Min(idx, copy.Length - 1)];
    }

    /// <summary>95th percentile of the time all bot brains took inside one tick (ms).</summary>
    public double BotTickP95Ms()
    {
        double[] copy = (double[])_botMs.Clone();
        Array.Sort(copy);
        int idx = (int)(copy.Length * 0.95);
        return copy[Math.Min(idx, copy.Length - 1)];
    }

    private int CountSeats(SeatControl control)
    {
        int n = 0;
        foreach (Seat s in _seats)
        {
            if (s.Control == control)
            {
                n++;
            }
        }

        return n;
    }

    // ---------- loop ----------

    private async Task RunAsync()
    {
        _clock.Start();
        double last = 0;
        using var timer = new PeriodicTimer(TickPeriod);
        try
        {
            while (!_closed && await timer.WaitForNextTickAsync().ConfigureAwait(false))
            {
                double now = _clock.Elapsed.TotalSeconds;
                float dt = (float)Math.Min(now - last, 0.25);
                if (dt < 0f)
                {
                    dt = 0f;
                }

                last = now;
                long t0 = Stopwatch.GetTimestamp();
                Step(now, dt);
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                _tickMs[_tickSampleIndex] = ms;
                _tickSampleIndex = (_tickSampleIndex + 1) % TickSamples;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Room {Code}: loop failed, closing", Code);
            string msg = Codec.Error("server_error");
            foreach (Seat s in _seats)
            {
                s.Conn?.Enqueue(msg);
            }
        }
        finally
        {
            Shutdown();
        }
    }

    /// <summary>One room tick. The loop calls it every 50 ms; tests call it to fast-forward.</summary>
    internal void Step(double now, float dt)
    {
        _tick++;
        DrainInbox(now);

        if (_engine != null)
        {
            long b0 = Stopwatch.GetTimestamp();
            _guarded.Clear();
            foreach (Seat s in _seats)
            {
                if (s.Brain != null)
                {
                    Seat bot = s;
                    bot.Brain!.Think(_engine, bot.Id, dt, cmd => Apply(bot, cmd, null));
                    if (bot.Brain is GuardedBrain guarded)
                    {
                        _guarded.Add(guarded);
                    }
                }
            }

            GuardedBrain.SettleTick(_guarded);
            _botMs[_botSampleIndex] = (Stopwatch.GetTimestamp() - b0) * 1000.0 / Stopwatch.Frequency;
            _botSampleIndex = (_botSampleIndex + 1) % TickSamples;

            IReadOnlyList<GameEvent> events = _engine.Tick(dt);
            FeedMeeting(events);
            RouteEvents(events, now);
            SendSnapshots(now);
            if (_engine.Phase == MatchPhase.Ended && HumanCount == 0)
            {
                // nobody is left to see the result and a lobby without humans only waits for its idle limit
                _log.LogInformation("Room {Code}: match over and no humans, closing", Code);
                _closed = true;
            }
            else if (_backToLobbyAt >= 0 && now >= _backToLobbyAt)
            {
                ReturnToLobby();
            }
        }

        CheckIdle(now);
    }

    private void DrainInbox(double now)
    {
        while (_dead.TryDequeue(out Connection? dead))
        {
            HandleDisconnect(dead);
        }

        Array.Clear(_moves, 0, _moves.Length);
        int n = 0;
        while (n < MaxInputsPerTick && _inbox.Reader.TryRead(out RoomInput? input))
        {
            n++;
            switch (input)
            {
                case JoinInput j:
                    HandleJoin(j);
                    break;
                case MsgInput m:
                    HandleMessage(m, now);
                    break;
            }
        }

        if (_engine != null)
        {
            for (int i = 0; i < _seats.Length; i++)
            {
                MoveCommand? mv = _moves[i];
                if (mv != null && _seats[i].Control == SeatControl.Human)
                {
                    Apply(_seats[i], mv, "move");
                }
            }
        }
    }

    // ---------- join / leave ----------

    private void HandleJoin(JoinInput j)
    {
        Connection conn = j.Conn;
        if (conn.IsClosed)
        {
            return;
        }

        Seat? seat = null;
        bool rejoin = false;
        if (!string.IsNullOrEmpty(j.Token))
        {
            foreach (Seat s in _seats)
            {
                bool holdable = s.Control == SeatControl.Human || s.Control == SeatControl.BotHoldingForHuman;
                if (holdable && s.Token != null && s.Token == j.Token)
                {
                    seat = s;
                    rejoin = true;
                    break;
                }
            }
        }

        if (seat == null)
        {
            if (_engine != null)
            {
                conn.EndJoin();
                conn.Enqueue(Codec.JoinFailed("in_match"));
                return;
            }

            foreach (Seat s in _seats)
            {
                if (s.Control == SeatControl.Empty)
                {
                    seat = s;
                    break;
                }
            }

            if (seat == null)
            {
                conn.EndJoin();
                conn.Enqueue(Codec.JoinFailed("full"));
                return;
            }

            seat.Name = NameRules.Unique(j.Name, TakenNames());
            seat.Color = FreeColor();
            seat.Token = NewToken();
        }

        if (rejoin)
        {
            Connection? old = seat.Conn;
            if (old != null && old != conn)
            {
                old.Room = null;
                old.Seat = -1;
                old.EndJoin();
                old.Close(WebSocketCloseStatus.PolicyViolation, "replaced by a newer connection");
            }

            _engine?.ResetMoveSeq(seat.Id);
        }

        seat.Control = SeatControl.Human;
        seat.Brain = null;
        seat.Conn = conn;
        conn.Seat = seat.Id;
        conn.Room = this;
        _noHumansSince = -1;
        if (j.IsCreate || _hostSeat < 0)
        {
            _hostSeat = seat.Id;
        }

        SeatView? state = rejoin && _engine != null ? _engine.ViewFor(seat.Id) : null;
        string phase = _engine == null ? "lobby" : Wire.Snake(_engine.Phase);
        conn.Enqueue(Wire.Welcome(Code, seat.Id, seat.Token ?? "", _hostSeat, _content, phase, state, _seats, SeatIsOut));
        if (_engine == null)
        {
            BroadcastLobby();
        }
    }

    /// <summary>Names of the occupied seats (the name rule compares a new name against them).</summary>
    private List<string> TakenNames()
    {
        var taken = new List<string>();
        foreach (Seat s in _seats)
        {
            if (s.Control != SeatControl.Empty)
            {
                taken.Add(s.Name);
            }
        }

        return taken;
    }

    private bool SeatIsOut(byte seat)
    {
        if (_engine == null)
        {
            return false;
        }

        foreach (PlayerState p in _engine.Players)
        {
            if (p.Seat == seat)
            {
                return p.Status == PlayerStatus.Out;
            }
        }

        return false;
    }

    private void HandleDisconnect(Connection conn)
    {
        int idx = conn.Seat;
        if (idx < 0 || idx >= _seats.Length)
        {
            return;
        }

        Seat seat = _seats[idx];
        if (seat.Conn != conn)
        {
            return;
        }

        if (_engine == null)
        {
            seat.Clear();
            PickHost();
            BroadcastLobby();
        }
        else if (seat.Control == SeatControl.Human)
        {
            // the bot keeps the seat active; the human can reclaim it with the token
            seat.Control = SeatControl.BotHoldingForHuman;
            seat.Conn = null;
            seat.Brain = MakeBrain(seat);
            PickHost();
        }
    }

    private void HandleLeave(Seat seat, Connection conn)
    {
        conn.Room = null;
        conn.Seat = -1;
        conn.EndJoin();
        if (_engine == null)
        {
            seat.Clear();
            PickHost();
            BroadcastLobby();
        }
        else
        {
            // leaving on purpose gives the seat to a plain bot for the rest of the match
            seat.Control = SeatControl.Bot;
            seat.Conn = null;
            seat.Token = null;
            seat.Brain = MakeBrain(seat);
            PickHost();
        }
    }

    private void PickHost()
    {
        if (_hostSeat >= 0 && _seats[_hostSeat].Control == SeatControl.Human)
        {
            return;
        }

        _hostSeat = -1;
        foreach (Seat s in _seats)
        {
            if (s.Control == SeatControl.Human)
            {
                _hostSeat = s.Id;
                return;
            }
        }
    }

    // ---------- messages ----------

    private void HandleMessage(MsgInput m, double now)
    {
        Connection conn = m.Conn;
        int idx = conn.Seat;
        if (idx < 0 || idx >= _seats.Length)
        {
            return;
        }

        Seat seat = _seats[idx];
        if (seat.Conn != conn || seat.Control != SeatControl.Human)
        {
            return;
        }

        ClientMessage msg = m.Msg;
        switch (msg.T)
        {
            case "set_color":
                HandleSetColor(seat, conn, msg);
                return;
            case "start":
                HandleStart(seat, conn);
                return;
            case "leave":
                HandleLeave(seat, conn);
                return;
        }

        if (_engine == null)
        {
            conn.Enqueue(Codec.Rejected(msg.T, "wrong_phase"));
            return;
        }

        if (!Codec.TryToCommand(msg, out ICommand? cmd, out string? error) || cmd == null)
        {
            if (error == "bad_coords")
            {
                conn.Enqueue(Codec.Error("bad_coords"));
                conn.Close(WebSocketCloseStatus.PolicyViolation, "bad coordinates");
            }
            else
            {
                conn.Enqueue(Codec.Error(error ?? "bad_field", msg.T));
            }

            return;
        }

        if (cmd is MoveCommand mv)
        {
            _moves[idx] = mv;
            return;
        }

        Apply(seat, cmd, msg.T);
    }

    private void HandleSetColor(Seat seat, Connection conn, ClientMessage msg)
    {
        if (_engine != null)
        {
            conn.Enqueue(Codec.Rejected("set_color", "wrong_phase"));
            return;
        }

        if (!Codec.TryGetInt(msg.Root, "color", out int color) || color < 0 || color >= MaxColors)
        {
            conn.Enqueue(Codec.Error("bad_field", "set_color"));
            return;
        }

        foreach (Seat s in _seats)
        {
            if (s != seat && s.Control != SeatControl.Empty && s.Color == color)
            {
                conn.Enqueue(Codec.Rejected("set_color", "not_allowed"));
                return;
            }
        }

        seat.Color = (byte)color;
        BroadcastLobby();
    }

    private void HandleStart(Seat seat, Connection conn)
    {
        if (_engine != null)
        {
            conn.Enqueue(Codec.Rejected("start", "wrong_phase"));
            return;
        }

        if (seat.Id != _hostSeat)
        {
            conn.Enqueue(Codec.Rejected("start", "not_allowed"));
            return;
        }

        var added = new List<Seat>();
        var setups = new List<SeatSetup>();
        foreach (Seat s in _seats)
        {
            if (s.Control == SeatControl.Empty)
            {
                s.Control = SeatControl.Bot;
                s.Name = NameRules.Unique("Bot " + (s.Id + 1), TakenNames());
                s.Color = FreeColor();
                added.Add(s);
            }

            setups.Add(new SeatSetup(s.Id, s.Name, s.Color));
        }

        try
        {
            var rng = new XorShiftRandom((ulong)Random.Shared.NextInt64());
            _engine = new MatchEngine(_content.Config, _content.Map, setups, _content.Tasks, rng, _content.Phrases, _content.Filter);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Room {Code}: could not start the match", Code);
            foreach (Seat s in added)
            {
                s.Clear();
            }

            _engine = null;
            conn.Enqueue(Codec.Error("start_failed"));
            return;
        }

        _meeting = _brains.NewRoomMeeting(_content.Phrases, Code + "-" + (++_matchNo));
        foreach (Seat s in added)
        {
            s.Brain = MakeBrain(s);
        }

        _backToLobbyAt = -1;
        Array.Clear(_moves, 0, _moves.Length);
    }

    /// <summary>The brain of a bot seat, chosen by the seat's role. Needs a running match.</summary>
    private IBotBrain MakeBrain(Seat seat)
    {
        PlayerState? p = _engine?.Find(seat.Id);
        if (p == null)
        {
            return new IdleBot();
        }

        try
        {
            return _brains.Create(p.Role, seat.Id, _meeting);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Room {Code}: no brain for seat {Seat}, it stays idle", Code, seat.Id);
            return new IdleBot();
        }
    }

    private void Apply(Seat seat, ICommand cmd, string? action)
    {
        if (_engine == null)
        {
            return;
        }

        CommandResult r;
        try
        {
            r = _engine.Apply(seat.Id, cmd);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Room {Code}: command {Cmd} from seat {Seat} threw", Code, cmd.GetType().Name, seat.Id);
            r = CommandResult.Rejected(RejectReason.NotAllowed);
        }

        if (!r.IsOk && action != null && action != "move" && seat.Conn != null)
        {
            seat.Conn.Enqueue(Codec.Rejected(action, WireNames.Of(r.Reason)));
        }
    }

    // ---------- output ----------

    private void RouteEvents(IReadOnlyList<GameEvent> events, double now)
    {
        foreach (GameEvent ev in events)
        {
            if (ev is GameOverEvent)
            {
                _backToLobbyAt = now + _content.Config.ReturnToLobbyS;
            }

            string? json = Wire.Event(ev, _seats, SeatIsOut);
            if (json == null)
            {
                continue;
            }

            foreach (Seat s in _seats)
            {
                if (s.Control == SeatControl.Human && s.Conn != null && ev.IsFor(s.Id))
                {
                    s.Conn.Enqueue(json);
                }
            }
        }
    }

    /// <summary>Gives the tick's events to the room's meeting feed: the chat log the talkers and memories read, and the meeting outcome for heat.</summary>
    private void FeedMeeting(IReadOnlyList<GameEvent> events)
    {
        if (_meeting == null || _engine == null)
        {
            return;
        }

        try
        {
            _meeting.Feed.Handle(events, _engine.MatchTime);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Room {Code}: the meeting feed failed on this tick", Code);
        }
    }

    private void SendSnapshots(double now)
    {
        if (_engine == null)
        {
            return;
        }

        foreach (Seat s in _seats)
        {
            if (s.Control == SeatControl.Human && s.Conn != null)
            {
                SeatView view = _engine.ViewFor(s.Id);
                s.Conn.SetSnapshot(Wire.Snapshot(view, _tick, now, _engine.ExtrasFor(s.Id)));
            }
        }
    }

    private void BroadcastLobby()
    {
        string json = Wire.Lobby(_seats, _hostSeat);
        foreach (Seat s in _seats)
        {
            if (s.Control == SeatControl.Human && s.Conn != null)
            {
                s.Conn.Enqueue(json);
            }
        }
    }

    private void ReturnToLobby()
    {
        _engine = null;
        _meeting = null;
        Array.Clear(_botMs, 0, _botMs.Length);
        _backToLobbyAt = -1;
        Array.Clear(_moves, 0, _moves.Length);
        foreach (Seat s in _seats)
        {
            if (s.Control == SeatControl.Bot || s.Control == SeatControl.BotHoldingForHuman)
            {
                s.Clear();
            }
            else
            {
                s.Brain = null;
            }
        }

        PickHost();
        BroadcastLobby();
    }

    // ---------- lifetime ----------

    private void CheckIdle(double now)
    {
        if (HumanCount > 0)
        {
            _noHumansSince = -1;
            return;
        }

        if (_noHumansSince < 0)
        {
            _noHumansSince = now;
            return;
        }

        double limit = _engine == null ? LobbyIdleCloseS : MatchIdleCloseS;
        if (now - _noHumansSince > limit)
        {
            _log.LogInformation("Room {Code}: no humans, closing", Code);
            _closed = true;
        }
    }

    private void Shutdown()
    {
        _closed = true;
        _inbox.Writer.TryComplete();
        foreach (Seat s in _seats)
        {
            if (s.Conn != null)
            {
                s.Conn.Room = null;
                s.Conn.Seat = -1;
                s.Conn.EndJoin();
            }
        }

        _manager.Remove(this);
    }

    // ---------- helpers ----------

    private byte FreeColor()
    {
        for (int c = 0; c < MaxColors; c++)
        {
            bool used = false;
            foreach (Seat s in _seats)
            {
                if (s.Control != SeatControl.Empty && s.Color == c)
                {
                    used = true;
                    break;
                }
            }

            if (!used)
            {
                return (byte)c;
            }
        }

        return 0;
    }

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
