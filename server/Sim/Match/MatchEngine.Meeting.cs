using System.Collections.Generic;
using Sim.Core;
using Sim.Uncycled;

namespace Sim.Match
{
    public sealed partial class MatchEngine
    {
        /// <summary>
        /// Meeting start (04 section 1): cancel cleaning, task sessions and smog repair, clear the
        /// fire, report every fresh pile and publish its clue, publish the task bar.
        /// </summary>
        private void StartMeeting(PlayerState caller, MeetingCause cause, int? reportedPileId)
        {
            foreach (CleanJob job in _cleanJobs)
            {
                Emit(new CleanCancelledEvent(job.Seat, job.PileId));
            }

            _cleanJobs.Clear();
            _sessions.Clear();
            if (Smog.RepairSeat != null)
            {
                Smog.CancelRepair();
                _smogRepairConsole = null;
            }

            if (Fire.Active)
            {
                EndSabotage(SabotageKind.Fire, SabotageEndResult.Cleared);
            }

            var clues = new List<ClueInfo>();
            foreach (Pile pile in Piles.ReportAllFresh())
            {
                clues.Add(pile.ToClue());
            }

            AddTimeline(TimelineKind.Meeting, caller.Seat, null, null);
            float taskBar = Tasks.Progress;
            Meeting = new MeetingSession(caller.Seat, cause, reportedPileId, clues, taskBar, ActiveSeats(), Config.DiscussionS);
            Phase = MatchPhase.Meeting;
            ClearSightMemory();
            Emit(new PhaseChangedEvent(MatchPhase.Meeting, MeetingStage.Discussion));
            Emit(new MeetingStartedEvent(caller.Seat, cause, reportedPileId, clues, taskBar, Config.DiscussionS, Meeting.Voters));
        }

        private void TickMeeting(float dt)
        {
            MeetingSession? m = Meeting;
            if (m == null)
            {
                AfterMeeting();
                return;
            }

            m.StageTimeLeft -= dt;
            switch (m.Stage)
            {
                case MeetingStage.Discussion:
                    if (m.StageTimeLeft <= 0f)
                    {
                        m.BeginVoting(Config.VotingS);
                        Emit(new PhaseChangedEvent(MatchPhase.Meeting, MeetingStage.Voting));
                        Emit(new VotingStartedEvent(Config.VotingS));
                    }

                    break;

                case MeetingStage.Voting:
                    if (m.StageTimeLeft <= 0f || m.AllVoted)
                    {
                        MeetingResult r = m.Close(Config.MissedVoteIsSkip, Config.ResultsS);
                        Emit(new PhaseChangedEvent(MatchPhase.Meeting, MeetingStage.Results));
                        Emit(new MeetingResultEvent(r.Votes, r.Ejected, r.Tie));
                    }

                    break;

                case MeetingStage.Results:
                    if (m.StageTimeLeft <= 0f)
                    {
                        FinishMeeting(m);
                    }

                    break;
            }
        }

        /// <summary>After the results screen: eject (Ejection phase) or go straight to the win check.</summary>
        private void FinishMeeting(MeetingSession m)
        {
            Meeting = null;
            byte? ejected = m.Result?.Ejected;
            if (ejected != null)
            {
                PlayerState? ep = Find(ejected.Value);
                if (ep != null && ep.IsActive)
                {
                    AddTimeline(TimelineKind.Ejection, ep.Seat, null, null);
                    MakeOut(ep, OutCause.Vote);
                    Emit(new EjectionEvent(ep.Seat, Config.ConfirmEject ? ep.IsPolluter : (bool?)null));
                    Phase = MatchPhase.Ejection;
                    _phaseTimer = Config.EjectionS;
                    Emit(new PhaseChangedEvent(MatchPhase.Ejection, null));
                    return;
                }
            }

            AfterMeeting();
        }

        /// <summary>Win check after a meeting (never mid-meeting), then back to Playing.</summary>
        private void AfterMeeting()
        {
            if (TryEndMatch())
            {
                return;
            }

            ResumeRound();
        }

        /// <summary>
        /// Meeting end (04 section 1): cooldowns reset, everyone to spawns, body piles removed,
        /// dump piles stay.
        /// </summary>
        private void ResumeRound()
        {
            Phase = MatchPhase.Playing;
            Meeting = null;
            SinceMeeting = 0f;
            _phaseTimer = 0f;
            Piles.RemoveReportedBodies();
            for (int i = 0; i < _players.Count; i++)
            {
                PlayerState p = _players[i];
                p.Position = Map.SpawnFor(i);
                p.MoveBudget = Config.MoveBudgetCap;
                p.HitReadyAt = ActiveTime + Config.HitCooldownS;
                p.DumpReadyAt = ActiveTime + Config.DumpCooldownS;
            }

            ClearSightMemory();
            Emit(new PhaseChangedEvent(MatchPhase.Playing, null));
            foreach (PlayerState p in _players)
            {
                Emit(new RoundResumedEvent(p.Seat, p.Position));
            }

            UpdateSightMemory();
        }
    }
}
