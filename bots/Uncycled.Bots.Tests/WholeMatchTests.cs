using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Xunit;
using Xunit.Abstractions;

namespace Uncycled.Bots.Tests
{
    public class WholeMatchTests
    {
        private sealed class Outcome
        {
            public ulong Seed;
            public bool Ended;
            public Winner? Winner;
            public string Reason = "";
            public float EndTime;
            public int Dumps;
            public int Hits;
            public int Meetings;
            public List<int> StepsPerCitizen = new List<int>();
            public Dictionary<string, int> Rejected = new Dictionary<string, int>();
        }

        private static readonly Lazy<List<Outcome>> All = new Lazy<List<Outcome>>(() =>
        {
            var list = new List<Outcome>();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                list.Add(Play(seed));
            }

            return list;
        });

        private readonly ITestOutputHelper _out;

        public WholeMatchTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static Outcome Play(ulong seed)
        {
            TestMatch m = TestMatch.Create(seed, startPlaying: false);
            var rig = new BrainRig(m, seed);
            float limit = m.Config.DeadlineS * 3f + 300f; // the deadline counts active play only, not meetings
            int max = (int)(limit / TestMatch.Dt) + 200;
            for (int i = 0; i < max && m.Engine.Phase != MatchPhase.Ended; i++)
            {
                rig.Step();
            }

            var o = new Outcome { Seed = seed, Ended = m.Engine.Phase == MatchPhase.Ended };
            if (m.Engine.Result != null)
            {
                o.Winner = m.Engine.Result.Winner;
                o.Reason = WireNames.Of(m.Engine.Result.Reason);
                o.EndTime = m.Engine.Result.MatchTime;
            }

            o.Dumps = m.Events.Count(e => e is DumpOkEvent);
            o.Hits = m.Events.Count(e => e is HitOkEvent);
            o.Meetings = m.Events.Count(e => e is MeetingStartedEvent);
            foreach (PlayerState p in m.Engine.Players.Where(p => p.Role == Role.Citizen))
            {
                o.StepsPerCitizen.Add(m.Events.Count(e => e is TaskProgressEvent t && t.Seat == p.Seat));
            }

            foreach (SentCommand c in rig.Sent.Where(c => !c.Result.IsOk))
            {
                string key = c.Command.GetType().Name + (c.Command is FixCommand f ? "(" + f.ConsoleId + "," + f.Holding + ")" : "") + ":" + c.Result;
                o.Rejected[key] = o.Rejected.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            return o;
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void WholeMatch_EightScriptedBotsReachGameOver()
        {
            foreach (Outcome o in All.Value)
            {
                _out.WriteLine("seed " + o.Seed + ": " + (o.Ended ? o.Winner + " by " + o.Reason + " at " + o.EndTime.ToString("0") + " s" : "NOT ENDED")
                    + ", dumps " + o.Dumps + ", hits " + o.Hits + ", meetings " + o.Meetings
                    + ", steps/citizen " + string.Join("/", o.StepsPerCitizen)
                    + (o.Rejected.Count > 0 ? ", rejected " + string.Join(" ", o.Rejected.Select(kv => kv.Key + "x" + kv.Value)) : ""));
            }

            Assert.All(All.Value, o =>
            {
                Assert.True(o.Ended, "seed " + o.Seed + " did not end");
                Assert.NotNull(o.Winner);
            });
            Assert.Contains(All.Value, o => o.Reason == "tasks_done" || o.Reason == "polluters_voted_out");
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void WholeMatch_PollutersCannotWinByStallingToTheDeadline()
        {
            // The deadline runs on active play time: meetings and ejections no longer count. Before that
            // change scripted polluters won 20 of 500 seeds (4.0 %), all of them by "deadline" after
            // stalling in meetings; with it they win about 1 in 500 (by pollution_full), so a polluter
            // win is no longer expected among 20 seeds and is not asserted here.
            Assert.DoesNotContain(All.Value, o => o.Reason == "deadline");
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Citizen_DoesTasks()
        {
            List<int> steps = All.Value.SelectMany(o => o.StepsPerCitizen).OrderBy(x => x).ToList();
            double median = steps[steps.Count / 2];
            _out.WriteLine("median task steps per citizen: " + median);
            Assert.True(median > 0);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Polluter_DumpsAndHits()
        {
            Assert.True(All.Value.Sum(o => o.Dumps) > 0);
            Assert.True(All.Value.Sum(o => o.Hits) > 0);
        }
    }
}
