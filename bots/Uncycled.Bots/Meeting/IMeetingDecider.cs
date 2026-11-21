using System.Threading;
using System.Threading.Tasks;

namespace Uncycled.Bots.Meeting
{
    /// <summary>
    /// Chooses what one bot says or votes in a meeting. The rule decider completes synchronously; a
    /// model decider may take seconds and must honour the token. The talker never waits on it.
    /// </summary>
    public interface IMeetingDecider
    {
        string Name { get; }

        Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct);
    }
}
