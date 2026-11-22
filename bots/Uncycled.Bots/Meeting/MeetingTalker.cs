using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core;
using Sim.Match;

namespace Uncycled.Bots.Meeting
{
    /// <summary>
    /// One bot's meeting behaviour: paces its lines like a person, asks a decider what to say and vote,
    /// grounds the answer and sends a ChatPhraseCommand or VoteCommand. Call Step every tick (in any
    /// phase: outside a meeting it only resets). It never awaits: a decision from the primary decider
    /// is started shortly before the planned moment and used only if it has finished by then, else
    /// the fallback (the synchronous rule decider) answers at the planned time and the late task is
    /// cancelled. One instance per bot seat; the floor is shared by the room.
    /// </summary>
    public sealed class MeetingTalker
    {
        public const int MaxLines = 3;
        public const float FirstMinS = 4f;
        public const float FirstMaxS = 12f;
        public const float GapMinS = 6f;
        public const float GapMaxS = 15f;
        public const float ReplyMinS = 3f;
        public const float ReplyMaxS = 8f;
        public const float VoteMinS = 3f;
        public const float VoteMaxS = 20f;

        /// <summary>Default for LeadS: the model call timeout.</summary>
        public const float DefaultLeadS = 3f;

        private const float VoteSafetyS = 1.5f;

        private readonly IMeetingDecider _primary;
        private readonly IMeetingDecider _fallback;
        private readonly MeetingFloor _floor;
        private readonly Random _rng;
        private readonly SuspicionModel _suspicion = new SuspicionModel();

        private bool _inMeeting;
        private MeetingStage? _stage;
        private int _lines;
        private int _seen;
        private int _seenGen;
        private float? _nextAt;
        private float? _replyAt;
        private float? _voteAt;
        private bool _voteDone;
        private bool _announced;
        private byte? _announcedVote;
        private MeetingDecision? _ready;
        private MeetingState? _readyState;
        private Task<MeetingDecision>? _task;
        private CancellationTokenSource? _cts;

