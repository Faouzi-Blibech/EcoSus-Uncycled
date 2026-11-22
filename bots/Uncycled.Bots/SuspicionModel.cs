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

        /// <summary>Seen close to the jammed machine or inside the warned gas room when the alarm went up: the accusation level, not a vote alone.</summary>
        public const float NearAlarmWeight = NearPileWeight;

        /// <summary>A player seen leaving a dirty spot dirty after a clean time: one cue, below the vote (40) and the accusation (30) thresholds, so it needs a second one.</summary>
        public const float FakeCleanWeight = 25f;
        public const float VoteThreshold = 40f;
        public const float NearPileWindowS = 20f;

        /// <summary>Own sightings of an accused outside the claimed zone must span this long to contradict an eyewitness line.</summary>
        public const float AlibiSpanS = 10f;

        private readonly Dictionary<byte, float> _scores = new Dictionary<byte, float>();
        private readonly Dictionary<byte, float> _chat = new Dictionary<byte, float>();
        private IReadOnlyList<ClueInfo> _clues = new List<ClueInfo>();
        private BotMemory? _mem;
        private float _now;

        public void Update(BotMemory mem, SeatView view)
        {
            _mem = mem;
            _now = view.MatchTime;
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

            foreach (WitnessRecord r in mem.Inferred)
            {
                if (r.Actor != me && direct.Add(r.Actor))
                {
                    Add(r.Actor, FakeCleanWeight);
                }
            }

            // seats this bot saw close to the machine when it jammed, or inside the room when the gas warning went up: a weaker cue than a witnessed act
            var nearAlarm = new HashSet<byte>();
            foreach (AlarmSighting a in mem.RecentAlarms(view))
            {
                foreach (byte seat in a.Near)
                {
                    if (seat != me && !mem.IsKnownOut(seat) && nearAlarm.Add(seat))
                    {
                        Add(seat, NearAlarmWeight);
                    }
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

        /// <summary>A specific eyewitness accusation heard in chat (saw_dump or saw_hit with a zone): a vote by itself.</summary>
        public const float EyewitnessWeight = VoteThreshold;
        public const float ContradictionWeight = 30f;

        /// <summary>
        /// Adds what was said in the meeting on top of the last Update (replaces the previous chat
        /// part, so calling it again with a longer log never stacks). Each other seat that accuses a seat
        /// counts once per accused:
        /// a vague accusation (sus, near_pile) adds HearsayWeight; a specific eyewitness line (saw_dump or
        /// saw_hit, with a zone: the speaker says he saw the act) adds EyewitnessWeight, a vote by itself, like a
        /// real player who is told "I saw it". The eyewitness line adds nothing when this bot's own sightings
        /// contradict it (the accused was in sight elsewhere near a clue time), and drops to hearsay when this bot
        /// already suspects the accuser (AccuseThreshold from its own evidence) or when a player it does not
        /// suspect spoke for the accused (an alibi line naming him). An alibi (i_was, with) whose zone does not
        /// match where this bot saw the speaker near a clue pile's time adds ContradictionWeight to the speaker
        /// once. Accusations of self and the bot's own lines are ignored. The bots have no per-seat skill
        /// (BotsSettings.Skill is one value for every bot), so the eyewitness weight is one constant.
        /// </summary>
        public void ApplyChat(MeetingLog log, BotMemory mem, byte self)
        {
            _chat.Clear();
            var claims = new Dictionary<(byte, byte), string?>(); // (accuser, accused) -> the claimed zone of an eyewitness line, null for a vague one
            var nearOnly = new HashSet<(byte, byte)>(); // claims that only say "seen close by" (saw_near): worth NearAlarmWeight, not a vote
            var contradicted = new HashSet<byte>();
            foreach (ChatAct a in log.Acts)
            {
                if (a.Speaker == self)
                {
                    continue;
                }

                if (a.Kind == "accuse" && a.Seat != null && a.Seat.Value != self)
                {
                    var key = (a.Speaker, a.Seat.Value);
                    bool witness = IsEyewitness(a);
                    bool near = a.PhraseId == EvidenceItem.SawNear && a.Zone != null;
                    if (!claims.TryGetValue(key, out string? zone) || ((witness || near) && zone == null) || (witness && nearOnly.Contains(key)))
                    {
                        claims[key] = witness || near ? a.Zone : null;
                        if (near && !witness)
                        {
                            nearOnly.Add(key);
                        }
                        else
                        {
                            nearOnly.Remove(key);
                        }
                    }
                }
                else if (a.Kind == "alibi" && a.Zone != null && !contradicted.Contains(a.Speaker) && Contradicts(mem, a.Speaker, a.Zone))
                {
                    contradicted.Add(a.Speaker);
                    AddChat(a.Speaker, ContradictionWeight);
                }
            }

            foreach (KeyValuePair<(byte, byte), string?> claim in claims)
            {
                byte accuser = claim.Key.Item1;
                byte accused = claim.Key.Item2;
                if (claim.Value == null)
                {
                    AddChat(accused, HearsayWeight);
                }
                else if (nearOnly.Contains(claim.Key) ? Contradicts(mem, accused, claim.Value) : ContradictsClaim(mem, accused, claim.Value))
                {
                    continue; // this bot saw him elsewhere then: the line does not hold
                }
                else if (BaseScore(accuser) >= RuleDecider.AccuseThreshold || DefendedBySomeoneTrusted(log, accused, accuser, self))
                {
                    AddChat(accused, HearsayWeight);
                }
                else
                {
                    AddChat(accused, nearOnly.Contains(claim.Key) ? NearAlarmWeight : EyewitnessWeight);
                }
            }
        }

        private static bool IsEyewitness(ChatAct a) =>
            a.Zone != null && (a.PhraseId == EvidenceItem.SawDump || a.PhraseId == EvidenceItem.SawHit);

        /// <summary>This bot's own evidence on a seat (before any chat).</summary>
        private float BaseScore(byte seat) => _scores.TryGetValue(seat, out float v) ? v : 0f;

        /// <summary>An alibi line naming the accused (a vouch) from a seat other than the accuser, the accused and this bot, that this bot does not suspect.</summary>
        private bool DefendedBySomeoneTrusted(MeetingLog log, byte accused, byte accuser, byte self)
        {
            foreach (ChatAct d in log.Acts)
            {
                if (d.Kind == "alibi" && d.Seat == accused && d.Speaker != accused && d.Speaker != accuser && d.Speaker != self
                    && BaseScore(d.Speaker) < RuleDecider.AccuseThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// An eyewitness line says the accused did an act in a zone. It carries no act time, so the claim can
        /// refer to the last NearPileWindowS seconds before the meeting started (the window the alibi check
        /// uses around a clue pile). It is contradicted when a clue pile time puts the accused elsewhere
        /// (Contradicts) or when this bot's own sightings in that window saw the accused outside the claimed
        /// zone at least twice, spanning AlibiSpanS or more, and never in it. A single glimpse elsewhere does not
        /// discredit a true witness. Only this bot's own memory is read.
        /// </summary>
        private bool ContradictsClaim(BotMemory mem, byte accused, string claimedZone)
        {
            if (Contradicts(mem, accused, claimedZone))
            {
                return true;
            }

            float end = mem.MeetingStartedAt ?? _now;
            int seenElsewhere = 0;
            float first = float.MaxValue;
            float last = float.MinValue;
            foreach (Sighting s in mem.SightingsOf(accused))
            {
                if (s.Time > end || s.Time < end - NearPileWindowS)
                {
                    continue;
                }

                if (s.Zone == claimedZone)
                {
                    return false;
                }

                seenElsewhere++;
                first = System.Math.Min(first, s.Time);
                last = System.Math.Max(last, s.Time);
            }

            // a real alibi, not a glimpse: seen elsewhere at least twice, spanning AlibiSpanS or more
            return seenElsewhere >= 2 && last - first >= AlibiSpanS;
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
