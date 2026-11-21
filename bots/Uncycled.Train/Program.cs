using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core;
using Uncycled.Bots;
using Uncycled.Bots.Meeting;
using Uncycled.Bots.Neural;
using Uncycled.Server.Content;

namespace Uncycled.Train
{
    /// <summary>
    /// Training host. stdout carries only protocol bytes (see PipeProtocol); every log line goes to stderr.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // Anything that writes to Console.Out by accident must not corrupt the binary stream.
            Stream stdout = Console.OpenStandardOutput();
            Console.SetOut(Console.Error);
            try
            {
                HostOptions options = HostOptions.Parse(args);
                ContentBundle content = ContentLoader.Load(options.ContentDir);
                BotsSettings settings = options.Settings;
                Func<Role, IRandom, IBotBrain> brains = HostBrains.Scripted(settings, content);
                if (options.BakeOff)
                {
                    return RunBakeOff(options, content, settings);
                }

                if (options.Eval)
                {
                    foreach (string line in EvalRunner.Run(options, content, HostBrains.ForEval(options, settings, content)))
                    {
                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(line + "\n");
                        stdout.Write(bytes, 0, bytes.Length);
                    }

                    stdout.Flush();
                    return 0;
                }

                var runners = new MatchRunner[options.Matches];
                for (int i = 0; i < runners.Length; i++)
                {
                    runners[i] = new MatchRunner(options, i, content, brains);
                }

                return options.BenchSeconds != null
                    ? Bench(options, runners)
                    : Serve(options, runners, stdout);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine("error: " + e.Message);
                Console.Error.WriteLine("usage: Uncycled.Train --content <dir> --matches <N> --seed <S> --learner citizen|polluter --threads <T> [--bench <seconds>] [--bots <bots.json>] [--rw-win v] [--rw-task v] [--rw-report v] [--rw-fix v] [--rw-hit v] [--rw-dump v] [--rw-ejected v]");
                Console.Error.WriteLine("       Uncycled.Train --bakeoff --content <dir> --meetings N --seeds A-B --out <file.json> [--max-calls N] [--deciders rule,clef,skip] [--bots <bots.json>]");
                return 2;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("fatal: " + e);
                return 1;
            }
        }

        /// <summary>--bakeoff: rule vs Clef (or the --deciders named) on the same vote stages; the report goes to --out, a table to stderr.</summary>
        private static int RunBakeOff(HostOptions options, ContentBundle content, BotsSettings settings)
        {
            if (string.IsNullOrEmpty(options.Out))
            {
                throw new ArgumentException("--bakeoff needs --out <file.json>");
            }

            var deciders = new List<IMeetingDecider>();
            using (var http = new HttpClient())
            using (var note = new MeetingDecisionLog(line => Console.Error.WriteLine(line)))
            {
                foreach (string name in options.Deciders.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    switch (name.Trim())
                    {
                        case "rule":
                            deciders.Add(new RuleDecider());
                            break;
                        case "skip":
                            deciders.Add(new SkipDecider());
                            break;
                        case "clef":
                            var budget = new DailyBudget(options.MaxCalls, () => DateTime.UtcNow);
                            ClefDecider? clef = ClefDecider.FromEnvironment(http, budget, note, TimeSpan.FromSeconds(settings.MeetingTimeoutS));
                            deciders.Add(clef ?? throw new ArgumentException("--deciders clef needs CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN in the environment"));
                            break;
                        default:
                            throw new ArgumentException("--deciders knows rule, clef and skip, got '" + name + "'");
                    }
                }

                var bake = new BakeOffOptions { Meetings = options.Meetings, SeedFrom = options.SeedFrom, SeedTo = options.SeedTo, MaxCalls = options.MaxCalls, CallTimeout = TimeSpan.FromSeconds(settings.MeetingTimeoutS + 2f) };
                BakeOffReport report = BakeOff.Run(content, settings, bake, deciders);
                File.WriteAllText(options.Out!, report.ToJson() + Environment.NewLine);
                Console.Error.WriteLine("bakeoff: " + report.Meetings + " meetings, " + report.Matches + " matches, stopped by " + report.StoppedBy + ", report in " + options.Out);
                foreach (DeciderStats d in report.Deciders)
                {
                    Console.Error.WriteLine("bakeoff: " + d.Name + " calls " + d.Calls + " failures " + d.Failures + " ejections correct " + d.EjectionsCorrect + " wrong " + d.EjectionsWrong + " none " + d.NoEjection
                        + " p50 " + d.Percentile(0.5).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " ms p95 " + d.Percentile(0.95).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " ms");
                }
            }

            return 0;
        }

        private static int Serve(HostOptions options, MatchRunner[] runners, Stream stdout)
        {
            Stream stdin = Console.OpenStandardInput();
            int agents = 0;
            foreach (MatchRunner r in runners)
            {
                agents += r.AgentCount;
            }

            int frameSize = runners[0].FrameSize;
            PipeProtocol.WriteHandshake(stdout, ObservationBuilder.Size, ActionCatalogue.Count, agents, ObservationBuilder.SchemaHash);
            Console.Error.WriteLine("host ready: " + runners.Length + " matches, " + agents + " agents, obs " + ObservationBuilder.Size
                + ", actions " + ActionCatalogue.Count + ", threads " + options.Threads + ", learner " + options.Learner);

            var frames = new byte[agents * frameSize];
            var actions = new int[agents];
            var offsets = new int[runners.Length];
            int at = 0;
            for (int i = 0; i < runners.Length; i++)
            {
                offsets[i] = at;
                at += runners[i].AgentCount;
            }

            var po = new ParallelOptions { MaxDegreeOfParallelism = options.Threads };
            Parallel.For(0, runners.Length, po, i => runners[i].WriteFrames(frames.AsSpan(offsets[i] * frameSize, runners[i].AgentCount * frameSize)));
            while (true)
            {
                stdout.Write(frames, 0, frames.Length);
                stdout.Flush();
                if (!PipeProtocol.ReadActions(stdin, actions))
                {
                    Console.Error.WriteLine("trainer closed the pipe, exiting");
                    return 0;
                }

                Parallel.For(0, runners.Length, po, i =>
                {
                    MatchRunner r = runners[i];
                    r.Advance(actions.AsSpan(offsets[i], r.AgentCount));
                    r.WriteFrames(frames.AsSpan(offsets[i] * frameSize, r.AgentCount * frameSize));
                });
            }
        }

        /// <summary>Scripted-vs-scripted with random learner actions; prints throughput to stderr.</summary>
        private static int Bench(HostOptions options, MatchRunner[] runners)
        {
            double seconds = options.BenchSeconds ?? 0;
            int threads = Math.Min(options.Threads, runners.Length);
            var workers = new Thread[threads];
            var stop = Stopwatch.StartNew();
            for (int t = 0; t < threads; t++)
            {
                int index = t;
                workers[t] = new Thread(() =>
                {
                    var rng = new XorShiftRandom(options.Seed * 7919UL + (ulong)index + 1);
                    var mine = new System.Collections.Generic.List<MatchRunner>();
                    for (int i = index; i < runners.Length; i += threads)
                    {
                        mine.Add(runners[i]);
                    }

                    while (stop.Elapsed.TotalSeconds < seconds)
                    {
                        foreach (MatchRunner r in mine)
                        {
                            r.AdvanceRandom(rng);
                        }
                    }
                });
                workers[t].Start();
            }

            foreach (Thread w in workers)
            {
                w.Join();
            }

            double elapsed = stop.Elapsed.TotalSeconds;
            long decisions = 0;
            long forced = 0;
            int finished = 0;
            foreach (MatchRunner r in runners)
            {
                decisions += r.Decisions;
                forced += r.ForcedDecisions;
                finished += r.MatchesFinished;
            }

            Console.Error.WriteLine("bench: " + options.Learner + " learner, " + runners.Length + " matches, " + threads + " threads, "
                + elapsed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " s");
            Console.Error.WriteLine("bench: " + (decisions - forced) + " real decisions of " + decisions + " (" + forced + " forced Stay), "
                + ((decisions - forced) / elapsed).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " real decisions per second (" + (decisions / elapsed).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " frames per second)");
            Console.Error.WriteLine("bench: " + finished + " matches finished, "
                + (finished / elapsed * 60.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " matches per minute");
            return 0;
        }
    }
}
