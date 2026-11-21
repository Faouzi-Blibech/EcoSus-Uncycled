namespace Sim.Uncycled
{
    /// <summary>City pollution 0-100. Polluters win when it is full.</summary>
    public sealed class PollutionMeter
    {
        public const float Min = 0f;
        public const float Max = 100f;

        public float Value { get; private set; }

        public bool IsFull => Value >= Max;

        /// <summary>Adds (or, with a negative amount, removes) pollution, clamped to 0-100.</summary>
        internal void Add(float amount)
        {
            float v = Value + amount;
            if (v < Min)
            {
                v = Min;
            }

            if (v > Max)
            {
                v = Max;
            }

            Value = v;
        }
    }
}
