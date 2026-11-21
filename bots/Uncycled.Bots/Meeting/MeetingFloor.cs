namespace Uncycled.Bots.Meeting
{
    /// <summary>One per room: at most one bot line every MinGapS seconds across all the room's bots.</summary>
    public sealed class MeetingFloor
    {
        public const float MinGapS = 2f;

        private float? _lastAt;

        /// <summary>True (and the floor is held) when no bot spoke in the last MinGapS seconds.</summary>
        public bool TryTake(float now)
        {
            if (_lastAt != null && now >= _lastAt.Value && now - _lastAt.Value < MinGapS)
            {
                return false;
            }

            _lastAt = now;
            return true;
        }
    }
}
