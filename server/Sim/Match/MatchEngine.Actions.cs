using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Uncycled;
using Sim.World;

namespace Sim.Match
{
    public sealed partial class MatchEngine
    {
        private static CommandResult Reject(RejectReason reason) => CommandResult.Rejected(reason);

        // ---------- move (08 section 2) ----------

        private CommandResult ApplyMove(PlayerState p, MoveCommand c)
        {
            if (Phase != MatchPhase.Playing)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive)
            {
                return Reject(RejectReason.NotAllowed);
            }

            if (c.Seq <= p.LastMoveSeq)
            {
                // Stale or duplicate packet: dropped without a correction.
                return Reject(RejectReason.NotAllowed);
            }

            if (!c.Position.IsFinite)
            {
                return Reject(RejectReason.TargetInvalid);
            }

            float distance = Vec2.Distance(p.Position, c.Position);
            if (distance > p.MoveBudget + Epsilon)
            {
                Emit(new CorrectionEvent(p.Seat, p.Position));
                return Reject(RejectReason.NotAllowed);
            }

            if (!Map.Walk.SegmentClear(p.Position, c.Position))
            {
                Emit(new CorrectionEvent(p.Seat, p.Position));
                return Reject(RejectReason.WallBetween);
            }

            p.Position = c.Position;
            p.MoveBudget = Math.Max(0f, p.MoveBudget - distance);
            p.LastMoveSeq = c.Seq;
            return CommandResult.Ok;
        }

        // ---------- use / task_step ----------

        private CommandResult ApplyUse(PlayerState p, UseCommand c)
        {
            if (Phase != MatchPhase.Playing)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive)
            {
                return Reject(RejectReason.NotAllowed);
            }

            ConsoleDef? console = Map.FindConsole(c.ConsoleId);
            if (console == null)
            {
                return Reject(RejectReason.TargetInvalid);
            }

            RejectReason? reach = CheckReach(p.Position, console.Position, console.Range);
            if (reach != null)
            {
                return Reject(reach.Value);
            }

            if (console.Kind != ConsoleKind.Task)
            {
                Emit(new UseOkEvent(p.Seat, console.Id, console.Kind, null, null, null));
                return CommandResult.Ok;
            }

            if (!p.IsCitizen)
            {
                // Polluters have no real tasks (they fake them client-side).
                return Reject(RejectReason.NotAllowed);
            }

            TaskState? task = null;
            foreach (TaskState t in Tasks.All)
            {
                if (t.Owner == p.Seat && !t.IsDone && t.CurrentConsoleId == console.Id)
                {
                    task = t;
                    break;
                }
            }

            if (task == null)
            {
                return Reject(RejectReason.NotAllowed);
            }

