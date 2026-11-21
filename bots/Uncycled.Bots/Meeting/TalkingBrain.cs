using System;
using Sim.Core;
using Sim.Match;

namespace Uncycled.Bots.Meeting
{
    /// <summary>A brain with a memory of its own that a MeetingTalker can read, and a vote that can be left to the talker.</summary>
    public interface ITalkingSeat
    {
        /// <summary>The brain's memory; null until its first Think.</summary>
        BotMemory? Memory { get; }

        /// <summary>When true the brain does not vote in a meeting (the talker does).</summary>
        bool ExternalVote { get; set; }
    }

    /// <summary>
    /// Adds meeting talk to a brain: after the wrapped brain thinks, the seat's MeetingTalker gets its
    /// turn (every tick, so it resets outside meetings) and chats and votes with the same commands a
    /// human sends. The brain's memory reads the room's meeting chat for heat. If the talker ever
    /// throws, the talker is switched off for this seat and the brain votes by itself again.
    /// </summary>
    public sealed class TalkingBrain : IBotBrain, IMeetingObserver, IAccuser
    {
        private readonly IBotBrain _inner;
        private readonly ITalkingSeat _seat;
        private readonly MeetingTalker _talker;
        private readonly MeetingLog _log;
        private readonly Action<string>? _warn;
        private bool _failed;
        private bool _stepped;
        private byte? _vote;

        public TalkingBrain(IBotBrain inner, MeetingTalker talker, MeetingLog log, Action<string>? warn = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _seat = inner as ITalkingSeat ?? throw new ArgumentException("the brain has no memory the talker can read", nameof(inner));
            _talker = talker ?? throw new ArgumentNullException(nameof(talker));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _warn = warn;
            _seat.ExternalVote = true;
        }

        /// <summary>The wrapped brain's memory (null until its first Think).</summary>
        public BotMemory? Memory => _seat.Memory;

        /// <summary>The seat this bot voted for in the meeting in progress (null for a skip or no vote yet): the feed counts it as an accusation, as for the scripted brains.</summary>
        public byte? Accusation => _vote;

        public void OnMeetingEnded(MeetingOutcome outcome) => (_inner as IMeetingObserver)?.OnMeetingEnded(outcome);

        public void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send)
        {
            _inner.Think(engine, seat, dt, send);
            if (engine.Phase == MatchPhase.Playing)
            {
                _vote = null;
            }

            Action<ICommand> relay = cmd =>
            {
                if (cmd is VoteCommand vote)
                {
                    _vote = vote.Target;
                }

                send(cmd);
            };
            BotMemory? mem = _seat.Memory;
            if (_failed || mem == null)
            {
                return;
            }

            mem.Chat = _log;
            bool inMeeting = engine.Phase == MatchPhase.Meeting;
            if (!inMeeting && !_stepped)
            {
                return;
            }

            _stepped = inMeeting; // one more Step after the meeting lets the talker reset
            try
            {
                SeatView view = engine.ViewFor(seat);
                _talker.Step(engine, seat, view, mem, _log, view.MatchTime, relay);
            }
            catch (Exception ex)
            {
                _failed = true;
                _seat.ExternalVote = false;
                _warn?.Invoke("meeting talker of seat " + seat + " failed, the brain votes by itself: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
