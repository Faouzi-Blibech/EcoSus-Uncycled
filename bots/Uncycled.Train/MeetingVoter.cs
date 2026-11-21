using Sim.Core;
using Sim.Match;
using Uncycled.Bots;

namespace Uncycled.Train
{
    /// <summary>
    /// Casts a seat's vote during the voting stage with the suspicion model (the learner's action is
    /// only Stay in a meeting, so the host votes for it). One vote per meeting; a rejected vote
    /// (target out) falls back to a skip.
    /// </summary>
    internal sealed class MeetingVoter : IAccuser
    {
        private readonly SuspicionModel _suspicion = new SuspicionModel();
        private const int MaxRejected = 3;

        private bool _voted;
        private byte? _target;

        /// <summary>The seat voted for in this meeting (the learner's stand-in for a spoken accusation).</summary>
        public byte? Accusation => _voted ? _target : null;
        private int _rejected;

        public void Step(MatchEngine engine, byte seat, SeatView view, BotMemory memory)
        {
            if (view.Phase != MatchPhase.Meeting || view.Meeting == null || view.Me.Status != PlayerStatus.Active)
            {
                _voted = false;
                _rejected = 0;
                return;
            }

            if (_voted || view.Meeting.Stage != MeetingStage.Voting || view.Meeting.MyVoteCast)
            {
                return;
            }

            _suspicion.Update(memory, view);
            byte? target = _suspicion.Vote(view, view.Me.Role, view.Me.Partners);
            CommandResult r = engine.Apply(seat, new VoteCommand(target));
            if (!r.IsOk && target != null && _rejected < MaxRejected)
            {
                // the target is out (an ejection or poison the seat never saw): remember it and vote the next suspect next tick
                memory.MarkOut(target.Value);
                _rejected++;
                return;
            }

            if (!r.IsOk && target != null)
            {
                engine.Apply(seat, new VoteCommand(null));
                target = null;
            }

            _target = target;
            _voted = true;
        }
    }
}
