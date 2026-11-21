using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Uncycled.Server.Rooms;

namespace Uncycled.Server.Net;

/// <summary>
/// One client socket. Outbound has two lanes: a latest-snapshot slot (a new snapshot replaces an
/// unsent one) and a reliable event queue capped at MaxQueuedEvents. A single send loop owns
/// all SendAsync calls. Everything here is safe to call from any thread.
/// </summary>
public sealed class Connection
{
    public const int MaxQueuedEvents = 256;
    private const double RateBurst = 60.0;
    private const double RatePerSecond = 30.0;

    private readonly WebSocket _ws;
    private readonly Microsoft.Extensions.Logging.ILogger _log;
    private readonly object _lock = new object();
    private readonly Queue<string> _events = new Queue<string>();
    private readonly Channel<byte> _wake = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private string? _snapshot;
    private WebSocketCloseStatus? _closeStatus;
    private string _closeDescription = "";
    private volatile bool _closeRequested;
    private volatile bool _receiveDone;
    private volatile bool _closed;
    private volatile Room? _room;
    private volatile int _seat = -1;
    private int _joining;

    // token bucket, touched only by the receive loop
    private double _tokens = RateBurst;
    private long _lastTicks = Stopwatch.GetTimestamp();
    private long _lastWarnTicks;

    public Connection(WebSocket ws, Microsoft.Extensions.Logging.ILogger log)
    {
        _ws = ws;
        _log = log;
    }

    public string Id { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>Set by the room loop while this connection owns a seat.</summary>
    public Room? Room
    {
        get => _room;
        set => _room = value;
    }

    public int Seat
    {
        get => _seat;
        set => _seat = value;
    }

    public bool IsClosed => _closed;

    public void MarkClosed() => _closed = true;

    public void MarkReceiveDone() => _receiveDone = true;

    /// <summary>Guards against two create/join messages in flight. False when already joining or joined.</summary>
    public bool TryBeginJoin() => Interlocked.CompareExchange(ref _joining, 1, 0) == 0;

    public void EndJoin() => Interlocked.Exchange(ref _joining, 0);

    /// <summary>Token bucket, 30 msg/s with a burst of 60. Receive loop only.</summary>
    public bool TryConsumeRate()
    {
        long now = Stopwatch.GetTimestamp();
        double dt = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;
        _tokens = Math.Min(RateBurst, _tokens + dt * RatePerSecond);
        if (_tokens < 1.0)
        {
            return false;
        }

        _tokens -= 1.0;
        return true;
    }

    /// <summary>True at most once per second, to avoid answering a flood with a flood.</summary>
    public bool ShouldWarnRate()
    {
        long now = Stopwatch.GetTimestamp();
        if ((now - _lastWarnTicks) / (double)Stopwatch.Frequency >= 1.0)
        {
            _lastWarnTicks = now;
            return true;
        }

        return false;
    }

    /// <summary>Queues a reliable message. A backlog over the cap aborts the socket.</summary>
    public void Enqueue(string json)
    {
        bool overflow = false;
        lock (_lock)
        {
            if (_events.Count >= MaxQueuedEvents)
            {
                overflow = true;
            }
            else
            {
                _events.Enqueue(json);
            }
        }

        if (overflow)
        {
            _log.LogWarning("Connection {Id}: event backlog over cap, aborting", Id);
            Abort();
            return;
        }

        _wake.Writer.TryWrite(0);
    }

    /// <summary>Replaces any unsent snapshot.</summary>
    public void SetSnapshot(string json)
    {
        lock (_lock)
        {
            _snapshot = json;
        }

        _wake.Writer.TryWrite(0);
    }

    /// <summary>Asks the send loop to flush, send a close frame and stop.</summary>
    public void Close(WebSocketCloseStatus status, string description)
    {
        _closeStatus = status;
        _closeDescription = description;
        _closeRequested = true;
        _wake.Writer.TryWrite(0);
    }

    public void Abort()
    {
        try
        {
            _ws.Abort();
        }
        catch (Exception)
        {
            // already gone
        }
    }

    /// <summary>No more messages will be queued: the send loop flushes and ends.</summary>
    public void CompleteSend() => _wake.Writer.TryComplete();

    public async Task RunSendLoopAsync()
    {
        try
        {
            while (await _wake.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_wake.Reader.TryRead(out _))
                {
                }

                if (!await DrainAsync().ConfigureAwait(false))
                {
                    break;
                }

                if (_closeRequested)
                {
                    break;
                }
            }

            await DrainAsync().ConfigureAwait(false);
            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
            {
                await _ws.CloseOutputAsync(
                    _closeStatus ?? WebSocketCloseStatus.NormalClosure,
                    _closeDescription,
                    CancellationToken.None).ConfigureAwait(false);
            }

            if (!_receiveDone && _closeRequested)
            {
                await Task.Delay(3000).ConfigureAwait(false);
                if (!_receiveDone)
                {
                    Abort();
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Connection {Id}: send loop ended with error", Id);
            Abort();
        }
    }

    private async Task<bool> DrainAsync()
    {
        while (true)
        {
            string? msg = null;
            lock (_lock)
            {
                if (_events.Count > 0)
                {
                    msg = _events.Dequeue();
                }
                else if (_snapshot != null)
                {
                    msg = _snapshot;
                    _snapshot = null;
                }
            }

            if (msg == null)
            {
                return true;
            }

            if (_ws.State != WebSocketState.Open)
            {
                return false;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(msg);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
