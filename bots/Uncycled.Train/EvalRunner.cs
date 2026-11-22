using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots;
using Uncycled.Bots.Meeting;
using Uncycled.Server.Content;

namespace Uncycled.Train
{
    /// <summary>
    /// --eval: plays one full match per seed with a brain for every seat (no trainer) and returns one
    /// JSON line per match, in seed order, whatever the thread count.
    /// </summary>
    public static class EvalRunner
    {
        private const float TickDt = MatchRunner.TickDt;
        private const float ExtraSecondsAfterDeadline = 300f;

        /// <param name="brains">Brain per role; the same delegate the trainer host uses for non-learner seats.</param>
        public static IReadOnlyList<string> Run(HostOptions options, ContentBundle content, Func<Role, IRandom, IBotBrain> brains)
        {
            int count = checked((int)(options.SeedTo - options.SeedFrom + 1));
            var lines = new string[count];
            var po = new ParallelOptions { MaxDegreeOfParallelism = options.Threads };
            Parallel.For(0, count, po, i => lines[i] = PlayOne(options.SeedFrom + (ulong)i, content, brains));
            return lines;
        }

        private static string PlayOne(ulong seed, ContentBundle content, Func<Role, IRandom, IBotBrain> brains)
        {
            MatchConfig cfg = content.Config;
            var rng = new XorShiftRandom(seed);
            var seats = new List<SeatSetup>();
            for (int i = 0; i < cfg.Seats; i++)
            {
                seats.Add(new SeatSetup((byte)i, "bot" + i, (byte)i));
            }

            var engine = new MatchEngine(cfg, content.Map, seats, content.Tasks, rng, content.Phrases);
            var brainBySeat = new IBotBrain[cfg.Seats];
            var send = new Action<ICommand>[cfg.Seats];
            var feed = new MeetingFeed(new MeetingLog(content.Phrases ?? throw new InvalidOperationException("the content has no phrase catalogue")));
            var floor = new MeetingFloor();
            for (int s = 0; s < cfg.Seats; s++)
            {
                byte seat = (byte)s;
                // the bots talk in the meetings like in the live room (rule decider, no model call)
                brainBySeat[s] = OfflineTalk.Wrap(brains(engine.ViewFor(seat).Me.Role, rng), feed, floor, seat, seed);
                send[s] = cmd => engine.Apply(seat, cmd);
            }

            for (int s = 0; s < brainBySeat.Length; s++)
            {
                if (brainBySeat[s] is IMeetingObserver observer)
                {
                    feed.Add((byte)s, observer, brainBySeat[s] as IAccuser);
                }
            }

            int correctEjections = 0;
            int hits = 0;
            int dumps = 0;
            int meetings = 0;
            int chatLines = 0;
            int identicalLines = 0;
            var saidInMeeting = new HashSet<string>();
            int hedgeLines = 0;
            int accusedSeats = 0;
            int accusedAnswered = 0;
            var accused = new Dictionary<byte, bool>(); // accused seat of this meeting -> has spoken since
            int wrongEjections = 0;
            int deliveries = 0;
            int repollutes = 0;
            int abilitiesUsed = 0;
            int maxTicks = (int)((cfg.DeadlineS + ExtraSecondsAfterDeadline) / TickDt) * 4;
            for (int t = 0; t < maxTicks && engine.Phase != MatchPhase.Ended; t++)
            {
                for (int s = 0; s < brainBySeat.Length; s++)
                {
                    brainBySeat[s].Think(engine, (byte)s, TickDt, send[s]);
                }

                IReadOnlyList<GameEvent> events = engine.Tick(TickDt);
                feed.Handle(events, engine.MatchTime);
                foreach (GameEvent e in events)
                {
                    if (e is EjectionEvent ej && engine.Find(ej.Seat)?.Role == Role.Polluter)
                    {
                        correctEjections++;
                    }
                    else if (e is EjectionEvent)
                    {
                        wrongEjections++;
                    }
                    else if (e is DeliveredEvent)
                    {
                        deliveries++;
                    }
                    else if (e is RepolluteOkEvent)
                    {
                        repollutes++;
                    }
                    else if (e is VirusPlantedEvent || e is GasWarningEvent)
                    {
                        abilitiesUsed++;
                    }
                    else if (e is HitOkEvent)
                    {
                        hits++;
                    }
                    else if (e is DumpOkEvent)
                    {
                        dumps++;
                    }
                    else if (e is MeetingStartedEvent)
                    {
                        meetings++;
                        saidInMeeting.Clear();
                        accused.Clear();
                    }
                    else if (e is ChatEvent chat && !chat.Ghost)
                    {
                        chatLines++;
                        if (chat.PhraseId == "hmm" || chat.PhraseId == "idk" || chat.PhraseId == "sus")
                        {
                            hedgeLines++; // an unsure line: no eyewitness claim behind it
                        }

                        if (accused.TryGetValue(chat.Seat, out bool spoke) && !spoke)
                        {
                            accused[chat.Seat] = true;
                            accusedAnswered++;
                        }

                        if (chat.PhraseId == "sus" || chat.PhraseId == "saw_dump" || chat.PhraseId == "saw_hit" || chat.PhraseId == "near_pile")
                        {
                            foreach (ChatArg arg in chat.Args)
                            {
                                if (arg.Kind == ChatArgKind.Seat && arg.Seat != chat.Seat && !accused.ContainsKey(arg.Seat))
                                {
                                    accused[arg.Seat] = false;
                                    accusedSeats++;
                                }
                            }
                        }

                        if (chat.PhraseId != null && !saidInMeeting.Add(LineKey(chat)))
                        {
                            identicalLines++; // the same phrase id and slots said twice in one meeting
                        }
                    }
                }
            }

            GameOverEvent? result = engine.Result;
            var sb = new StringBuilder();
            sb.Append("{\"seed\":").Append(seed.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"winner\":\"").Append(result == null ? "none" : result.Winner == Winner.Citizens ? "citizens" : "polluters").Append('"');
            sb.Append(",\"reason\":\"").Append(result == null ? "unfinished" : Snake(result.Reason.ToString())).Append('"');
            sb.Append(",\"tasks_done\":").Append(engine.Tasks.DoneSteps.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"tasks_total\":").Append(engine.Tasks.TotalSteps.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"meter\":").Append(engine.Meter.Value.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(",\"correct_ejections\":").Append(correctEjections.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"wrong_ejections\":").Append(wrongEjections.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"deliveries\":").Append(deliveries.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"repollutes\":").Append(repollutes.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"abilities_used\":").Append(abilitiesUsed.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"hits\":").Append(hits.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"dumps\":").Append(dumps.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"meetings\":").Append(meetings.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"chat_lines\":").Append(chatLines.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"identical_lines\":").Append(identicalLines.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"hedge_lines\":").Append(hedgeLines.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"accused_seats\":").Append(accusedSeats.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"accused_answered\":").Append(accusedAnswered.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"match_time\":").Append(engine.MatchTime.ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append(",\"active_time\":").Append(engine.ActiveTime.ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>A phrase line as one key: the id and its slots in order (and the speaker for not_me, i_was, with), so two lines are identical when the keys are equal.</summary>
        private static string LineKey(ChatEvent chat)
        {
            var sb = new StringBuilder(chat.PhraseId);
            if (chat.PhraseId == "not_me" || chat.PhraseId == "i_was" || chat.PhraseId == "with")
            {
                sb.Append("|by").Append(chat.Seat.ToString(CultureInfo.InvariantCulture)); // first-person lines are separate statements per speaker
            }

            foreach (ChatArg arg in chat.Args)
            {
                sb.Append('|').Append(arg.Kind == ChatArgKind.Seat ? "s" + arg.Seat.ToString(CultureInfo.InvariantCulture) : "z" + arg.Zone);
            }

            return sb.ToString();
        }

        private static string Snake(string pascal)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascal[i]))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(pascal[i]));
            }

            return sb.ToString();
        }
    }
}
