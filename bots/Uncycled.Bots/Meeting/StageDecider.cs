using System.Threading;
using System.Threading.Tasks;
using Sim.Core;
using Sim.Match;

namespace Uncycled.Bots.Meeting
{
    /// <summary>Asks one decider in the discussion stage and another in the vote stage (bots.json meeting.clef_for "vote": rules talk, the model votes; the default is "none").</summary>
    public sealed class StageDecider : IMeetingDecider
    {
        private readonly IMeetingDecider _discussion;
        private readonly IMeetingDecider _voting;

        public StageDecider(IMeetingDecider discussion, IMeetingDecider voting)
        {
            _discussion = discussion;
            _voting = voting;
        }

        public string Name => _discussion.Name + "+" + _voting.Name;

        public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
        {
            return (state.Stage == MeetingStage.Voting ? _voting : _discussion).DecideAsync(state, ct);
        }
    }
}
