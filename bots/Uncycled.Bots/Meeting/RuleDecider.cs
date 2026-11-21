using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core;
using Sim.Match;

namespace Uncycled.Bots.Meeting
{
    /// <summary>
    /// The offline path: the suspicion score picks what to accuse and whom to vote for, an accusation
    /// against the bot gets "not_me" then an alibi, otherwise it stays quiet. Synchronous.
    /// </summary>
    public sealed class RuleDecider : IMeetingDecider
    {
        public const string SourceName = "rule";

        /// <summary>A citizen accuses a seat at this score (one near-pile sighting); a polluter already at hearsay level.</summary>
        public const float AccuseThreshold = SuspicionModel.NearPileWeight;

        public string Name => SourceName;

        public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
        {
            return Task.FromResult(Decide(state));
        }

        public static MeetingDecision Decide(MeetingState s)
        {
            if (s.Stage == MeetingStage.Voting)
            {
                byte? top = TopSuspect(s, SuspicionModel.VoteThreshold);
                return top != null
                    ? new MeetingDecision { Vote = top, Source = SourceName }
                    : new MeetingDecision { SkipVote = true, Source = SourceName };
            }

            if (s.AccusedInChat())
            {
                if (!s.SaidOwn("not_me"))
                {
                    return MeetingDecision.Say(SourceName, "not_me");
                }

                if (!s.GaveAlibi())
                {
                    string? zone = AlibiZone(s);
                    if (zone != null)
                    {
                        return MeetingDecision.Say(SourceName, "i_was", null, zone);
                    }
                }
            }

            float threshold = s.Role == Role.Polluter ? SuspicionModel.HearsayWeight : AccuseThreshold;
            byte? target = TopSuspect(s, threshold);
            if (target != null && !SaidAccuseOf(s, target.Value))
            {
                return Accuse(s, target.Value);
            }

            return MeetingDecision.Quiet(SourceName);
        }

        private static MeetingDecision Accuse(MeetingState s, byte target)
        {
            // the strongest own evidence about the seat: a witnessed dump, a hit, then a sighting near a pile
            foreach (string kind in new[] { EvidenceItem.SawDump, EvidenceItem.SawHit, EvidenceItem.NearPile })
            {
                foreach (EvidenceItem e in s.Evidence)
                {
                    if (e.Kind == kind && e.Seat == target)
                    {
                        return MeetingDecision.Say(SourceName, kind, target, e.Zone);
                    }
                }
            }

            return MeetingDecision.Say(SourceName, "sus", target);
        }

        private static bool SaidAccuseOf(MeetingState s, byte seat)
        {
            foreach (ChatAct a in s.Chat)
            {
                if (a.Speaker == s.Self && a.Kind == "accuse" && a.Seat == seat)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The zone to give as an alibi: the own zone nearest the clue time (see MeetingState.AlibiZone), which
        /// is what a bot that saw this one then will check it against; a polluter instead lies with a zone it
        /// was not in and that holds no clue pile.
        /// </summary>
        internal static string? AlibiZone(MeetingState s)
        {
            string? real = s.AlibiZone();
            if (s.Role != Role.Polluter)
            {
                return real;
            }

            var clueZones = new HashSet<string>();
            foreach (Sim.Uncycled.ClueInfo c in s.Clues)
            {
                clueZones.Add(c.ZoneId);
            }

            foreach (string z in s.Zones)
            {
                if (z != real && !clueZones.Contains(z))
                {
                    return z;
                }
            }

            return real;
        }

        /// <summary>Highest suspicion at or above the threshold among seats this bot may name (ties: lowest seat).</summary>
        private static byte? TopSuspect(MeetingState s, float threshold)
        {
            byte? best = null;
            float bestScore = 0f;
            foreach (byte seat in s.Living)
            {
                if (seat == s.Self || (s.Role == Role.Polluter && s.IsPartner(seat)))
                {
                    continue;
                }

                float score = s.SuspicionOf(seat);
                if (score < threshold)
                {
                    continue;
                }

                if (best == null || score > bestScore || (score == bestScore && seat < best.Value))
                {
                    best = seat;
                    bestScore = score;
                }
            }

            return best;
        }
    }
}
