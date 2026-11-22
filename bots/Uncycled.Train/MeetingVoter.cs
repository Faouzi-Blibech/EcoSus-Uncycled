using System;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots;
using Uncycled.Bots.Meeting;

namespace Uncycled.Train
{
    /// <summary>
    /// A learner seat in a meeting (its action is only Stay there, so the host talks and votes for it): the same
    /// MeetingTalker a scripted seat or a live bot gets, asking the meeting brain (the rule decider by default,
    /// meeting.clef_for = none). Chat lines and one vote per meeting; the v2 network plays the round only.
    /// </summary>
    internal sealed class MeetingVoter : IAccuser
    {
        private readonly MeetingTalker _talker;
        private readonly MeetingLog _log;
        private byte? _vote;

        public MeetingVoter(IMeetingDecider decider, MeetingFloor floor, MeetingLog log, Random rng)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _talker = new MeetingTalker(decider, decider, floor, rng);
        }

        /// <summary>The seat voted for in this meeting (null for a skip or before the vote): the feed counts it as an accusation.</summary>
        public byte? Accusation => _vote;

        public void Step(MatchEngine engine, byte seat, SeatView view, BotMemory memory)
        {
            if (view.Phase == MatchPhase.Playing)
            {
                _vote = null;
            }

            _talker.Step(engine, seat, view, memory, _log, view.MatchTime, cmd =>
            {
                if (cmd is VoteCommand vote)
                {
                    _vote = vote.Target;
                }

                engine.Apply(seat, cmd);
            });
        }
    }
}
