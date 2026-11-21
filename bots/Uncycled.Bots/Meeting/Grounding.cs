using System.Collections.Generic;
using Sim.Core;
using Sim.Match;

namespace Uncycled.Bots.Meeting
{
    /// <summary>
    /// Checks a decider's choice against what this bot may really say. A citizen only claims what it
    /// holds (a witness record, its own sighting, its own zone). A polluter may lie about acts and
    /// zones but never accuses or votes for its partner, and defends it at most once, only when the
    /// partner is hot and it is cool. Nobody names itself or a seat that is out. An ungrounded pick
    /// is replaced by the closest grounded phrase, else "hmm" (or no vote).
    /// </summary>
    public static class Grounding
    {
        /// <summary>Round 5 heat (BotMemory.HeatOf, shares of the living) at or above which a partner counts as hot: two distinct accusers of 8 seats (0.25) or recorded heat of about that size from earlier meetings; one accuser (0.125) is not enough.</summary>
        public const float HotHeat = 0.25f;

        /// <summary>Round 5 heat at or below which the polluter itself counts as cool: at most one accuser at 7 or 8 living (1/7 = 0.143, 1/8 = 0.125) and little heat carried from earlier meetings.</summary>
        public const float CoolHeat = 0.15f;

        public static MeetingDecision Check(MeetingDecision d, MeetingState s)
        {
            return s.Stage == MeetingStage.Voting ? CheckVote(d, s) : CheckSpeak(d, s);
        }

        // ---------- vote ----------

        private static MeetingDecision CheckVote(MeetingDecision d, MeetingState s)
        {
            if (!d.SkipVote && d.Vote != null && IsTarget(s, d.Vote.Value))
            {
                return new MeetingDecision { Vote = d.Vote, Source = d.Source };
            }

            return new MeetingDecision { SkipVote = true, Source = d.Source };
        }

        // ---------- speak ----------

        private static MeetingDecision CheckSpeak(MeetingDecision d, MeetingState s)
        {
            string src = d.Source;
            if (!d.Speak || string.IsNullOrEmpty(d.PhraseId))
            {
                return MeetingDecision.Quiet(src);
            }

            bool pol = s.Role == Role.Polluter;
            byte? seat = d.Seat;
            string? zone = d.Zone;
            bool zoneOk = zone != null && OnMap(s, zone);

            switch (d.PhraseId)
            {
                case "hmm":
                case "idk":
                case "skip_no_proof":
                    return MeetingDecision.Say(src, d.PhraseId!);

                case "not_me":
                    if (!s.AccusedInChat())
                    {
                        return Hmm(src);
                    }

                    if (!s.SaidOwn("not_me"))
                    {
                        return MeetingDecision.Say(src, "not_me");
                    }

                    // a second denial becomes the alibi (as the rule path does), and then nothing more
                    string? alibi = s.GaveAlibi() ? null : RuleDecider.AlibiZone(s);
                    return alibi != null ? MeetingDecision.Say(src, "i_was", null, alibi) : Hmm(src);

                case "sus":
                    if (seat == null || !IsTarget(s, seat.Value))
                    {
                        return Hmm(src);
                    }

                    return pol || s.SuspicionOf(seat.Value) > 0f ? MeetingDecision.Say(src, "sus", seat) : Hmm(src);

                case "vote":
                    if (seat == null || !IsTarget(s, seat.Value))
                    {
                        return Hmm(src);
                    }

                    return pol || s.SuspicionOf(seat.Value) > 0f ? MeetingDecision.Say(src, "vote", seat) : Hmm(src);

                case "saw_dump":
                case "saw_hit":
                case "near_pile":
                    return CheckAccuse(d.PhraseId!, seat, zone, zoneOk, s, src);

                case "i_was":
                    return CheckAlibi(zone, zoneOk, s, src);

                case "with":
                    return CheckWith(seat, zone, zoneOk, s, src);

                case "where":
                    return seat != null && IsLiving(s, seat.Value) ? MeetingDecision.Say(src, "where", seat) : Hmm(src);

                case "agree":
                    return CheckAgree(seat, s, src);

                case "doubt":
                case "lying":
                    return CheckDoubt(d.PhraseId!, seat, s, src);

                default:
                    return Hmm(src);
            }
        }

        private static MeetingDecision CheckAccuse(string id, byte? seat, string? zone, bool zoneOk, MeetingState s, string src)
        {
            if (seat == null || !IsTarget(s, seat.Value))
            {
                return Hmm(src);
            }

            if (s.Role == Role.Polluter)
            {
                // lying is its game: any living non-partner, any zone on the map
                return zoneOk ? MeetingDecision.Say(src, id, seat, zone) : MeetingDecision.Say(src, "sus", seat);
            }

            string kind = id;
            EvidenceItem? exact = null;
            EvidenceItem? sameKind = null;
            EvidenceItem? any = null;
            foreach (EvidenceItem e in s.Evidence)
            {
                if (e.Seat != seat || e.Kind == EvidenceItem.MyZone)
                {
                    continue;
                }

                any ??= e;
                if (e.Kind == kind)
                {
                    sameKind ??= e;
                    if (zoneOk && e.Zone == zone)
                    {
                        exact = e;
                    }
                }
            }

            EvidenceItem? use = exact ?? sameKind ?? any;
            if (use != null)
            {
                return MeetingDecision.Say(src, use.Kind, seat, use.Zone);
            }

            return s.SuspicionOf(seat.Value) > 0f ? MeetingDecision.Say(src, "sus", seat) : Hmm(src);
        }

