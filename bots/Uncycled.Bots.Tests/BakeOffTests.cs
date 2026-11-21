using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core;
using Uncycled.Bots;
using Uncycled.Bots.Meeting;
using Uncycled.Server.Content;
using Uncycled.Train;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public class BakeOffTests
    {
        private const ulong FirstSeed = 100000;

        private static ContentBundle Content()
        {
            return ContentLoader.Load(ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR")));
        }

        private static BakeOffOptions Options(int meetings, int maxCalls = 1000)
        {
            return new BakeOffOptions { Meetings = meetings, SeedFrom = FirstSeed, SeedTo = FirstSeed + 40, MaxCalls = maxCalls };
        }

        /// <summary>Knows the hidden roles (the bake-off names each state "bakeoff-seed" and builds the engine from that seed) and always votes a living polluter.</summary>
        private sealed class TruthDecider : IMeetingDecider
        {
            public string Name => "truth";

            public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
            {
                ulong seed = ulong.Parse(state.MatchId.Substring("bakeoff-".Length));
                var engine = TestMatch.Create(seed, startPlaying: false).Engine;
                byte? polluter = null;
                foreach (byte seat in state.Living)
                {
                    if (seat != state.Self && engine.Find(seat)!.Role == Role.Polluter)
                    {
                        polluter = seat;
                        break;
                    }
                }

                return Task.FromResult(polluter == null
                    ? new MeetingDecision { SkipVote = true, Source = Name }
                    : new MeetingDecision { Vote = polluter, Source = Name });
            }
        }

        private sealed class Throwing : IMeetingDecider
        {
            public string Name => "throws";

            public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct) => throw new InvalidOperationException("down");
        }

        private static DeciderStats Of(BakeOffReport r, string name) => r.Deciders.Single(d => d.Name == name);

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void BakeOff_CountsCorrectAndWrongEjectionsPerDecider()
        {
            BakeOffReport r = BakeOff.Run(Content(), BotsSettings.Load("none"), Options(6), new IMeetingDecider[] { new TruthDecider(), new SkipDecider() });

            Assert.Equal(6, r.Meetings);
            Assert.Equal("meetings", r.StoppedBy);

            DeciderStats truth = Of(r, "truth");
            Assert.Equal(6, truth.EjectionsCorrect);
            Assert.Equal(0, truth.EjectionsWrong);
            Assert.Equal(0, truth.NoEjection);
            Assert.True(truth.CitizenVotes.Total > 0);
            Assert.Equal(1.0, truth.CitizenVotes.Share(truth.CitizenVotes.Polluter));
            Assert.Equal(0, truth.Failures);

            DeciderStats skip = Of(r, "skip");
            Assert.Equal(0, skip.EjectionsCorrect);
            Assert.Equal(0, skip.EjectionsWrong);
            Assert.Equal(6, skip.NoEjection);
            Assert.Equal(1.0, skip.CitizenVotes.Share(skip.CitizenVotes.Skip));

            // both were asked the same bots at the same vote stages
            Assert.Equal(truth.Calls, skip.Calls);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void BakeOff_StopsBeforeAMeetingThatWouldPassTheCallBudget()
        {
            BakeOffReport r = BakeOff.Run(Content(), BotsSettings.Load("none"), Options(50, maxCalls: 10), new IMeetingDecider[] { new SkipDecider() });

            Assert.Equal("max_calls", r.StoppedBy);
            Assert.InRange(Of(r, "skip").Calls, 1, 10);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void BakeOff_ADeciderThatThrowsCountsAsFailuresAndSkips()
        {
            BakeOffReport r = BakeOff.Run(Content(), BotsSettings.Load("none"), Options(2), new IMeetingDecider[] { new Throwing(), new SkipDecider() });

            DeciderStats t = Of(r, "throws");
            Assert.True(t.Calls > 0);
            Assert.Equal(t.Calls, t.Failures);
            Assert.Equal(t.Calls, t.CitizenVotes.Skip + t.PolluterVotes.Skip);
            Assert.Equal(0, Of(r, "skip").Failures);
        }

        [Fact]
        public void BakeOff_LatencyPercentilesUseTheNearestRank()
        {
            var s = new DeciderStats("x");
            for (int i = 1; i <= 100; i++)
            {
                s.LatenciesMs.Add(i);
            }

            Assert.Equal(50.0, s.Percentile(0.5));
            Assert.Equal(95.0, s.Percentile(0.95));
            Assert.Equal(0.0, new DeciderStats("empty").Percentile(0.5));
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void BakeOff_ReportJsonCarriesTheTableTheGateNeeds()
        {
            BakeOffReport r = BakeOff.Run(Content(), BotsSettings.Load("none"), Options(2), new IMeetingDecider[] { new SkipDecider() });

            using (JsonDocument doc = JsonDocument.Parse(r.ToJson()))
            {
                JsonElement root = doc.RootElement;
                Assert.Equal(2, root.GetProperty("meetings").GetInt32());
                JsonElement d = root.GetProperty("deciders")[0];
                Assert.Equal("skip", d.GetProperty("name").GetString());
                foreach (string key in new[] { "calls", "call_failures", "ejections_correct", "ejections_wrong", "no_ejection", "latency_ms_p50", "latency_ms_p95" })
                {
                    Assert.True(d.TryGetProperty(key, out _), key);
                }

                Assert.True(d.GetProperty("citizen_votes").TryGetProperty("share_polluter", out _));
            }
        }
    }
}