            _sessions[p.Seat] = new TaskSession(task.Id, task.StepsDone, console.Id, ActiveTime);
            Emit(new UseOkEvent(p.Seat, console.Id, ConsoleKind.Task, task.Id, task.Type.Id, task.StepsDone));
            return CommandResult.Ok;
        }

        private CommandResult ApplyTaskStep(PlayerState p, TaskStepCommand c)
        {
            if (Phase != MatchPhase.Playing)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive || !p.IsCitizen)
            {
                return Reject(RejectReason.NotAllowed);
            }

            TaskSession? session;
            if (!_sessions.TryGetValue(p.Seat, out session) || session == null || session.TaskId != c.TaskId)
            {
                return Reject(RejectReason.NotAllowed);
            }

            TaskState? task = Tasks.Find(c.TaskId);
            if (task == null || task.Owner != p.Seat || task.IsDone
                || task.StepsDone != session.Step || task.CurrentConsoleId != session.ConsoleId)
            {
                _sessions.Remove(p.Seat);
                return Reject(RejectReason.NotAllowed);
            }

            ConsoleDef? console = Map.FindConsole(session.ConsoleId);
            if (console == null)
            {
                _sessions.Remove(p.Seat);
                return Reject(RejectReason.TargetInvalid);
            }

            RejectReason? reach = CheckReach(p.Position, console.Position, console.Range);
            if (reach != null)
            {
                _sessions.Remove(p.Seat);
                return Reject(reach.Value);
            }

            if (ActiveTime - session.OpenedAt + Epsilon < task.Type.MinSeconds)
            {
                return Reject(RejectReason.OnCooldown);
            }

            Tasks.CompleteStep(task);
            _sessions.Remove(p.Seat);
            Emit(new TaskProgressEvent(p.Seat, task.Id, task.StepsDone));
            TryEndMatch();
            return CommandResult.Ok;
        }

        // ---------- hit / dump ----------

        private CommandResult ApplyHit(PlayerState p, HitCommand c)
        {
            if (Phase != MatchPhase.Playing)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive || !p.IsPolluter)
            {
                return Reject(RejectReason.NotAllowed);
            }

            if (ActiveTime + Epsilon < p.HitReadyAt)
            {
                return Reject(RejectReason.OnCooldown);
            }

            PlayerState? target = Find(c.Target);
            if (target == null || target == p || !target.IsActive || !target.IsCitizen)
            {
                return Reject(RejectReason.TargetInvalid);
            }

            if (PoisonRule.IsImmune(target, ActiveTime))
            {
                return Reject(RejectReason.TargetImmune);
            }

            RejectReason? reach = CheckReach(p.Position, target.Position, Config.HitRange);
            if (reach != null)
            {
                return Reject(reach.Value);
            }

            List<byte> witnesses = WitnessesOf(p);
            string zone = Map.ZoneAt(target.Position);
            Vec2 victimPos = target.Position;
            foreach (byte w in witnesses)
            {
                Witnesses.Add(new WitnessRecord(ActKind.Hit, p.Seat, w, MatchTime, zone, p.Position, null, target.Seat));
            }

            p.HitReadyAt = ActiveTime + Config.HitCooldownS;
            bool isOut = PoisonRule.Apply(target, Config.PoisonPerHit, Config.VictimImmunityS, ActiveTime);
            AddTimeline(TimelineKind.Hit, p.Seat, target.Seat, zone);

            if (witnesses.Count > 0)
            {
                Emit(new CueEvent(witnesses, CueKind.Hit, p.Seat, victimPos));
            }

            Emit(new HitOkEvent(p.Seat, Config.HitCooldownS));
            Emit(new PoisonedEvent(target.Seat, target.Poison));
            if (isOut)
            {
                MakeOut(target, OutCause.Poison);
            }

            TryEndMatch();
            return CommandResult.Ok;
        }

        private CommandResult ApplyDump(PlayerState p)
        {
            if (Phase != MatchPhase.Playing)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive || !p.IsPolluter || p.Signature == null)
            {
                return Reject(RejectReason.NotAllowed);
            }

            if (ActiveTime + Epsilon < p.DumpReadyAt)
            {
                return Reject(RejectReason.OnCooldown);
            }

            List<byte> witnesses = WitnessesOf(p);
            string zone = Map.ZoneAt(p.Position);
            Pile pile = Piles.AddDump(p.Position, zone, MatchTime, p.Signature.Value, p.Seat);
            foreach (byte w in witnesses)
            {
                Witnesses.Add(new WitnessRecord(ActKind.Dump, p.Seat, w, MatchTime, zone, p.Position, pile.Id, null));
            }

            p.DumpReadyAt = ActiveTime + Config.DumpCooldownS;
            Meter.Add(Config.DumpMeterAdd);
            AddTimeline(TimelineKind.Dump, p.Seat, null, zone);

            if (witnesses.Count > 0)
            {
                Emit(new CueEvent(witnesses, CueKind.Dump, p.Seat, pile.Position));
            }

            Emit(new DumpOkEvent(p.Seat, pile.Id, Config.DumpCooldownS));
            TryEndMatch();
            return CommandResult.Ok;
        }

        // ---------- cleaning ----------

        private CommandResult ApplyCleanStart(PlayerState p, CleanStartCommand c)
        {
            if (Phase != MatchPhase.Playing)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive || !p.IsCitizen)
            {
                return Reject(RejectReason.NotAllowed);
            }

            Pile? pile = Piles.Find(c.PileId);
            if (pile == null || !pile.IsCleanable)
            {
                return Reject(RejectReason.TargetInvalid);
            }

            CleanJob? existing = FindCleanJob(pile.Id);
            if (existing != null)
            {
                return existing.Seat == p.Seat ? CommandResult.Ok : Reject(RejectReason.AlreadyCleaning);
            }

            RejectReason? reach = CheckReach(p.Position, pile.Position, Config.CleanRange);
            if (reach != null)
            {
                return Reject(reach.Value);
            }

            CancelCleaningBy(p.Seat);
            _cleanJobs.Add(new CleanJob(pile.Id, p.Seat));
            Emit(new CleanOkEvent(p.Seat, pile.Id, Config.CleanS));
            return CommandResult.Ok;
        }

        private CommandResult ApplyCleanCancel(PlayerState p, CleanCancelCommand c)
        {
            CleanJob? job = FindCleanJob(c.PileId);
            if (job == null || job.Seat != p.Seat)
            {
                return Reject(RejectReason.TargetInvalid);
            }

            _cleanJobs.Remove(job);
            Emit(new CleanCancelledEvent(p.Seat, job.PileId));
            return CommandResult.Ok;
        }

        private CleanJob? FindCleanJob(int pileId)
        {
            foreach (CleanJob j in _cleanJobs)
            {
                if (j.PileId == pileId)
                {
                    return j;
                }
            }

            return null;
        }

        private void CancelCleaningBy(byte seat)
        {
            for (int i = _cleanJobs.Count - 1; i >= 0; i--)
            {
                CleanJob job = _cleanJobs[i];
                if (job.Seat == seat)
                {
                    _cleanJobs.RemoveAt(i);
                    Emit(new CleanCancelledEvent(seat, job.PileId));
                }
            }
        }

        /// <summary>Server-timed cleaning: revalidated every tick, completes after CleanS.</summary>
        private void TickCleaning(float dt)
        {
            if (_cleanJobs.Count == 0)
            {
                return;
            }

            CleanJob[] jobs = _cleanJobs.ToArray();
            foreach (CleanJob job in jobs)
            {
                PlayerState? p = Find(job.Seat);
                Pile? pile = Piles.Find(job.PileId);
                if (p == null || !p.IsActive || pile == null || !pile.IsCleanable
                    || CheckReach(p.Position, pile.Position, Config.CleanRange) != null)
                {
                    _cleanJobs.Remove(job);
                    Emit(new CleanCancelledEvent(job.Seat, job.PileId));
                    continue;
                }

                job.Elapsed += dt;
                if (job.Elapsed + Epsilon >= Config.CleanS)
                {
                    _cleanJobs.Remove(job);
                    Piles.MarkCleaned(pile);
                    Meter.Add(-Config.CleanMeterSub);
                    Emit(new CleanDoneEvent(job.Seat, pile.Id));
                }
            }
        }

        // ---------- report / emergency ----------

        private CommandResult ApplyReport(PlayerState p, ReportCommand c)
        {
            if (Phase != MatchPhase.Playing)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive)
            {
                return Reject(RejectReason.NotAllowed);
            }

            Pile? pile = Piles.Find(c.PileId);
            if (pile == null)
            {
                return Reject(RejectReason.TargetInvalid);
            }

            if (pile.State == PileState.Reported)
            {
                return Reject(RejectReason.AlreadyReported);
            }

            if (pile.State != PileState.Fresh)
            {
                return Reject(RejectReason.TargetInvalid);
            }

            RejectReason? reach = CheckReach(p.Position, pile.Position, Config.ReportRange);
            if (reach != null)
            {
                return Reject(reach.Value);
            }

            AddTimeline(TimelineKind.Report, p.Seat, null, pile.ZoneId);
            StartMeeting(p, MeetingCause.Report, pile.Id);
            return CommandResult.Ok;
        }

        private CommandResult ApplyCallMeeting(PlayerState p)
        {
            if (Phase != MatchPhase.Playing)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive)
            {
                return Reject(RejectReason.NotAllowed);
            }

            if (Fire.Active)
            {
                return Reject(RejectReason.FireActive);
            }

            if (EmergenciesLeft <= 0)
            {
                return Reject(RejectReason.NoEmergenciesLeft);
            }

            if (SinceMeeting + Epsilon < Config.EmergencyMinSinceMeetingS)
            {
                return Reject(RejectReason.OnCooldown);
            }

            if (_emergencyConsoles.Count == 0)
            {
                return Reject(RejectReason.NotAllowed);
            }

            RejectReason? best = RejectReason.OutOfRange;
            foreach (ConsoleDef console in _emergencyConsoles)
            {
                RejectReason? reach = CheckReach(p.Position, console.Position, console.Range);
                if (reach == null)
                {
                    best = null;
                    break;
                }

                if (reach == RejectReason.WallBetween)
                {
                    best = RejectReason.WallBetween;
                }
            }

            if (best != null)
            {
                return Reject(best.Value);
            }

            EmergenciesLeft--;
            StartMeeting(p, MeetingCause.Emergency, null);
            return CommandResult.Ok;
        }

        // ---------- meeting commands ----------

        private CommandResult ApplyVote(PlayerState p, VoteCommand c)
        {
            MeetingSession? m = Meeting;
            if (Phase != MatchPhase.Meeting || m == null || m.Stage != MeetingStage.Voting)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive || !m.IsVoter(p.Seat) || m.HasVoted(p.Seat))
            {
                return Reject(RejectReason.NotAllowed);
            }

            if (c.Target != null)
            {
                PlayerState? target = Find(c.Target.Value);
                if (target == null || !target.IsActive)
                {
                    return Reject(RejectReason.TargetInvalid);
                }
            }

            m.RecordVote(p.Seat, c.Target);
            Emit(new VoteCastEvent(p.Seat));
            return CommandResult.Ok;
        }

        private CommandResult ApplyChat(PlayerState p, ChatPhraseCommand c)
        {
            MeetingSession? m = Meeting;
            if (Phase != MatchPhase.Meeting || m == null || m.Stage != MeetingStage.Discussion)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive)
            {
                return Reject(RejectReason.NotAllowed);
            }

            if (string.IsNullOrEmpty(c.PhraseId))
            {
                return Reject(RejectReason.TargetInvalid);
            }

            PhraseInfo? phrase = null;
            if (_phrases != null && !_phrases.TryGet(c.PhraseId, out phrase))
            {
                return Reject(RejectReason.TargetInvalid);
            }

            if (c.Args.Count > Config.PhraseMaxArgs || (phrase != null && c.Args.Count != phrase.Slots.Count))
            {
                return Reject(RejectReason.TargetInvalid);
            }

            for (int i = 0; i < c.Args.Count; i++)
            {
                ChatArg arg = c.Args[i];
                if (phrase != null && arg.Kind != phrase.Slots[i])
                {
                    return Reject(RejectReason.TargetInvalid);
                }

                bool valid;
                if (arg.Kind == ChatArgKind.Seat)
                {
                    PlayerState? named = Find(arg.Seat);
                    valid = named != null && (_phrases == null || named.IsActive);
                }
                else
                {
                    valid = arg.Zone != null && Map.HasZone(arg.Zone);
                }

                if (!valid)
                {
                    return Reject(RejectReason.TargetInvalid);
                }
            }

            if (m.ChatCount(p.Seat) >= Config.PhrasesPerMeeting)
            {
                return Reject(RejectReason.NotAllowed);
            }

            m.CountChat(p.Seat);
            Emit(new ChatEvent(p.Seat, c.PhraseId, new List<ChatArg>(c.Args)));
            return CommandResult.Ok;
        }

        // ---------- sabotage ----------

        private CommandResult ApplySabotage(PlayerState p, SabotageCommand c)
        {
            if (Phase != MatchPhase.Playing)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive || !p.IsPolluter)
            {
                return Reject(RejectReason.NotAllowed);
            }

            if (Fire.Active || Smog.Active || ActiveTime + Epsilon < SabotageTimer.ReadyAt)
            {
                return Reject(RejectReason.OnCooldown);
            }

            if (c.Kind == SabotageKind.Fire)
            {
                if (!Fire.Available)
                {
                    return Reject(RejectReason.NotAllowed);
                }

                Fire.Start(Config.FireCountdownS);
                SabotageTimer.OnStarted();
                Emit(new SabotageStartedEvent(SabotageKind.Fire, Config.FireCountdownS));
                return CommandResult.Ok;
            }

            if (!Smog.Available)
            {
                return Reject(RejectReason.NotAllowed);
            }

            Smog.Start();
            _smogRepairConsole = null;
            SabotageTimer.OnStarted();
            Emit(new SabotageStartedEvent(SabotageKind.Smog, null));
            return CommandResult.Ok;
        }

        private CommandResult ApplyFix(PlayerState p, FixCommand c)
        {
            if (Phase != MatchPhase.Playing)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive || !p.IsCitizen)
            {
                return Reject(RejectReason.NotAllowed);
            }

            ConsoleDef? console = Map.FindConsole(c.ConsoleId);
            if (console == null)
            {
                return Reject(RejectReason.TargetInvalid);
            }

            if (console.Kind == ConsoleKind.FireFix)
            {
                int index = Fire.IndexOf(console.Id);
                if (!c.Holding)
                {
                    if (Fire.Active && index >= 0 && Fire.HolderAt(index) == p.Seat)
                    {
                        Fire.ReleaseAt(index);
                    }

                    return CommandResult.Ok;
                }

                if (!Fire.Active || index < 0)
                {
                    return Reject(RejectReason.NotAllowed);
                }

                RejectReason? reach = CheckReach(p.Position, console.Position, console.Range);
                if (reach != null)
                {
                    return Reject(reach.Value);
                }

                return Fire.TryHold(p.Seat, index) ? CommandResult.Ok : Reject(RejectReason.NotAllowed);
            }

            if (console.Kind == ConsoleKind.SmogFix)
            {
                if (!Smog.Active)
                {
                    return Reject(RejectReason.NotAllowed);
                }

                RejectReason? reach = CheckReach(p.Position, console.Position, console.Range);
                if (reach != null)
                {
                    return Reject(reach.Value);
                }

                if (Smog.RepairSeat == null)
                {
                    Smog.BeginRepair(p.Seat);
                    _smogRepairConsole = console;
                    return CommandResult.Ok;
                }

                return Smog.RepairSeat == p.Seat ? CommandResult.Ok : Reject(RejectReason.NotAllowed);
            }

            return Reject(RejectReason.TargetInvalid);
        }

        /// <summary>Fire holders are revalidated every tick; all held = fixed; countdown end = +meter.</summary>
        private void TickFire(float dt)
        {
            if (!Fire.Active)
            {
                return;
            }

            for (int i = 0; i < _fireConsoles.Count; i++)
            {
                byte? holder = Fire.HolderAt(i);
                if (holder == null)
                {
                    continue;
                }

                PlayerState? hp = Find(holder.Value);
                ConsoleDef console = _fireConsoles[i];
                if (hp == null || !hp.IsActive || !hp.IsCitizen
                    || CheckReach(hp.Position, console.Position, console.Range) != null)
                {
                    Fire.ReleaseAt(i);
                }
            }

            if (Fire.AllHeld)
            {
                EndSabotage(SabotageKind.Fire, SabotageEndResult.Fixed);
                return;
            }

            Fire.Countdown -= dt;
            if (Fire.Countdown <= 0f)
            {
                EndSabotage(SabotageKind.Fire, SabotageEndResult.Expired);
                Meter.Add(Config.FireExpiredAdd);
            }
        }

        private void TickSmogRepair(float dt)
        {
            if (!Smog.Active || Smog.RepairSeat == null)
            {
                return;
            }

            PlayerState? rp = Find(Smog.RepairSeat.Value);
            ConsoleDef? console = _smogRepairConsole;
            if (rp == null || !rp.IsActive || console == null
                || CheckReach(rp.Position, console.Position, console.Range) != null)
            {
                Smog.CancelRepair();
                _smogRepairConsole = null;
                return;
            }

            Smog.AdvanceRepair(dt);
            if (Smog.RepairElapsed + Epsilon >= Config.SmogFixS)
            {
                EndSabotage(SabotageKind.Smog, SabotageEndResult.Fixed);
            }
        }

        private void EndSabotage(SabotageKind kind, SabotageEndResult result)
        {
            if (kind == SabotageKind.Fire)
            {
                Fire.Stop();
            }
            else
            {
                Smog.Clear();
                _smogRepairConsole = null;
            }

            SabotageTimer.OnEnded(ActiveTime);
            Emit(new SabotageEndedEvent(kind, result));
        }
    }
}
