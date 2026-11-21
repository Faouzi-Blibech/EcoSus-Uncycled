using System.Diagnostics;
using Sim.Match;
using Uncycled.Bots;

namespace Uncycled.Server.Bots;

/// <summary>
/// One bot seat's brain behind a safety net. The room tick calls Think synchronously, so a brain
/// that throws, or that keeps taking longer than the budget, is replaced for this seat by a fresh
/// scripted brain (logged once); if that one throws too, the seat falls back to IdleBot. The room
/// never sees an exception from a brain.
/// </summary>
public sealed class GuardedBrain : IBotBrain
{
    // Slow calls are counted over a window of calls (20 per second), so a brain that is slow only
    // on its periodic decision still gets caught, and one cold start (JIT) does not.
    private const int SlowWindowCalls = 600;

    // With fewer bots than this a slow tick cannot be told apart from one slow brain.
    private const int MinBotsForPause = 3;

    private readonly Func<IBotBrain> _makeFallback;
    private readonly ILogger _log;
    private readonly string _who;
    private readonly double _slowMs;
    private readonly int _slowStrikes;
    private IBotBrain _active;
    private int _stage;
    private int _calls;
    private int _slow;
    private bool _measured;
    private double _lastMs;

    public GuardedBrain(IBotBrain primary, Func<IBotBrain> makeFallback, ILogger log, string who, double slowMs, int slowStrikes)
    {
        _active = primary;
        _makeFallback = makeFallback;
        _log = log;
        _who = who;
        _slowMs = slowMs;
        _slowStrikes = slowStrikes;
    }

    /// <summary>0 = the chosen brain, 1 = scripted fallback, 2 = idle.</summary>
    public int Stage => _stage;

    public void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send)
    {
        long t0 = Stopwatch.GetTimestamp();
        try
        {
            _active.Think(engine, seat, dt, send);
        }
        catch (Exception ex)
        {
            Degrade("threw " + ex.GetType().Name + ": " + ex.Message);
            return;
        }

        if (_stage != 0)
        {
            return;
        }

        _lastMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        _measured = true;
        if (++_calls > SlowWindowCalls)
        {
            _calls = 1;
            _slow = 0;
        }
    }

    /// <summary>
    /// Ends one room tick: counts a strike for each brain whose call overran the budget in it, unless
    /// most of the bots overran in the same tick. That is a pause of the whole tick (GC, a CPU quota,
    /// the first JIT of every seat), which no single brain caused. The room calls this once per tick,
    /// after every Think.
    /// </summary>
    public static void SettleTick(IReadOnlyList<GuardedBrain> brains)
    {
        int measured = 0;
        int slow = 0;
        foreach (GuardedBrain b in brains)
        {
            if (b._measured)
            {
                measured++;
                if (b._lastMs > b._slowMs)
                {
                    slow++;
                }
            }
        }

        bool wholeTickPause = measured >= MinBotsForPause && slow * 2 > measured;
        foreach (GuardedBrain b in brains)
        {
            if (!b._measured)
            {
                continue;
            }

            b._measured = false;
            if (!wholeTickPause && b._lastMs > b._slowMs && ++b._slow >= b._slowStrikes)
            {
                b.Degrade("took over " + b._slowMs.ToString("0.#") + " ms " + b._slow + " times");
            }
        }
    }

    private void Degrade(string reason)
    {
        _slow = 0;
        _calls = 0;
        if (_stage == 0)
        {
            _stage = 1;
            _log.LogWarning("Bot {Who}: brain {Reason}; the scripted brain plays this seat", _who, reason);
            try
            {
                _active = _makeFallback();
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Bot {Who}: scripted fallback could not be built", _who);
            }
        }
        else if (_stage == 1)
        {
            _log.LogWarning("Bot {Who}: scripted brain {Reason}; the seat goes idle", _who, reason);
        }

        _stage = 2;
        _active = new IdleBot();
    }
}
