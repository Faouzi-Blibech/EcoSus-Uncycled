using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Server.Content;
using Uncycled.Server.Net;

namespace Uncycled.Server.Rooms;

/// <summary>Maps engine views and events to protocol JSON (docs/protocol.md). Pure functions.</summary>
public static class Wire
{
    private static readonly ConcurrentDictionary<string, string> SnakeCache = new ConcurrentDictionary<string, string>();

    /// <summary>Id of the recycling fact shown in the debrief (the client owns the text).</summary>
    public const string DefaultFactId = "fact_01";

    public static string Snake<T>(T value) where T : struct, Enum
    {
        string name = value.ToString();
        return SnakeCache.GetOrAdd(name, ToSnake);
    }

    private static string ToSnake(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 4);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // ---------- lobby / welcome ----------

    private static void WriteSizes(Utf8JsonWriter w, int size, IReadOnlyList<int> sizes)
    {
        w.WriteNumber("size", size);
        w.WriteStartArray("sizes");
        foreach (int n in sizes)
        {
            w.WriteNumberValue(n);
        }

        w.WriteEndArray();
    }

    public static string Lobby(IReadOnlyList<Seat> seats, int hostSeat, int size, IReadOnlyList<int> sizes) =>
        JsonOut.Build(w =>
        {
            w.WriteString("t", "lobby");
            w.WriteNumber("host_seat", hostSeat);
            WriteSizes(w, size, sizes);
            w.WriteStartArray("seats");
            foreach (Seat s in seats)
            {
                if (s.Control == SeatControl.Empty)
                {
                    continue;
                }

                w.WriteStartObject();
                w.WriteNumber("seat", (int)s.Id);
                w.WriteString("name", s.Name);
                w.WriteNumber("color", (int)s.Color);
                w.WriteBoolean("human", s.Control == SeatControl.Human);
                w.WriteBoolean("ready", true);
                w.WriteEndObject();
            }

            w.WriteEndArray();
        });

    public static string Welcome(string code, int seat, string token, int hostSeat, ContentBundle content, string phase, SeatView? state, IReadOnlyList<Seat> seats, Func<byte, bool> isOut, int size = 0) =>
        JsonOut.Build(w =>
        {
            w.WriteString("t", "welcome");
            w.WriteString("room", code);
            w.WriteNumber("seat", seat);
            w.WriteString("token", token);
            w.WriteNumber("host_seat", hostSeat);
            WriteSizes(w, size > 0 ? size : content.Config.DefaultSize, content.Config.RoomSizes);
            w.WritePropertyName("tuning");
            w.WriteRawValue(content.TuningJson);
            w.WritePropertyName("tasks");
            w.WriteRawValue(content.TasksJson);
            w.WriteString("map_id", content.MapId);
            w.WriteString("map_version", content.MapVersion);
            w.WriteString("phase", phase);
            WriteSeats(w, seats, isOut);
            if (state != null)
            {
                w.WritePropertyName("state");
                w.WriteStartObject();
                WriteState(w, state);
                w.WriteEndObject();
                if (state.Me.Status == PlayerStatus.Out)
                {
                    // ghost chat so far: only an out seat's view holds any (the engine never gives it to a living seat)
                    w.WriteStartArray("ghost_chat");
                    foreach (string line in NewestGhostLines(state.GhostChat, content.Config))
                    {
                        w.WriteRawValue(line);
                    }

                    w.WriteEndArray();
                }
            }
        });

    /// <summary>
    /// The newest ghost lines that fit the welcome budget (at most GhostWelcomeMaxLines lines and
    /// GhostWelcomeMaxBytes bytes of encoded JSON), encoded, oldest first. Keeps a welcome under the
    /// client's inbound buffer however much was said.
    /// </summary>
    private static List<string> NewestGhostLines(IReadOnlyList<ChatEvent> lines, MatchConfig config)
    {
        var kept = new List<string>();
        int bytes = 0;
        for (int i = lines.Count - 1; i >= 0 && kept.Count < config.GhostWelcomeMaxLines; i--)
        {
            ChatEvent line = lines[i];
            string json = JsonOut.Build(w => WriteChat(w, line));
            int size = Encoding.UTF8.GetByteCount(json) + 1; // the comma
            if (bytes + size > config.GhostWelcomeMaxBytes)
            {
                break;
            }

            bytes += size;
            kept.Add(json);
        }

        kept.Reverse();
        return kept;
    }

