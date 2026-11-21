using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots.Meeting;

namespace Uncycled.Bots.Neural
{
    /// <summary>
    /// The Workers AI request for Clef-flash (checked with curl on 2026-11-20): {"model":"clef-flash",
    /// "state":{...},"questions":{"qid":{"type":"choice","instructions":"...","criteria":{"key":"desc"}}}}.
    /// The state holds only what this bot knows; the questions have fixed options.
    /// </summary>
    public static class ClefRequest
    {
        public const string Model = "clef-flash";
        public const string QSpeak = "speak";
        public const string QKind = "kind";
        public const string QSeat = "seat";
        public const string QZone = "zone";
        public const string QVote = "vote";
        public const string None = "none";
        public const string Skip = "skip";

        public static readonly string[] Kinds = { "accuse", "alibi", "question", "react", "vote_talk", "quiet" };

        public static string SeatKey(byte seat) => "seat_" + seat;

        public static string Build(MeetingState s)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteString("model", Model);
                    WriteState(w, s);
                    w.WriteStartObject("questions");
                    if (s.Stage == MeetingStage.Voting)
                    {
                        WriteVote(w, s);
                    }
                    else
                    {
                        WriteSpeak(w);
                        WriteKind(w);
                        WriteSeat(w, s);
                        WriteZone(w, s);
                    }

                    w.WriteEndObject();
                    w.WriteEndObject();
                }

                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        // ---------- state ----------

