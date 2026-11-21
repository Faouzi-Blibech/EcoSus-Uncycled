using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Uncycled;

namespace Sim.Match
{
    public sealed partial class MatchEngine
    {
        /// <summary>
        /// The wire-only numbers for one seat: the smog level (it outlives the sabotage while vision
        /// eases back) and, for the seat that is repairing the smog panel, how long it has repaired.
        /// </summary>
        public SnapshotExtras ExtrasFor(byte seat)
        {
            float? repair = Smog.Active && Smog.RepairSeat == seat ? Smog.RepairElapsed : (float?)null;
            float? machineRepair = Machine.Jammed && Machine.RepairSeat == seat ? Machine.RepairElapsed : (float?)null;
            return new SnapshotExtras(Smog.Level, repair, Machine.Jammed, machineRepair);
        }

        /// <summary>
        /// Everything one seat may know (08 section 1). Others' roles and poison never appear;
        /// Out seats get no players and no piles until game over.
        /// </summary>
        public SeatView ViewFor(byte seat)
        {
            int mi = IndexOf(seat);
            if (mi < 0)
            {
                throw new ArgumentException("Unknown seat " + seat, nameof(seat));
            }

            PlayerState me = _players[mi];
            int n = _players.Count;
            var players = new List<PlayerView>();
            var piles = new List<PileView>();

            if (Phase == MatchPhase.Ended)
            {
                foreach (PlayerState q in _players)
                {
                    if (q != me)
                    {
                        players.Add(new PlayerView(q.Seat, q.Position, q.Color, !q.IsActive));
                    }
                }

                foreach (Pile pile in Piles.All)
                {
                    if (pile.IsPresent)
                    {
                        piles.Add(new PileView(pile.Id, pile.Position, pile.Kind, pile.State));
                    }
                }
            }
            else if (me.IsActive && (Phase == MatchPhase.Playing || Phase == MatchPhase.Intro))
            {
                for (int j = 0; j < n; j++)
                {
                    PlayerState q = _players[j];
                    if (j == mi || !q.IsActive)
                    {
                        continue;
                    }

                    if (CanSee(me, q.Position))
                    {
                        players.Add(new PlayerView(q.Seat, q.Position, q.Color, false));
                        continue;
                    }

                    float seenAt = _lastSeenAt[mi * n + j];
                    if (Phase == MatchPhase.Playing && MatchTime - seenAt <= Config.LostSightGraceS + Epsilon)
                    {
                        players.Add(new PlayerView(q.Seat, _lastSeenPos[mi * n + j], q.Color, false));
                    }
                }

                foreach (Pile pile in Piles.All)
                {
                    if (pile.IsPresent && CanSee(me, pile.Position))
                    {
                        piles.Add(new PileView(pile.Id, pile.Position, pile.Kind, pile.State));
                    }
                }
            }

            var tasks = new List<TaskInfo>();
            foreach (TaskState t in Tasks.OwnedBy(seat))
            {
                tasks.Add(TaskInfo.From(t));
            }

            CooldownView? cooldowns = null;
            if (me.IsPolluter)
            {
                cooldowns = new CooldownView(
                    Math.Max(0f, me.HitReadyAt - ActiveTime),
                    Math.Max(0f, me.DumpReadyAt - ActiveTime),
                    SabotageTimer.Remaining(ActiveTime),
                    Math.Max(0f, me.RepolluteReadyAt - ActiveTime),
                    HasPlantedVirus(me.Seat) ? Config.VirusCooldownS : Math.Max(0f, me.VirusReadyAt - ActiveTime));
            }

            var myView = new MyView(
                me.Seat,
                me.Role,
                PartnersOf(me),
                me.IsPolluter ? me.Signature : null,
                me.Status,
                me.Position,
                VisionOf(me),
                me.IsCitizen ? me.Poison : (int?)null,
                cooldowns,
                tasks,
                me.Bags,
                me.BagCap,
                me.HasTool,
                me.Repollutes,
                me.Ability,
                me.Heals,
                me.Masks,
                me.InfectedDeathAt == null ? (float?)null : Math.Max(0f, me.InfectedDeathAt.Value - ActiveTime));

            var spots = new List<SpotView>();
            foreach (Spot spot in Spots.All)
            {
                spots.Add(new SpotView(spot.Id, spot.State));
            }

            SabotageView? sabotage = null;
            if (Fire.Active)
            {
                sabotage = new SabotageView(SabotageKind.Fire, Fire.Countdown, Fire.HeldConsoleIds(), 0f);
            }
            else if (Smog.Active)
            {
                sabotage = new SabotageView(SabotageKind.Smog, null, Array.Empty<string>(), Smog.Level);
            }

            MeetingView? meetingView = null;
            MeetingSession? m = Meeting;
            if (m != null)
            {
                byte? myVote;
                bool voted = m.TryGetVote(seat, out myVote);
                meetingView = new MeetingView(
                    m.Stage,
                    Math.Max(0f, m.StageTimeLeft),
                    m.CalledBy,
                    m.Cause,
                    m.Clues,
                    new List<byte>(m.VotedSeats),
                    voted,
                    voted ? myVote : null,
                    m.TaskBar,
                    m.ChatCount(seat),
                    new List<byte>(m.Voters),
                    m.TextCount(seat));
            }

            float phaseLeft = 0f;
            if (Phase == MatchPhase.Intro || Phase == MatchPhase.Ejection)
            {
                phaseLeft = Math.Max(0f, _phaseTimer);
            }
            else if (m != null)
            {
                phaseLeft = Math.Max(0f, m.StageTimeLeft);
            }

            float clockValue = Config.DeadlineClock == DeadlineClock.MatchTime ? MatchTime : ActiveTime;

            return new SeatView(
                seat,
                Phase,
                phaseLeft,
                MatchTime,
                Config.DeadlineS,
                Config.DeadlineClock,
                clockValue,
                Meter.Value,
                EmergenciesLeft,
                myView,
                players,
                piles,
                sabotage,
                meetingView,
                Result,
                me.IsActive ? null : new List<ChatEvent>(_ghostChat),
                spots);
        }
    }
}
