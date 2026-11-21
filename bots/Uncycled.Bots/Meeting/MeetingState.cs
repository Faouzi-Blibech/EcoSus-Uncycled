using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;

namespace Uncycled.Bots.Meeting
{
    /// <summary>One thing the bot itself holds: a witnessed act, a sighting near a pile, or its own zone.</summary>
    public sealed class EvidenceItem
    {
        public const string SawDump = "saw_dump";
        public const string SawHit = "saw_hit";
        public const string NearPile = "near_pile";
        public const string MyZone = "my_zone";

        public EvidenceItem(string kind, byte? seat, string zone, float time)
        {
            Kind = kind;
            Seat = seat;
            Zone = zone;
            Time = time;
        }

        /// <summary>saw_dump, saw_hit, near_pile or my_zone.</summary>
        public string Kind { get; }

        /// <summary>The seat concerned (null for my_zone).</summary>
        public byte? Seat { get; }

        public string Zone { get; }

        public float Time { get; }
    }

    /// <summary>
    /// Everything a decider may use, built from one bot's own SeatView, memory and the public chat:
    /// never another seat's private evidence.
    /// </summary>
    public sealed class MeetingState
    {
        /// <summary>Room or match id, for the decision log only.</summary>
        public string MatchId { get; set; } = "";

        /// <summary>Seats at the table (from the match config), for the model prompt.</summary>
        public int Seats { get; set; } = 8;

        /// <summary>Polluters at the table (from the match config), for the model prompt.</summary>
        public int Polluters { get; set; } = 2;

        /// <summary>True when the talker is answering an accusation against this bot (a reply is due).</summary>
        public bool Replying { get; set; }

        public byte Self { get; set; }

        public Role Role { get; set; }

        /// <summary>The other polluters (empty for a citizen).</summary>
        public IReadOnlyList<byte> Partners { get; set; } = new List<byte>();

        /// <summary>Seats not known to be out, this bot included.</summary>
        public IReadOnlyList<byte> Living { get; set; } = new List<byte>();

        public MeetingStage Stage { get; set; }

        public float StageLeft { get; set; }

        public IReadOnlyList<ClueInfo> Clues { get; set; } = new List<ClueInfo>();

        public IReadOnlyList<ChatAct> Chat { get; set; } = new List<ChatAct>();

        /// <summary>Suspicion score per living seat (own evidence plus hearsay).</summary>
        public IReadOnlyDictionary<byte, float> Suspicion { get; set; } = new Dictionary<byte, float>();

        /// <summary>Heat per living seat, in [0, 1].</summary>
        public IReadOnlyDictionary<byte, float> Heat { get; set; } = new Dictionary<byte, float>();

        public IReadOnlyList<EvidenceItem> Evidence { get; set; } = new List<EvidenceItem>();

        /// <summary>Zone ids of the map.</summary>
        public IReadOnlyList<string> Zones { get; set; } = new List<string>();

        public bool IsPartner(byte seat)
        {
            for (int i = 0; i < Partners.Count; i++)
            {
                if (Partners[i] == seat)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsLiving(byte seat)
        {
            for (int i = 0; i < Living.Count; i++)
            {
                if (Living[i] == seat)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The zone this bot was really in nearest the time of the latest clue (what another bot that saw it
        /// then will check an alibi against); the latest own zone when the meeting has no clue.
        /// </summary>
        public string? AlibiZone()
        {
            float? clueAt = null;
            foreach (ClueInfo c in Clues)
            {
                if (clueAt == null || c.Time > clueAt.Value)
                {
                    clueAt = c.Time;
                }
            }

            EvidenceItem? best = null;
            foreach (EvidenceItem e in Evidence)
            {
                if (e.Kind != EvidenceItem.MyZone)
                {
                    continue;
                }

                bool better = best == null
                    || (clueAt != null ? Math.Abs(e.Time - clueAt.Value) < Math.Abs(best.Time - clueAt.Value) : e.Time > best.Time);
                if (better)
                {
                    best = e;
                }
            }

            return best?.Zone;
        }

        public float SuspicionOf(byte seat) => Suspicion.TryGetValue(seat, out float v) ? v : 0f;

        public float HeatOf(byte seat) => Heat.TryGetValue(seat, out float v) ? v : 0f;

        /// <summary>True when another seat has accused this bot in the chat.</summary>
        public bool AccusedInChat()
        {
            foreach (ChatAct a in Chat)
            {
                if (a.Kind == "accuse" && a.Seat == Self && a.Speaker != Self)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when this bot already said the phrase (optionally about that seat).</summary>
        public bool SaidOwn(string phraseId, byte? seat = null)
        {
            foreach (ChatAct a in Chat)
            {
                if (a.Speaker == Self && a.PhraseId == phraseId && (seat == null || a.Seat == seat))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when this bot already gave an alibi (i_was or with).</summary>
        public bool GaveAlibi()
        {
            foreach (ChatAct a in Chat)
            {
                if (a.Speaker == Self && a.Kind == "alibi")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
