using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots.Meeting;

namespace Uncycled.Bots.Neural
{
    /// <summary>
    /// One JSON line per decision the talker really sends (match id, seat, stage, source clef or rule, why the
    /// rule path answered, the model's latency in ms, the phrase or vote) and plain notes (for example the one
    /// auth failure). The sink gets complete lines; never pass it secrets: nothing from the request headers is
    /// logged. ToFile queues the lines and writes them on a background task, so the room tick never waits on disk.
    /// </summary>
    public sealed class MeetingDecisionLog : IDecisionSink, IDisposable
    {
        private readonly Action<string> _writeLine;
        private readonly Channel<string>? _queue;
        private readonly Task? _writer;

        public MeetingDecisionLog(Action<string> writeLine)
        {
            _writeLine = writeLine ?? throw new ArgumentNullException(nameof(writeLine));
        }

        private MeetingDecisionLog(string path)
        {
            _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
            _writeLine = line => _queue.Writer.TryWrite(line);
            _writer = Task.Run(() => WriteLoop(path));
        }

        /// <summary>A log appending to a file (created with its folder when missing); Dispose it to flush the queue.</summary>
        public static MeetingDecisionLog ToFile(string path)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return new MeetingDecisionLog(path);
        }

        private async Task WriteLoop(string path)
        {
            await foreach (string line in _queue!.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    try
                    {
                        File.AppendAllText(path, line + "\n");
                        break;
                    }
                    catch (IOException)
                    {
                        // the file is busy for a moment; the lines wait in the queue, not on the tick
                        await Task.Delay(25).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>Stops a file log after writing what is queued.</summary>
        public void Dispose()
        {
            if (_queue != null)
            {
                _queue.Writer.TryComplete();
                _writer!.Wait(TimeSpan.FromSeconds(5));
            }
        }

        /// <summary>Logs the decision the talker sent: source, the fallback reason and the model latency it carries.</summary>
        public void Sent(MeetingState state, MeetingDecision sent)
        {
            Decision(state, sent.Source, sent.Reason, sent.LatencyMs ?? 0L, sent);
        }

        public void Decision(MeetingState state, string source, string? fallbackReason, long latencyMs, MeetingDecision d)
        {
            Write(w =>
            {
                w.WriteString("match", state.MatchId);
                w.WriteNumber("seat", state.Self);
                w.WriteString("stage", state.Stage == MeetingStage.Voting ? "voting" : "discussion");
                w.WriteString("source", source);
                if (fallbackReason != null)
                {
                    w.WriteString("fallback", fallbackReason);
                }

                w.WriteNumber("latency_ms", latencyMs);
                if (d.Speak)
                {
                    w.WriteString("phrase", d.PhraseId);
                    if (d.Seat != null)
                    {
                        w.WriteNumber("target", d.Seat.Value);
                    }

                    if (d.Zone != null)
                    {
                        w.WriteString("zone", d.Zone);
                    }
                }

                if (state.Stage == MeetingStage.Voting)
                {
                    if (d.Vote != null && !d.SkipVote)
                    {
                        w.WriteNumber("vote", d.Vote.Value);
                    }
                    else
                    {
                        w.WriteString("vote", "skip");
                    }
                }
            });
        }
        public void Note(string message)
        {
            Write(w => w.WriteString("note", message));
        }

        private void Write(Action<Utf8JsonWriter> body)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    body(w);
                    w.WriteEndObject();
                }

                try
                {
                    _writeLine(Encoding.UTF8.GetString(ms.ToArray()));
                }
                catch (Exception)
                {
                    // a failing log sink must never break a meeting
                }
            }
        }
    }
}
