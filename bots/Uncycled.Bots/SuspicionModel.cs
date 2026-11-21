using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Bots.Meeting;

namespace Uncycled.Bots
{
    /// <summary>
    /// Who does this bot suspect, and why (team doc 06 section 4). Only what the bot itself saw:
    /// a hit or dump it witnessed (100), and own sightings in a pile's zone near the pile's time (30).
    /// Vote the top seat at 40 or more, else skip. Recomputed from memory on every Update.
    /// </summary>
    public sealed class SuspicionModel
    {
        public const float DirectWeight = 100f;
        public const float NearPileWeight = 30f;
        public const float VoteThreshold = 40f;
        public const float NearPileWindowS = 20f;

        private readonly Dictionary<byte, float> _scores = new Dictionary<byte, float>();
        private readonly Dictionary<byte, float> _chat = new Dictionary<byte, float>();
        private IReadOnlyList<ClueInfo> _clues = new List<ClueInfo>();
        private BotMemory? _mem;

        public void Update(BotMemory mem, SeatView view)
        {
            _mem = mem;
            _scores.Clear();
            _chat.Clear();
            _clues = view.Meeting?.Clues ?? (IReadOnlyList<ClueInfo>)new List<ClueInfo>();
            byte me = view.Me.Seat;

            var direct = new HashSet<byte>();
            foreach (WitnessRecord r in mem.Witnessed)
            {
                if (r.Actor != me && direct.Add(r.Actor))
                {
                    Add(r.Actor, DirectWeight);
                }
            }

            if (view.Meeting == null)
            {
                return;
            }

            foreach (ClueInfo clue in view.Meeting.Clues)
            {
                foreach (byte seat in mem.SeenSeats)
                {
                    if (seat == me || mem.IsKnownOut(seat))
                    {
                        continue;
                    }

                    foreach (Sighting s in mem.SightingsOf(seat))
                    {
                        if (s.Zone == clue.ZoneId && System.Math.Abs(s.Time - clue.Time) <= NearPileWindowS)
                        {
                            Add(seat, NearPileWeight);
                            break;
                        }
                    }
                }
            }
        }

        public const float HearsayWeight = 15f;
        public const float ContradictionWeight = 30f;

        /// <summary>
        /// Adds what was said in the meeting on top of the last Update (replaces the previous chat
        /// part, so calling it again with a longer log never stacks): each other seat that accuses a
        /// seat adds HearsayWeight to it once; an alibi (i_was, with) whose zone does not match where
        /// this bot saw the speaker near a clue pile's time adds ContradictionWeight to the speaker
        /// once. Accusations of self and the bot's own lines are ignored.
        /// </summary>
        public void ApplyChat(MeetingLog log, BotMemory mem, byte self)
        {
            _chat.Clear();
            var accusedBy = new HashSet<(byte, byte)>();
            var contradicted = new HashSet<byte>();
            foreach (ChatAct a in log.Acts)
            {
                if (a.Speaker == self)
                {
                    continue;
                }

                if (a.Kind == "accuse" && a.Seat != null && a.Seat.Value != self && accusedBy.Add((a.Speaker, a.Seat.Value)))
                {
                    AddChat(a.Seat.Value, HearsayWeight);
                }
                else if (a.Kind == "alibi" && a.Zone != null && !contradicted.Contains(a.Speaker) && Contradicts(mem, a.Speaker, a.Zone))
                {
                    contradicted.Add(a.Speaker);
                    AddChat(a.Speaker, ContradictionWeight);
                }
            }
        }

        private bool Contradicts(BotMemory mem, byte speaker, string claimedZone)
        {
            foreach (ClueInfo clue in _clues)
            {
                bool saw = false;
                bool sawInClaimed = false;
                foreach (Sighting s in mem.SightingsOf(speaker))
                {
                    if (System.Math.Abs(s.Time - clue.Time) > NearPileWindowS)
                    {
                        continue;
                    }

                    saw = true;
                    sawInClaimed |= s.Zone == claimedZone;
                }

                if (saw && !sawInClaimed)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddChat(byte seat, float amount)
        {
            _chat[seat] = (_chat.TryGetValue(seat, out float v) ? v : 0f) + amount;
        }

        public float Score(byte seat)
        {
            float v = _scores.TryGetValue(seat, out float b) ? b : 0f;
            return v + (_chat.TryGetValue(seat, out float c) ? c : 0f);
        }

        /// <summary>Seat to vote for, or null to skip. A polluter never votes a partner.</summary>
        public byte? Vote(SeatView view, Role myRole, IReadOnlyList<byte> partners)
        {
            byte me = view.Me.Seat;
            byte? best = null;
            float bestScore = 0f;
            var seats = new HashSet<byte>(_scores.Keys);
            seats.UnionWith(_chat.Keys);
            foreach (byte seat in seats)
            {
                float score = Score(seat);
                if (seat == me || (_mem != null && _mem.IsKnownOut(seat)))
                {
                    continue;
                }

                if (myRole == Role.Polluter && Contains(partners, seat))
                {
                    continue;
                }

                if (best == null || score > bestScore || (score == bestScore && seat < best.Value))
                {
                    best = seat;
                    bestScore = score;
                }
            }

            return best != null && bestScore >= VoteThreshold ? best : null;
        }

        private void Add(byte seat, float amount)
        {
            _scores[seat] = Score(seat) + amount;
        }

        private static bool Contains(IReadOnlyList<byte> seats, byte seat)
        {
            for (int i = 0; i < seats.Count; i++)
            {
                if (seats[i] == seat)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