        public MeetingTalker(IMeetingDecider primary, IMeetingDecider fallback, MeetingFloor floor, Random rng)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _floor = floor ?? throw new ArgumentNullException(nameof(floor));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>Heat of a seat in [0, 1] for the state; null uses the Round 5 heat (BotMemory.HeatOf, with this meeting's chat).</summary>
        public Func<byte, float>? HeatOf { get; set; }

        /// <summary>Seconds before the planned moment that the primary decider is started: set it to the model call timeout (bots.json meeting.timeout_s).</summary>
        public float LeadS { get; set; } = DefaultLeadS;

        /// <summary>Gets each decision that is really sent (for the decision log); null logs nothing.</summary>
        public IDecisionSink? Sink { get; set; }

        /// <summary>Room or match id handed to the deciders (for the decision log).</summary>
        public string MatchId { get; set; } = "";

        /// <summary>Source of the last decision that was sent ("rule", "clef", ...), for logs and tests.</summary>
        public string LastSource { get; private set; } = "";

        /// <param name="engine">The room's engine; only the seat's own SeatView (view) is read.</param>
        public void Step(MatchEngine engine, byte seat, SeatView view, BotMemory mem, MeetingLog log, float now, Action<ICommand> send)
        {
            if (_seats == null || _seats.Length != engine.Players.Count)
            {
                // the real seat ids of the room (a human may keep a seat id above the room size after it shrank)
                _seats = new byte[engine.Players.Count];
                for (int i = 0; i < _seats.Length; i++)
                {
                    _seats[i] = engine.Players[i].Seat;
                }
            }

            MeetingView? meeting = view.Meeting;
            if (view.Phase != MatchPhase.Meeting || meeting == null || view.Me.Status != PlayerStatus.Active)
            {
                Reset();
                return;
            }

            if (!_inMeeting)
            {
                Reset();
                _inMeeting = true;
                _seen = log.Acts.Count;
                _seenGen = log.Generation;
                ReportEmergencyCall(meeting, seat, mem);
            }

            if (_stage != meeting.Stage)
            {
                DropTask();
                _ready = null;
                _readyState = null;
                _stage = meeting.Stage;
            }

            if (meeting.Stage == MeetingStage.Discussion)
            {
                StepDiscussion(view, mem, log, now, send);
            }
            else if (meeting.Stage == MeetingStage.Voting)
            {
                StepVoting(view, mem, log, now, send);
            }
        }

        /// <summary>One note per bot emergency call (who, why, where) for the server log; the reason is the one the seat's EmergencyRule rolled, "other" when it has none (a free call when infected).</summary>
        private void ReportEmergencyCall(MeetingView meeting, byte seat, BotMemory mem)
        {
            string trigger = mem.EmergencyTrigger ?? "other";
            string? zone = mem.EmergencyZone;
            if (meeting.CalledBy != seat || meeting.Cause != MeetingCause.Emergency)
            {
                return;
            }

            mem.EmergencyTrigger = null;
            mem.EmergencyZone = null;
            try
            {
                (Sink as IEmergencyCallSink)?.EmergencyCall(MatchId, seat, trigger, zone);
            }
            catch (Exception)
            {
                // a failing log must never break a meeting
            }
        }

        // ---------- discussion ----------

        private void StepDiscussion(SeatView view, BotMemory mem, MeetingLog log, float now, Action<ICommand> send)
        {
            byte self = view.Me.Seat;
            if (_nextAt == null)
            {
                _nextAt = now + Between(FirstMinS, FirstMaxS);
            }

            if (_seenGen != log.Generation)
            {
                _seen = 0; // the host cleared the log (it may be longer again by now): read the new acts from the start
                _seenGen = log.Generation;
            }

            for (; _seen < log.Acts.Count; _seen++)
            {
                ChatAct a = log.Acts[_seen];
                if (a.Kind == "accuse" && a.Seat == self && a.Speaker != self && _lines < MaxLines && _replyAt == null)
                {
                    _replyAt = now + Between(ReplyMinS, ReplyMaxS);

                    // a line that was ready or being decided before the accusation is stale: the reply goes first
                    _ready = null;
                    _readyState = null;
                    DropTask();
                }
            }

            if (_lines >= MaxLines)
            {
                return;
            }

            bool reply = _replyAt != null;
            float due = reply ? _replyAt!.Value : _nextAt.Value;

            if (_task == null && _ready == null && now >= due - LeadS)
            {
                StartPrimary(BuildState(view, mem, log, reply));
            }

            if (now < due)
            {
                return;
            }

            if (_ready == null)
            {
                MeetingState state = BuildState(view, mem, log, reply);
                MeetingDecision? primary = TakePrimary(out string? reason);
                MeetingDecision d;
                if (primary == null || (!primary.Speak && reply))
                {
                    reason = primary == null ? reason : "reply_declined";
                    d = RunFallback(state);
                }
                else
                {
                    d = primary;
                    reason = null;
                }

                d = Ground(d, state, reason);
                if (!d.Speak)
                {
                    if (reply)
                    {
                        _replyAt = null;
                    }
                    else
                    {
                        _nextAt = now + Between(GapMinS, GapMaxS);
                    }

                    return;
                }

                _ready = d;
                _readyState = state;
            }

            if (Grounding.IsRepeat(log.Acts, view.Me.Seat, _ready!.PhraseId!, _ready.Seat, _ready.Zone))
            {
                // another bot said it while this one waited for the floor: decide again on the chat as it is now
                _ready = null;
                _readyState = null;
                return;
            }

            if (!_floor.TryTake(now))
            {
                return;
            }

            MeetingDecision say = _ready!;
            MeetingState sayState = _readyState!;
            _ready = null;
            _readyState = null;
            var args = new List<ChatArg>();
            if (say.Seat != null)
            {
                args.Add(ChatArg.ForSeat(say.Seat.Value));
            }

            if (say.Zone != null)
            {
                args.Add(ChatArg.ForZone(say.Zone));
            }

            send(new ChatPhraseCommand(say.PhraseId!, args));
            LastSource = say.Source;
            Report(sayState, say);
            _lines++;
            if ((say.PhraseId == "vote" || say.PhraseId == "sus" || say.PhraseId == "saw_dump" || say.PhraseId == "saw_hit" || say.PhraseId == "near_pile" || say.PhraseId == "saw_near") && say.Seat != null)
            {
                _announced = true;
                _announcedVote = say.Seat;
            }
            else if (say.PhraseId == "skip_no_proof")
            {
                _announced = true;
                _announcedVote = null;
            }
            else if (say.Announces != null)
            {
                _announced = true;
                _announcedVote = say.Announces;
            }

            _replyAt = null;
            _nextAt = now + Between(GapMinS, GapMaxS);
            _seen = log.Acts.Count;
        }

        // ---------- voting ----------

        private void StepVoting(SeatView view, BotMemory mem, MeetingLog log, float now, Action<ICommand> send)
        {
            MeetingView meeting = view.Meeting!;
            if (_voteDone)
            {
                return;
            }

            if (meeting.MyVoteCast)
            {
                _voteDone = true;
                return;
            }

            if (_voteAt == null)
            {
                float delay = Math.Min(Between(VoteMinS, VoteMaxS), Math.Max(0f, meeting.EndsIn - VoteSafetyS));
                _voteAt = now + delay;
            }

            if (_task == null && !_announced && now >= _voteAt.Value - LeadS)
            {
                StartPrimary(BuildState(view, mem, log, false));
            }

            if (now < _voteAt.Value)
            {
                return;
            }

            MeetingState state = BuildState(view, mem, log, false);
            MeetingDecision d;
            if (_announced)
            {
                // a bot votes what it said in the chat (a vote line, or the seat it accused last): the talk is its decision
                d = new MeetingDecision { Vote = _announcedVote, SkipVote = _announcedVote == null, Source = _fallback.Name };
                d = Ground(d, state, "announced");
            }
            else
            {
                MeetingDecision? primary = TakePrimary(out string? reason);
                d = primary ?? RunFallback(state);
                d = Ground(d, state, primary == null ? reason : null);
            }

            _voteDone = true;
            LastSource = d.Source;
            send(new VoteCommand(d.SkipVote ? null : d.Vote));
            Report(state, d);
        }

        private byte[]? _seats;

        private MeetingState BuildState(SeatView view, BotMemory mem, MeetingLog log, bool replying)
        {
            MeetingState state = MeetingStateBuilder.Build(view, mem, log, _suspicion, HeatOf, _seats);
            state.MatchId = MatchId;
            state.Replying = replying;
            return state;
        }

        /// <summary>Grounds a decision; the result carries the primary's reason (or this one) and latency into the decision log.</summary>
        private static MeetingDecision Ground(MeetingDecision d, MeetingState state, string? reason)
        {
            MeetingDecision g = Grounding.Check(d, state);
            g.Reason = d.Reason ?? reason;
            g.LatencyMs = d.LatencyMs;
            return g;
        }

        private void Report(MeetingState state, MeetingDecision sent)
        {
            try
            {
                Sink?.Sent(state, sent);
            }
            catch (Exception)
            {
                // a failing log must never break a meeting
            }
        }

        // ---------- decisions ----------

        private void StartPrimary(MeetingState state)
        {
            var cts = new CancellationTokenSource();
            try
            {
                _task = _primary.DecideAsync(state, cts.Token);
                _cts = cts;
            }
            catch (Exception)
            {
                cts.Dispose();
                _task = null;
                _cts = null;
            }
        }

        /// <summary>The primary decision when it has finished successfully; a late one is cancelled and dropped (reason: late, error).</summary>
        private MeetingDecision? TakePrimary(out string? reason)
        {
            reason = "late";
            Task<MeetingDecision>? task = _task;
            if (task == null)
            {
                return null;
            }

            MeetingDecision? result = null;
            if (task.IsCompleted)
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    result = task.Result;
                    reason = null;
                }
                else
                {
                    GC.KeepAlive(task.Exception);
                    reason = "error";
                }
            }

