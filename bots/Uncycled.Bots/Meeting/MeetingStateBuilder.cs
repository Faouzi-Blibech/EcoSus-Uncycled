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

        /// <param name="seats">The seat ids of the room, for a meeting view that does not list the living (null: the seats this bot knows of).</param>
        /// <param name="heatOf">Heat of a seat in [0, 1]; null uses the Round 5 heat of the memory (BotMemory.HeatOf, clamped to 1), which counts this meeting's chat accusations.</param>
        public static MeetingState Build(SeatView view, BotMemory mem, MeetingLog log, SuspicionModel suspicion, Func<byte, float>? heatOf, IReadOnlyList<byte>? seats = null)
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
            else if (seats != null && seats.Count > 0)
            {
                living.AddRange(seats);
            }
            else
            {
                // no list: the seats this bot knows of (never invented ids 0..Seats-1: a seat id may be above the room size)
                living.Add(self);
                var known = new HashSet<byte>(mem.SeenSeats) { meeting.CalledBy };
                known.UnionWith(meeting.Voted);
                known.Remove(self);
                var rest = new List<byte>(known);
                rest.Sort();
                living.AddRange(rest);
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
                CalledBy = meeting.CalledBy,
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
            var nearPiles = new HashSet<(byte, string, float)>();

            foreach (WitnessRecord r in mem.Witnessed)
            {
                if (r.Actor != self)
                {
                    items.Add(new EvidenceItem(r.Act == ActKind.Hit ? EvidenceItem.SawHit : EvidenceItem.SawDump, r.Actor, r.ZoneId, r.At));
                }
            }

            // an alarm that went up in this bot's sight (the machine jam, a gas warning) and the seats it saw close by then
            foreach (AlarmSighting a in mem.RecentAlarms(view))
            {
                items.Add(new EvidenceItem(a.Kind == AlarmSighting.Jam ? EvidenceItem.SawJam : EvidenceItem.SawGas, null, a.Zone, a.Time));
                foreach (byte seat in a.Near)
                {
                    if (seat != self && !mem.IsKnownOut(seat))
                    {
                        items.Add(new EvidenceItem(EvidenceItem.SawNear, seat, a.Zone, a.Time));
                    }
                }
            }

            // A fake clean (mem.Inferred) is a tell this bot worked out, not an act it saw: it is no evidence item.
            // It counts in the suspicion score (SuspicionModel), and when the bot accuses on it the line is the
            // vague "sus", never a saw_dump line with a zone (which every listener would count as an eyewitness).

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
                                if (!nearPiles.Add((seat, s.Zone, s.Time)))
                                {
                                    break; // the same sighting fits another pile of the zone: one item, not one per pile
                                }

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
