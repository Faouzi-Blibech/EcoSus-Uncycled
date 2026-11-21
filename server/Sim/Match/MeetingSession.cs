using System.Collections.Generic;
using Sim.Core;
using Sim.Uncycled;

namespace Sim.Match
{
    public sealed class MeetingResult
    {
        public MeetingResult(IReadOnlyList<VoteRecord> votes, byte? ejected, bool tie)
        {
            Votes = votes;
            Ejected = ejected;
            Tie = tie;
        }

        public IReadOnlyList<VoteRecord> Votes { get; }

        public byte? Ejected { get; }

        public bool Tie { get; }
    }

    /// <summary>
    /// Vote counting. Plurality ejects; a tie between seats, or skips at least as many as the top
    /// seat, ejects nobody. With missedIsSkip, a missed vote counts as a skip.
    /// </summary>
    public static class VoteTally
    {
        public static MeetingResult Tally(IReadOnlyList<VoteRecord> votes, bool missedIsSkip)
        {
            var counts = new SortedDictionary<byte, int>();
            int skips = 0;
            foreach (VoteRecord v in votes)
            {
                if (v.Target == null)
                {
                    if (!v.Missed || missedIsSkip)
                    {
                        skips++;
                    }

                    continue;
                }

                int c;
                counts.TryGetValue(v.Target.Value, out c);
                counts[v.Target.Value] = c + 1;
            }

            int top = 0;
            int topSeats = 0;
            byte topSeat = 0;
            foreach (KeyValuePair<byte, int> kv in counts)
            {
                if (kv.Value > top)
                {
                    top = kv.Value;
                    topSeats = 1;
                    topSeat = kv.Key;
                }
                else if (kv.Value == top)
                {
                    topSeats++;
                }
            }

            if (top > 0 && topSeats == 1 && top > skips)
            {
                return new MeetingResult(votes, topSeat, false);
            }

            bool tie = top > 0 && (topSeats > 1 || top == skips);
            return new MeetingResult(votes, null, tie);
        }
    }

    /// <summary>
    /// One meeting: discussion, voting, results. Holds votes and chat counts. The engine drives
    /// the stage timers.
    /// </summary>
    public sealed class MeetingSession
    {
        private readonly List<byte> _voters;
        private readonly Dictionary<byte, byte?> _votes = new Dictionary<byte, byte?>();
        private readonly List<byte> _votedOrder = new List<byte>();
        private readonly Dictionary<byte, int> _chatCounts = new Dictionary<byte, int>();
        private readonly Dictionary<byte, int> _textCounts = new Dictionary<byte, int>();

        public MeetingSession(byte calledBy, MeetingCause cause, int? reportedPileId, IReadOnlyList<ClueInfo> clues, float taskBar, IReadOnlyList<byte> voters, float discussionS)
        {
            CalledBy = calledBy;
            Cause = cause;
            ReportedPileId = reportedPileId;
            Clues = clues;
            TaskBar = taskBar;
            _voters = new List<byte>(voters);
            Stage = MeetingStage.Discussion;
            StageTimeLeft = discussionS;
        }

        public byte CalledBy { get; }

        public MeetingCause Cause { get; }

        public int? ReportedPileId { get; }

        public IReadOnlyList<ClueInfo> Clues { get; }

        public float TaskBar { get; }

        /// <summary>Seats that were active at meeting start.</summary>
        public IReadOnlyList<byte> Voters => _voters;

        public MeetingStage Stage { get; private set; }

        public float StageTimeLeft { get; internal set; }

        public MeetingResult? Result { get; private set; }

        /// <summary>Seats that have voted, in vote order (never who they voted for).</summary>
        public IReadOnlyList<byte> VotedSeats => _votedOrder;

        public bool AllVoted => _votedOrder.Count >= _voters.Count;

        public bool HasVoted(byte seat) => _votes.ContainsKey(seat);

        /// <summary>True when the seat voted; target null = skip.</summary>
        public bool TryGetVote(byte seat, out byte? target) => _votes.TryGetValue(seat, out target);

        public bool IsVoter(byte seat) => _voters.Contains(seat);

        public int ChatCount(byte seat)
        {
            int c;
            _chatCounts.TryGetValue(seat, out c);
            return c;
        }

        internal void CountChat(byte seat)
        {
            _chatCounts[seat] = ChatCount(seat) + 1;
        }

        /// <summary>Typed chat lines the seat has sent in this meeting.</summary>
        public int TextCount(byte seat)
        {
            int c;
            _textCounts.TryGetValue(seat, out c);
            return c;
        }

        internal void CountText(byte seat)
        {
            _textCounts[seat] = TextCount(seat) + 1;
        }

        internal void BeginVoting(float votingS)
        {
            Stage = MeetingStage.Voting;
            StageTimeLeft = votingS;
        }

        internal void RecordVote(byte voter, byte? target)
        {
            _votes[voter] = target;
            _votedOrder.Add(voter);
        }

        /// <summary>Closes voting: every voter who did not vote gets a missed vote. Then tallies.</summary>
        internal MeetingResult Close(bool missedIsSkip, float resultsS)
        {
            var records = new List<VoteRecord>(_voters.Count);
            foreach (byte voter in _voters)
            {
                byte? target;
                if (_votes.TryGetValue(voter, out target))
                {
                    records.Add(new VoteRecord(voter, target, false));
                }
                else
                {
                    records.Add(new VoteRecord(voter, null, true));
                }
            }

            Result = VoteTally.Tally(records, missedIsSkip);
            Stage = MeetingStage.Results;
            StageTimeLeft = resultsS;
            return Result;
        }
    }
}
