using System;
using System.Collections.Generic;
using Sim.Match;
using Uncycled.Bots.Meeting;

namespace Uncycled.Bots
{
    /// <summary>One accusation made in a meeting: accuser named target (the chat will feed these later).</summary>
    public readonly struct Accusation
    {
        public Accusation(byte accuser, byte target)
        {
            Accuser = accuser;
            Target = target;
        }

        public byte Accuser { get; }

        public byte Target { get; }
    }

    /// <summary>
    /// What everyone at the table learned from one meeting: who voted for whom (public in the result),
    /// who was ejected, and who accused whom. Votes lists the living voters only.
    /// </summary>
    public sealed class MeetingOutcome
    {
        public MeetingOutcome(IReadOnlyList<VoteRecord> votes, byte? ejected, IReadOnlyList<Accusation> accusations)
        {
            Votes = votes ?? throw new ArgumentNullException(nameof(votes));
            Ejected = ejected;
            Accusations = accusations ?? throw new ArgumentNullException(nameof(accusations));
        }

        public IReadOnlyList<VoteRecord> Votes { get; }

        public byte? Ejected { get; }

        public IReadOnlyList<Accusation> Accusations { get; }
    }

    /// <summary>Something that wants to hear how a meeting ended (a bot memory or a brain holding one).</summary>
    public interface IMeetingObserver
    {
        void OnMeetingEnded(MeetingOutcome outcome);
    }

    /// <summary>A seat that names a suspect during a meeting. Null when it accuses nobody (or the meeting is over).</summary>
    public interface IAccuser
    {
        byte? Accusation { get; }
    }

    /// <summary>
    /// Turns the public meeting_result event into a MeetingOutcome and hands it to every registered
    /// observer. The host (training host, room) calls Handle with the events of each tick.
    /// With a MeetingLog the feed owns it: it adds the chat lines (ChatEvent), clears it when a meeting
    /// starts, and the accusations of the outcome (so Round 5 heat) are the distinct (accuser, target)
    /// pairs of that log; a scripted bot's vote (IAccuser) is added to the log as an accusation at
    /// the result. Without a log the accusations are the IAccuser votes alone.
    /// </summary>
    public sealed class MeetingFeed
    {
        private readonly List<byte> _seats = new List<byte>();
        private readonly List<IMeetingObserver> _observers = new List<IMeetingObserver>();
        private readonly List<IAccuser?> _accusers = new List<IAccuser?>();

        public MeetingFeed(MeetingLog? log = null)
        {
            Log = log;
        }

        /// <summary>The chat of the meeting in progress (null when the feed has no log); talkers and memories read it.</summary>
        public MeetingLog? Log { get; }

        public void Add(byte seat, IMeetingObserver observer, IAccuser? accuser = null)
        {
            _seats.Add(seat);
            _observers.Add(observer ?? throw new ArgumentNullException(nameof(observer)));
            _accusers.Add(accuser);
        }

        /// <param name="matchTime">Match time stamped on the chat acts (heat does not use it).</param>
        public void Handle(IReadOnlyList<GameEvent> events, float matchTime = 0f)
        {
            foreach (GameEvent e in events)
            {
                if (e is MeetingStartedEvent)
                {
                    Log?.Clear();
                }
                else if (e is ChatEvent chat)
                {
                    Log?.Add(chat, matchTime);
                }
                else if (e is MeetingResultEvent result)
                {
                    Deliver(result, matchTime);
                }
            }
        }

        private void Deliver(MeetingResultEvent result, float matchTime)
        {
            var accusations = new List<Accusation>();
            for (int i = 0; i < _seats.Count; i++)
            {
                byte? target = _accusers[i]?.Accusation;
                if (target == null || !Voted(result, _seats[i]))
                {
                    continue;
                }

                if (Log == null)
                {
                    accusations.Add(new Accusation(_seats[i], target.Value));
                }
                else if (!Log.WasAccusedBy(target.Value, _seats[i]))
                {
                    Log.AddAccusation(_seats[i], target.Value, matchTime);
                }
            }

            if (Log != null)
            {
                foreach ((byte accuser, byte target) in Log.DistinctAccusations())
                {
                    accusations.Add(new Accusation(accuser, target));
                }
            }

            var outcome = new MeetingOutcome(result.Votes, result.Ejected, accusations);
            foreach (IMeetingObserver observer in _observers)
            {
                observer.OnMeetingEnded(outcome);
            }
        }

        /// <summary>Only a living voter takes part in the meeting.</summary>
        private static bool Voted(MeetingResultEvent result, byte seat)
        {
            foreach (VoteRecord v in result.Votes)
            {
                if (v.Voter == seat)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