            DropTask();
            return result;
        }

        private void DropTask()
        {
            Task<MeetingDecision>? task = _task;
            CancellationTokenSource? cts = _cts;
            _task = null;
            _cts = null;
            if (task == null || cts == null)
            {
                return;
            }

            if (!task.IsCompleted)
            {
                task.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
                try
                {
                    cts.Cancel();
                }
                catch (Exception)
                {
                    // a cancellation callback of the decider threw: the decision is dropped anyway
                }
            }

            cts.Dispose();
        }

        private MeetingDecision RunFallback(MeetingState state)
        {
            try
            {
                Task<MeetingDecision> t = _fallback.DecideAsync(state, CancellationToken.None);
                if (t.IsCompleted && t.Status == TaskStatus.RanToCompletion)
                {
                    return t.Result;
                }
            }
            catch (Exception)
            {
                // the fallback must be synchronous and safe; if not, stay quiet
            }

            return MeetingDecision.Quiet(_fallback.Name);
        }

        private float Between(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

        private void Reset()
        {
            DropTask();
            _inMeeting = false;
            _stage = null;
            _lines = 0;
            _seen = 0;
            _nextAt = null;
            _replyAt = null;
            _voteAt = null;
            _voteDone = false;
            _announced = false;
            _announcedVote = null;
            _ready = null;
            _readyState = null;
        }
    }
}