    /// <summary>The "seats" array shared by welcome and match_started: every occupied seat.</summary>
    private static void WriteSeats(Utf8JsonWriter w, IReadOnlyList<Seat> seats, Func<byte, bool> isOut)
    {
        w.WriteStartArray("seats");
        foreach (Seat s in seats)
        {
            if (s.Control == SeatControl.Empty)
            {
                continue;
            }

            w.WriteStartObject();
            w.WriteNumber("seat", (int)s.Id);
            w.WriteString("name", s.Name);
            w.WriteNumber("color", (int)s.Color);
            w.WriteBoolean("human", s.Control == SeatControl.Human);
            w.WriteBoolean("out", isOut(s.Id));
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteState(Utf8JsonWriter w, SeatView v)
    {
        MyView me = v.Me;
        w.WriteString("phase", Snake(v.Phase));
        w.F("match_t", v.MatchTime);
        w.F("clock_t", v.DeadlineClockValue);
        w.WriteString("role", Snake(me.Role));
        if (me.Role == Role.Polluter && me.Partners.Count > 0)
        {
            w.WriteNumber("partner", (int)me.Partners[0]);
        }
        else
        {
            w.WriteNull("partner");
        }

        if (me.Role == Role.Polluter && me.Signature.HasValue)
        {
            w.WriteString("signature", Snake(me.Signature.Value));
        }
        else
        {
            w.WriteNull("signature");
        }

        w.WriteString("status", Snake(me.Status));
        if (me.Poison.HasValue && me.Role == Role.Citizen)
        {
            w.WriteNumber("poison", me.Poison.Value);
        }
        else
        {
            w.WriteNull("poison");
        }

        w.WriteStartArray("tasks");
        foreach (TaskInfo t in me.Tasks)
        {
            WriteTask(w, t);
        }

        w.WriteEndArray();
        WriteBags(w, me);
        if (me.Cooldowns != null)
        {
            WriteCooldowns(w, "cooldowns", me.Cooldowns);
        }
        else
        {
            w.WriteNull("cooldowns");
        }

        w.WriteNumber("emergencies_left", v.EmergenciesLeft);
        w.F("meter", v.Meter);
        if (v.Meeting != null)
        {
            MeetingView m = v.Meeting;
            w.WriteStartObject("meeting");
            w.WriteString("stage", Snake(m.Stage));
            w.F("ends_in", m.EndsIn);
            w.WriteNumber("by", (int)m.CalledBy);
            w.WriteString("cause", Snake(m.Cause));
            WriteClues(w, "clues", m.Clues);
            w.Ids("voted", m.Voted);
            if (m.MyVote.HasValue)
            {
                w.WriteNumber("my_vote", (int)m.MyVote.Value);
            }
            else
            {
                w.WriteNull("my_vote");
            }

            w.WriteBoolean("my_vote_cast", m.MyVoteCast);
            w.WriteNumber("my_chat", m.MyChatCount);
            w.WriteNumber("my_text", m.MyText);
            w.Ids("living", m.Living);
            w.F("task_bar", m.TaskBar);
            w.WriteEndObject();
        }
        else
        {
            w.WriteNull("meeting");
        }

        WriteSabotage(w, "sabotage", v.Sabotage);
        if (v.GameOver != null)
        {
            w.WriteStartObject("game_over");
            WriteGameOverBody(w, v.GameOver);
            w.WriteEndObject();
        }
        else
        {
            w.WriteNull("game_over");
        }
    }

    /// <summary>
    /// One chat line: {t, seat, text} for a typed line or {t, seat, id, args} for a phrase, plus
    /// ghost: true for a ghost line (out seats only). Used by the chat event and welcome.ghost_chat.
    /// </summary>
    private static void WriteChat(Utf8JsonWriter w, ChatEvent e)
    {
        w.WriteString("t", "chat");
        w.WriteNumber("seat", (int)e.Seat);
        if (e.PhraseId == null)
        {
            w.WriteString("text", e.Text ?? "");
        }
        else
        {
            w.WriteString("id", e.PhraseId);
            w.WriteStartArray("args");
            foreach (ChatArg a in e.Args)
            {
                if (a.Kind == ChatArgKind.Seat)
                {
                    w.WriteNumberValue((int)a.Seat);
                }
                else
                {
                    w.WriteStringValue(a.Zone);
                }
            }

            w.WriteEndArray();
        }

        if (e.Ghost)
        {
            w.WriteBoolean("ghost", true);
        }
    }

    // ---------- snapshot ----------

    public static string Snapshot(SeatView v, long tick, double serverTime, SnapshotExtras? extras = null) =>
        JsonOut.Build(w =>
        {
            MyView me = v.Me;
            w.WriteString("t", "snapshot");
            w.WriteNumber("tick", tick);
            w.WriteNumber("s", Math.Round(serverTime, 3));
            w.WriteString("phase", Snake(v.Phase));
            w.F("phase_left", v.PhaseTimeLeft);
            w.F("match_t", v.MatchTime);
            w.F("deadline_t", v.DeadlineS);
            w.F("clock_t", v.DeadlineClockValue);
            w.F("meter", v.Meter);

            w.WriteStartObject("me");
            w.Pos("pos", me.Position);
            w.F("vision", me.Vision);
            w.F("smog_level", extras?.SmogLevel ?? 0f);
            w.WriteString("status", Snake(me.Status));
            if (me.Role == Role.Citizen && me.Poison.HasValue)
            {
                w.WriteNumber("poison", me.Poison.Value);
            }

            WriteBags(w, me);
            if (me.Cooldowns != null)
            {
                WriteCooldowns(w, "cd", me.Cooldowns);
            }

            w.WriteNumber("emergencies_left", v.EmergenciesLeft);
            w.WriteEndObject();

            w.WriteStartArray("spots");
            foreach (SpotView s in v.Spots)
            {
                w.WriteStartObject();
                w.WriteString("id", s.Id);
                w.WriteString("state", Snake(s.State));
                w.WriteEndObject();
            }

            w.WriteEndArray();

            if (me.Status != PlayerStatus.Out)
            {
                w.WriteStartArray("players");
                foreach (PlayerView p in v.Players)
                {
                    w.WriteStartObject();
                    w.WriteNumber("seat", (int)p.Seat);
                    w.Pos("pos", p.Position);
                    w.WriteNumber("color", (int)p.Color);
                    w.WriteBoolean("out", p.IsOut);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteStartArray("piles");
                foreach (PileView p in v.Piles)
                {
                    w.WriteStartObject();
                    w.WriteNumber("id", p.Id);
                    w.Pos("pos", p.Position);
                    w.WriteString("kind", Snake(p.Kind));
                    w.WriteString("state", Snake(p.State));
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            WriteSabotage(w, "sabotage", v.Sabotage, extras?.RepairElapsed);
        });

    /// <summary>bags, cap and tool: Cleaners only (a Trasher never carries bags).</summary>
    private static void WriteBags(Utf8JsonWriter w, MyView me)
    {
        if (me.Role != Role.Citizen)
        {
            return;
        }

        w.WriteNumber("bags", me.Bags);
        w.WriteNumber("cap", me.BagCap);
        w.WriteBoolean("tool", me.HasTool);
    }

    private static void WriteCooldowns(Utf8JsonWriter w, string name, CooldownView cd)
    {
        w.WriteStartObject(name);
        w.F("hit", cd.Hit);
        w.F("dump", cd.Dump);
        w.F("sabotage", cd.Sabotage);
        w.WriteEndObject();
    }

    private static void WriteSabotage(Utf8JsonWriter w, string name, SabotageView? s, float? repairElapsed = null)
    {
        if (s == null)
        {
            w.WriteNull(name);
            return;
        }

        w.WriteStartObject(name);
        w.WriteString("kind", Snake(s.Kind));
        if (s.Countdown.HasValue)
        {
            w.F("countdown", s.Countdown.Value);
        }

        w.WriteStartArray("held");
        foreach (string h in s.Held)
        {
            w.WriteStringValue(h);
        }

        w.WriteEndArray();
        w.F("smog_level", s.SmogLevel);
        if (repairElapsed.HasValue)
        {
            w.F("repair_t", repairElapsed.Value);
        }

        w.WriteEndObject();
    }

    private static void WriteTask(Utf8JsonWriter w, TaskInfo t)
    {
        w.WriteStartObject();
        w.WriteNumber("id", t.Id);
        w.WriteString("type", t.TypeId);
        w.WriteNumber("steps", t.Steps);
        w.WriteNumber("steps_done", t.StepsDone);
        w.WriteString("console", t.ConsoleId);
        w.WriteEndObject();
    }

    private static void WriteClues(Utf8JsonWriter w, string name, IReadOnlyList<ClueInfo> clues)
    {
        w.WriteStartArray(name);
        foreach (ClueInfo c in clues)
        {
            w.WriteStartObject();
            w.WriteNumber("pile", c.PileId);
            w.WriteString("zone", c.ZoneId);
            w.F("t", c.Time);
            if (c.Waste.HasValue)
            {
                w.WriteString("waste", Snake(c.Waste.Value));
            }

            if (c.Victim.HasValue)
            {
                w.WriteNumber("victim", (int)c.Victim.Value);
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteGameOverBody(Utf8JsonWriter w, GameOverEvent e)
    {
        w.WriteString("winner", Snake(e.Winner));
        w.WriteString("reason", WireNames.Of(e.Reason));
        w.WriteStartObject("roles");
        foreach (RoleEntry r in e.Roles)
        {
            w.WriteString(((int)r.Seat).ToString(), Snake(r.Role));
        }

        w.WriteEndObject();
        w.WriteStartArray("timeline");
        foreach (TimelineEntry t in e.Timeline)
        {
            w.WriteStartObject();
            w.F("t", t.Time);
            w.WriteString("kind", Snake(t.Kind));
            if (t.Seat.HasValue)
            {
                w.WriteNumber("seat", (int)t.Seat.Value);
            }

            if (t.Target.HasValue)
            {
                w.WriteNumber("target", (int)t.Target.Value);
            }

            if (!string.IsNullOrEmpty(t.ZoneId))
            {
                w.WriteString("zone", t.ZoneId);
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteString("fact", DefaultFactId);
        w.F("match_t", e.MatchTime);
    }

    // ---------- events ----------

    /// <summary>
    /// Protocol message for an engine event, or null when it has none. The same string goes to
    /// every recipient (the engine already decided who may see it).
    /// </summary>
    public static string? Event(GameEvent ev, IReadOnlyList<Seat>? seats = null, Func<byte, bool>? isOut = null)
    {
        switch (ev)
        {
            case MatchStartedEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "match_started");
                    w.WriteString("role", Snake(e.Role));
                    if (e.Role == Role.Polluter)
                    {
                        if (e.Partners.Count > 0)
                        {
                            w.WriteNumber("partner", (int)e.Partners[0]);
                        }

                        if (e.Signature.HasValue)
                        {
                            w.WriteString("signature", Snake(e.Signature.Value));
                        }
                    }

                    w.WriteStartArray("tasks");
                    foreach (TaskInfo t in e.Tasks)
                    {
                        WriteTask(w, t);
                    }

                    w.WriteEndArray();
                    w.Pos("spawn", e.Spawn);
                    w.F("intro_s", e.IntroS);
                    if (seats != null)
                    {
                        WriteSeats(w, seats, isOut ?? (_ => false));
                    }
                });

            case PhaseChangedEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "phase_changed");
                    w.WriteString("phase", Snake(e.Phase));
                    if (e.Stage.HasValue)
                    {
                        w.WriteString("stage", Snake(e.Stage.Value));
                    }
                });

            case CorrectionEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "correct");
                    w.Pos("pos", e.Position);
                });

            case UseOkEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "use_ok");
                    w.WriteString("console", e.ConsoleId);
                    w.WriteString("kind", e.Kind == ConsoleKind.Task ? "spot" : Snake(e.Kind));
                    if (e.TaskId.HasValue)
                    {
                        w.WriteNumber("task", e.TaskId.Value);
                    }

                    if (e.TaskType != null)
                    {
                        w.WriteString("type", e.TaskType);
                    }

                    if (e.Step.HasValue)
                    {
                        w.WriteNumber("step", e.Step.Value);
                    }
                });

