using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;

namespace Uncycled.Bots.Meeting
{
    /// <summary>
    /// Builds the MeetingState of one bot from its own SeatView, its memory and the public chat only:
    /// never the private witness record of another seat.
    /// </summary>
    public static class MeetingStateBuilder
    {
        private const int MaxOwnZones = 3;

        /// <param name="heatOf">Heat of a seat in [0, 1]; null uses the Round 5 heat of the memory (BotMemory.HeatOf, clamped to 1), which counts this meeting's chat accusations.</param>
        public static MeetingState Build(SeatView view, BotMemory mem, MeetingLog log, SuspicionModel suspicion, Func<byte, float>? heatOf)
        {
            MeetingView meeting = view.Meeting ?? throw new ArgumentException("The seat is not in a meeting.", nameof(view));
            byte self = view.Me.Seat;

            mem.Chat = log;
            suspicion.Update(mem, view);
            suspicion.ApplyChat(log, mem, self);

            var living = new List<byte>();
            if (meeting.Living.Count > 0)
            {
                living.AddRange(meeting.Living);
            }
            else
            {
                for (int i = 0; i < mem.Config.Seats; i++)
                {
                    living.Add((byte)i);
                }
            }

            living.RemoveAll(seat => seat != self && mem.IsKnownOut(seat));

            var scores = new Dictionary<byte, float>();
            var heat = new Dictionary<byte, float>();
            foreach (byte seat in living)
            {
                scores[seat] = suspicion.Score(seat);
                heat[seat] = heatOf != null ? heatOf(seat) : Math.Min(1f, mem.HeatOf(seat));
            }

            var zones = new List<string>();
            foreach (Sim.World.ZoneDef z in mem.Map.Zones)
            {
                zones.Add(z.Id);
            }

            return new MeetingState
            {
                Self = self,
                Role = view.Me.Role,
                Partners = new List<byte>(view.Me.Partners),
                Living = living,
                Stage = meeting.Stage,
                StageLeft = meeting.EndsIn,
                Clues = meeting.Clues,
                Chat = new List<ChatAct>(log.Acts),
                Suspicion = scores,
                Heat = heat,
                Evidence = BuildEvidence(view, mem, self),
                Zones = zones,
                Seats = mem.Config.Seats,
                Polluters = mem.Config.Polluters,
            };
        }

        private static List<EvidenceItem> BuildEvidence(SeatView view, BotMemory mem, byte self)
        {
            var items = new List<EvidenceItem>();

            foreach (WitnessRecord r in mem.Witnessed)
            {
                if (r.Actor != self)
                {
                    items.Add(new EvidenceItem(r.Act == ActKind.Dump ? EvidenceItem.SawDump : EvidenceItem.SawHit, r.Actor, r.ZoneId, r.At));
                }
            }

            if (view.Meeting != null)
            {
                foreach (ClueInfo clue in view.Meeting.Clues)
                {
                    foreach (byte seat in mem.SeenSeats)
                    {
                        if (seat == self || mem.IsKnownOut(seat))
                        {
                            continue;
                        }

                        foreach (Sighting s in mem.SightingsOf(seat))
                        {
                            if (s.Zone == clue.ZoneId && Math.Abs(s.Time - clue.Time) <= SuspicionModel.NearPileWindowS)
                            {
                                items.Add(new EvidenceItem(EvidenceItem.NearPile, seat, s.Zone, s.Time));
                                break;
                            }
                        }
                    }
                }
            }

            var seen = new HashSet<string>();
            IReadOnlyList<Sighting> own = mem.OwnSightings;

            // first the own zone nearest the latest clue: the zone an alibi must name (MeetingState.AlibiZone)
            if (view.Meeting != null && view.Meeting.Clues.Count > 0 && own.Count > 0)
            {
                float clueAt = float.MinValue;
                foreach (ClueInfo clue in view.Meeting.Clues)
                {
                    clueAt = Math.Max(clueAt, clue.Time);
                }

                Sighting nearest = own[0];
                foreach (Sighting o in own)
                {
                    if (Math.Abs(o.Time - clueAt) < Math.Abs(nearest.Time - clueAt))
                    {
                        nearest = o;
                    }
                }

                if (nearest.Zone.Length > 0 && seen.Add(nearest.Zone))
                {
                    items.Add(new EvidenceItem(EvidenceItem.MyZone, null, nearest.Zone, nearest.Time));
                }
            }

            for (int i = own.Count - 1; i >= 0 && seen.Count < MaxOwnZones; i--)
            {
                if (own[i].Zone.Length > 0 && seen.Add(own[i].Zone))
                {
                    items.Add(new EvidenceItem(EvidenceItem.MyZone, null, own[i].Zone, own[i].Time));
                }
            }

            return items;
        }
    }
}
