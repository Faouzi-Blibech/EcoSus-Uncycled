using System;

namespace Uncycled.Bots.Neural
{
    /// <summary>
    /// A per-day call allowance (UTC): TryConsume is false once the cap is used up, and the count starts
    /// again at 00:00 UTC. Thread-safe; one instance per server process.
    /// </summary>
    public sealed class DailyBudget
    {
        private readonly object _lock = new object();
        private readonly int _cap;
        private readonly Func<DateTime> _utcNow;
        private DateTime _day;
        private int _used;

        /// <param name="utcNow">The clock; it must return UTC (a Local time is converted, an Unspecified one is taken as UTC).</param>
        public DailyBudget(int cap, Func<DateTime> utcNow)
        {
            _cap = cap;
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _day = Today();
        }

        private DateTime Today()
        {
            DateTime now = _utcNow();
            return (now.Kind == DateTimeKind.Local ? now.ToUniversalTime() : now).Date;
        }

        public bool TryConsume()
        {
            lock (_lock)
            {
                DateTime today = Today();
                if (today != _day)
                {
                    _day = today;
                    _used = 0;
                }

                if (_used >= _cap)
                {
                    return false;
                }

                _used++;
                return true;
            }
        }
    }
}