            case SpotCleanedEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "spot_cleaned");
                    w.WriteString("spot", e.Spot);
                    w.WriteNumber("bags", e.Bags);
                });

            case TaskProgressEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "task_progress");
                    w.WriteNumber("task", e.TaskId);
                    w.WriteNumber("steps_done", e.StepsDone);
                });

            case TaskAddedEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "task_added");
                    w.WritePropertyName("task");
                    WriteTask(w, e.Task);
                });

            case CleanOkEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "clean_ok");
                    w.WriteNumber("pile", e.PileId);
                    w.F("duration", e.Duration);
                });

            case CleanDoneEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "clean_done");
                    w.WriteNumber("pile", e.PileId);
                });

            case CleanCancelledEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "clean_cancelled");
                    w.WriteNumber("pile", e.PileId);
                });

            case CueEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "cue");
                    w.WriteString("kind", Snake(e.Kind));
                    w.WriteNumber("seat", (int)e.Actor);
                    w.Pos("pos", e.Position);
                });

            case HitOkEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "hit_ok");
                    w.F("cooldown", e.Cooldown);
                });

            case DumpOkEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "dump_ok");
                    w.WriteNumber("pile", e.PileId);
                    w.F("cooldown", e.Cooldown);
                });

            case PoisonedEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "poisoned");
                    w.WriteNumber("level", e.Level);
                });

            case YouAreOutEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "you_are_out");
                    w.WriteString("cause", Snake(e.Cause));
                });

            case MeetingStartedEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "meeting_started");
                    w.WriteNumber("by", (int)e.By);
                    w.WriteString("cause", Snake(e.Cause));
                    if (e.ReportedPileId.HasValue)
                    {
                        w.WriteNumber("pile", e.ReportedPileId.Value);
                    }

                    WriteClues(w, "clues", e.Clues);
                    w.F("task_bar", e.TaskBar);
                    w.F("discussion_s", e.DiscussionS);
                    w.Ids("living", e.Living);
                });

            case ChatEvent e:
                return JsonOut.Build(w => WriteChat(w, e));

            case VotingStartedEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "voting_started");
                    w.F("voting_s", e.VotingS);
                });

            case VoteCastEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "vote_cast");
                    w.WriteNumber("seat", (int)e.Seat);
                });

            case MeetingResultEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "meeting_result");
                    w.WriteStartObject("votes");
                    foreach (VoteRecord vr in e.Votes)
                    {
                        string key = ((int)vr.Voter).ToString();
                        if (vr.Target.HasValue)
                        {
                            w.WriteNumber(key, (int)vr.Target.Value);
                        }
                        else
                        {
                            w.WriteString(key, "skip");
                        }
                    }

                    w.WriteEndObject();
                    if (e.Ejected.HasValue)
                    {
                        w.WriteNumber("ejected", (int)e.Ejected.Value);
                    }

                    w.WriteBoolean("tie", e.Tie);
                });

            case EjectionEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "ejection");
                    w.WriteNumber("seat", (int)e.Seat);
                    if (e.WasPolluter.HasValue)
                    {
                        w.WriteBoolean("was_polluter", e.WasPolluter.Value);
                    }
                    else
                    {
                        w.WriteNull("was_polluter");
                    }
                });

            case RoundResumedEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "round_resumed");
                    w.Pos("spawn", e.Spawn);
                });

            case SabotageStartedEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "sabotage_started");
                    w.WriteString("kind", Snake(e.Kind));
                    if (e.Countdown.HasValue)
                    {
                        w.F("countdown", e.Countdown.Value);
                    }
                });

            case SabotageEndedEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "sabotage_ended");
                    w.WriteString("kind", Snake(e.Kind));
                    w.WriteString("result", Snake(e.Result));
                });

            case GameOverEvent e:
                return JsonOut.Build(w =>
                {
                    w.WriteString("t", "game_over");
                    WriteGameOverBody(w, e);
                });

            default:
                return null;
        }
    }
}
