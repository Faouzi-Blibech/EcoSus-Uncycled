using Sim.Core;

namespace Uncycled.Bots.Neural
{
    /// <summary>Picks an action index from an observation and a validity mask.</summary>
    public interface IPolicy
    {
        /// <param name="temperature">0 = the best valid action; above 0 = sample from the softmax of logits / temperature.</param>
        int Act(float[] obs, bool[] mask, float temperature, IRandom rng);
    }
}