        private static MeetingDecision CheckAlibi(string? zone, bool zoneOk, MeetingState s, string src)
        {
            if (s.Role == Role.Polluter)
            {
                return zoneOk ? MeetingDecision.Say(src, "i_was", null, zone) : Hmm(src);
            }

            // a citizen's alibi is always the zone it was in at the clue time: that is where a bot that saw it will look
            string? real = s.AlibiZone();
            return real != null ? MeetingDecision.Say(src, "i_was", null, real) : Hmm(src);
        }

        private static MeetingDecision CheckWith(byte? seat, string? zone, bool zoneOk, MeetingState s, string src)
        {
            if (seat == null || seat == s.Self || !IsLiving(s, seat.Value))
            {
                return CheckAlibi(zone, zoneOk, s, src);
            }

            if (s.Role == Role.Polluter)
            {
                if (s.IsPartner(seat.Value))
                {
                    return DefenceAllowed(s) && zoneOk ? MeetingDecision.Say(src, "with", seat, zone) : Hmm(src);
                }

                return zoneOk ? MeetingDecision.Say(src, "with", seat, zone) : Hmm(src);
            }

            // a citizen vouches only for a seat it saw in a zone it was in too
            if (zoneOk && Holds(s, EvidenceItem.MyZone, null, zone!) && Holds(s, EvidenceItem.NearPile, seat, zone!))
            {
                return MeetingDecision.Say(src, "with", seat, zone);
            }

            return CheckAlibi(zone, zoneOk, s, src);
        }

        private static MeetingDecision CheckAgree(byte? seat, MeetingState s, string src)
        {
            if (seat == null || seat == s.Self || !IsLiving(s, seat.Value))
            {
                return Hmm(src);
            }

            bool accused = false;
            foreach (ChatAct a in s.Chat)
            {
                if (a.Speaker != seat.Value || a.Kind != "accuse" || a.Seat == null)
                {
                    continue;
                }

                accused = true;
                if (s.Role == Role.Polluter && s.IsPartner(a.Seat.Value))
                {
                    return Hmm(src);
                }
            }

            return accused ? MeetingDecision.Say(src, "agree", seat) : Hmm(src);
        }

        private static MeetingDecision CheckDoubt(string id, byte? seat, MeetingState s, string src)
        {
            if (seat == null || !IsTarget(s, seat.Value))
            {
                return Hmm(src);
            }

            if (s.Role == Role.Polluter)
            {
                if (AccusedPartner(s, seat.Value))
                {
                    return DefenceAllowed(s) ? MeetingDecision.Say(src, id, seat) : Hmm(src);
                }

                return MeetingDecision.Say(src, id, seat);
            }

            bool accusedMe = false;
            foreach (ChatAct a in s.Chat)
            {
                if (a.Speaker == seat.Value && a.Kind == "accuse" && a.Seat == s.Self)
                {
                    accusedMe = true;
                }
            }

            return s.SuspicionOf(seat.Value) > 0f || accusedMe ? MeetingDecision.Say(src, id, seat) : Hmm(src);
        }

        // ---------- helpers ----------

        private static MeetingDecision Hmm(string src) => MeetingDecision.Say(src, "hmm");

        private static bool IsLiving(MeetingState s, byte seat) => seat != s.Self && s.IsLiving(seat);

        /// <summary>A seat this bot may accuse or vote for: living, not itself, and not its partner.</summary>
        private static bool IsTarget(MeetingState s, byte seat)
        {
            return IsLiving(s, seat) && !(s.Role == Role.Polluter && s.IsPartner(seat));
        }

        private static bool OnMap(MeetingState s, string zone)
        {
            foreach (string z in s.Zones)
            {
                if (z == zone)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Holds(MeetingState s, string kind, byte? seat, string zone)
        {
            foreach (EvidenceItem e in s.Evidence)
            {
                if (e.Kind == kind && e.Seat == seat && e.Zone == zone)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AccusedPartner(MeetingState s, byte speaker)
        {
            foreach (ChatAct a in s.Chat)
            {
                if (a.Speaker == speaker && a.Kind == "accuse" && a.Seat != null && s.IsPartner(a.Seat.Value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Defending the partner (vouching with it, or calling out a seat that accused it): only when
        /// a partner is hot and this bot is cool, and only once per meeting.
        /// </summary>
        private static bool DefenceAllowed(MeetingState s)
        {
            bool hot = false;
            foreach (byte p in s.Partners)
            {
                hot |= s.HeatOf(p) >= HotHeat;
            }

            if (!hot || s.HeatOf(s.Self) > CoolHeat)
            {
                return false;
            }

            foreach (ChatAct a in s.Chat)
            {
                if (a.Speaker != s.Self || a.Seat == null)
                {
                    continue;
                }

                bool vouch = a.PhraseId == "with" && s.IsPartner(a.Seat.Value);
                bool callOut = (a.PhraseId == "doubt" || a.PhraseId == "lying") && AccusedPartner(s, a.Seat.Value);
                if (vouch || callOut)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