        private static void WriteState(Utf8JsonWriter w, MeetingState s)
        {
            w.WriteStartObject("state");
            w.WriteString("situation", "Meeting of a social deduction game with " + s.Seats + " seats. Citizens do tasks; " + s.Polluters + (s.Polluters == 1 ? " hidden polluter dumps" : " hidden polluters dump") + " waste and poison. "
                + "After a dump or a body is found the seats discuss, then vote to eject one seat or skip. Citizens win by ejecting every polluter.");

            w.WriteStartObject("me");
            w.WriteNumber("seat", s.Self);
            w.WriteString("role", s.Role == Role.Polluter ? "polluter" : "citizen");
            if (s.Role == Role.Polluter)
            {
                w.WriteStartArray("partners");
                foreach (byte p in s.Partners)
                {
                    w.WriteNumberValue(p);
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();

            w.WriteString("stage", s.Stage == MeetingStage.Voting ? "voting" : "discussion");
            w.WriteNumber("seconds_left", Math.Round(s.StageLeft, 1));

            w.WriteStartArray("living");
            foreach (byte seat in s.Living)
            {
                w.WriteNumberValue(seat);
            }

            w.WriteEndArray();

            w.WriteStartArray("clues");
            foreach (Sim.Uncycled.ClueInfo c in s.Clues)
            {
                w.WriteStartObject();
                w.WriteString("zone", c.ZoneId);
                w.WriteNumber("time_s", Math.Round(c.Time, 0));
                w.WriteString("kind", c.Kind.ToString().ToLowerInvariant());
                w.WriteEndObject();
            }

            w.WriteEndArray();

            w.WriteStartArray("chat");
            foreach (ChatAct a in s.Chat)
            {
                w.WriteStartObject();
                w.WriteNumber("speaker", a.Speaker);
                w.WriteString("phrase", a.PhraseId);
                if (a.Seat != null)
                {
                    w.WriteNumber("seat", a.Seat.Value);
                }

                if (a.Zone != null)
                {
                    w.WriteString("zone", a.Zone);
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();

            w.WriteStartArray("evidence");
            foreach (EvidenceItem e in s.Evidence)
            {
                w.WriteStartObject();
                w.WriteString("kind", e.Kind);
                if (e.Seat != null)
                {
                    w.WriteNumber("seat", e.Seat.Value);
                }

                w.WriteString("zone", e.Zone);
                w.WriteNumber("time_s", Math.Round(e.Time, 0));
                w.WriteEndObject();
            }

            w.WriteEndArray();

            w.WriteStartObject("suspicion");
            foreach (KeyValuePair<byte, float> kv in s.Suspicion.OrderBy(k => k.Key))
            {
                w.WriteNumber(SeatKey(kv.Key), Math.Round(kv.Value, 0));
            }

            w.WriteEndObject();

            w.WriteStartObject("heat");
            foreach (KeyValuePair<byte, float> kv in s.Heat.OrderBy(k => k.Key))
            {
                w.WriteNumber(SeatKey(kv.Key), Math.Round(kv.Value, 2));
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        // ---------- questions ----------

        private static void Question(Utf8JsonWriter w, string id, string instructions, IEnumerable<KeyValuePair<string, string>> criteria)
        {
            w.WriteStartObject(id);
            w.WriteString("type", "choice");
            w.WriteString("instructions", instructions);
            w.WriteStartObject("criteria");
            foreach (KeyValuePair<string, string> c in criteria)
            {
                w.WriteString(c.Key, c.Value);
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        private static void WriteSpeak(Utf8JsonWriter w)
        {
            Question(w, QSpeak,
                "Should this seat say a line right now? Speak only for a reason: to accuse a seat with evidence it holds, to answer an accusation against it, "
                + "to give an alibi, or to ask a pointed question. Otherwise wait. Never speak twice in a row without news.",
                new[]
                {
                    new KeyValuePair<string, string>("speak", "Say one line now."),
                    new KeyValuePair<string, string>("wait", "Stay quiet for now."),
                });
        }

        private static void WriteKind(Utf8JsonWriter w)
        {
            Question(w, QKind,
                "If this seat speaks now, what kind of line is most useful? A citizen only accuses on evidence it holds. A polluter may mislead "
                + "but must never accuse or doubt its partner.",
                new[]
                {
                    new KeyValuePair<string, string>("accuse", "Accuse a seat of a dump, a hit or being near a pile."),
                    new KeyValuePair<string, string>("alibi", "Say where this seat was, or who it was with."),
                    new KeyValuePair<string, string>("question", "Ask a seat where it was."),
                    new KeyValuePair<string, string>("react", "React to an accusation: deny it, agree with a seat or doubt a seat."),
                    new KeyValuePair<string, string>("vote_talk", "Say who this seat will vote for, or that there is no proof."),
                    new KeyValuePair<string, string>("quiet", "Nothing useful to say."),
                });
        }

        private static void WriteSeat(Utf8JsonWriter w, MeetingState s)
        {
            var criteria = new List<KeyValuePair<string, string>>();
            foreach (byte seat in s.Living)
            {
                criteria.Add(new KeyValuePair<string, string>(SeatKey(seat), "Seat " + seat + (seat == s.Self ? " (this seat)." : ".")));
            }

            criteria.Add(new KeyValuePair<string, string>(None, "No seat is concerned."));
            Question(w, QSeat,
                "Which seat should the line be about? Pick the seat with the strongest suspicion or the one a reply is owed to; none when the line names no seat.",
                criteria);
        }

        private static void WriteZone(Utf8JsonWriter w, MeetingState s)
        {
            var criteria = new List<KeyValuePair<string, string>>();
            foreach (string zone in s.Zones)
            {
                criteria.Add(new KeyValuePair<string, string>(zone, "The zone " + zone + "."));
            }

            criteria.Add(new KeyValuePair<string, string>(None, "No zone is concerned."));
            Question(w, QZone,
                "Which zone should the line mention (where a dump was seen, or where this seat was)? None when the line names no zone.",
                criteria);
        }

        private static void WriteVote(Utf8JsonWriter w, MeetingState s)
        {
            var criteria = new List<KeyValuePair<string, string>>();
            foreach (byte seat in s.Living)
            {
                if (seat != s.Self)
                {
                    criteria.Add(new KeyValuePair<string, string>(SeatKey(seat), "Vote to eject seat " + seat + "."));
                }
            }

            criteria.Add(new KeyValuePair<string, string>(Skip, "Skip the vote: not enough proof."));
            Question(w, QVote,
                "Who should this seat vote to eject? Vote a seat only with real suspicion (evidence it holds, or several accusations); a wrong ejection helps the polluters, "
                + "so skip when there is no proof. A polluter must never vote for its partner.",
                criteria);
        }
    }
}
