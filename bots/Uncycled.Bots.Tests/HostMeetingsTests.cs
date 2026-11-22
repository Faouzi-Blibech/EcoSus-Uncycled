using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots.Meeting;
using Uncycled.Server.Content;
using Uncycled.Train;
using Xunit;
using Xunit.Abstractions;

namespace Uncycled.Bots.Tests
{
    /// <summary>Real meetings in the offline runners: chat and votes through the rules-path meeting brain, ejections that pay the learner.</summary>
    public sealed class HostMeetingsTests
    {
        private static readonly string ContentDir = ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
        private readonly ITestOutputHelper _out;

        public HostMeetingsTests(ITestOutputHelper output)
        {
            _out = output;
        }

        [Fact]
        public void EvalMeetings_HaveChatAndVotes()
        {
            ContentBundle content = ContentLoader.Load(ContentDir);
            HostOptions options = HostOptions.Parse(new[] { "--eval", "--content", ContentDir, "--seeds", "1-8", "--threads", "2" });

            IReadOnlyList<string> lines = EvalRunner.Run(options, content, HostBrains.Scripted(new BotsSettings(), content));

            int meetings = 0;
            int chat = 0;
            int cast = 0;
            int missed = 0;
            foreach (string line in lines)
            {
                using (JsonDocument doc = JsonDocument.Parse(line))
                {
                    meetings += doc.RootElement.GetProperty("meetings").GetInt32();
                    chat += doc.RootElement.GetProperty("chat_lines").GetInt32();
                    cast += doc.RootElement.GetProperty("votes_cast").GetInt32();
                    missed += doc.RootElement.GetProperty("votes_missed").GetInt32();
                }
            }

            Assert.True(meetings > 0, "no meeting in 8 matches: the test checks nothing");
            Assert.True(chat > 0, "no chat line in " + meetings + " meetings");
            Assert.True(cast >= meetings, "fewer votes than meetings: " + cast + " in " + meetings);
            Assert.Equal(0, missed);
        }

        [Fact]
        public void HostLearnerSeats_VoteThroughTheMeetingBrain()
        {
            // The meeting brain is a decider the talker asks; this one never speaks and votes for the lowest living seat
            // that is not the asked seat (the host asks it for every learner seat). Whatever the old own vote rule (suspicion alone) would pick, the learner's vote must be that seat.
            var decider = new LowestOtherSeatDecider();
            HostOptions o = HostOptions.Parse(new[] { "--content", ContentDir, "--matches", "8", "--seed", "3", "--threads", "1" });
            ContentBundle content = ContentLoader.Load(ContentDir);
            var r = new MatchRunner(o, 0, content, HostBrains.Scripted(BotsSettings.Load("none"), content), null, decider);
            var rng = new XorShiftRandom(5);
            byte learner = r.AgentSeat(0);
            VoteRecord? mine = null;
            for (int d = 0; d < 6000 && mine == null; d++)
            {
                learner = r.AgentSeat(0); // before the step: a decision that ends the match starts the next one with another seat
                r.AdvanceRandom(rng);
                IReadOnlyList<VoteRecord>? votes = r.LastMeetingVotes;
                if (votes != null && votes.Any(v => v.Voter == learner && !v.Missed))
                {
                    mine = votes.First(v => v.Voter == learner);
                }
            }

            Assert.True(mine != null, "the learner never cast a vote in 6000 decisions");
            Assert.True(decider.Asked > 0, "the meeting brain was never asked for the learner");
            Assert.NotNull(mine!.Value.Target);
            Assert.Equal(decider.VoteOf[learner], mine.Value.Target);
        }

        [Fact]
        public void HostLearnerSeats_TalkInMeetings()
        {
            HostOptions o = HostOptions.Parse(new[] { "--content", ContentDir, "--matches", "8", "--seed", "3", "--threads", "1" });
            ContentBundle content = ContentLoader.Load(ContentDir);
            var r = new MatchRunner(o, 0, content, HostBrains.Scripted(BotsSettings.Load("none"), content), null, new LowestOtherSeatDecider { Talks = true });
            var rng = new XorShiftRandom(5);
            bool spoke = false;
            for (int d = 0; d < 6000 && !spoke; d++)
            {
                byte learner = r.AgentSeat(0);
                r.AdvanceRandom(rng);
                spoke = r.MeetingChat.Acts.Any(a => a.Speaker == learner);
            }

            Assert.True(spoke, "the learner never put a chat line in a meeting in 6000 decisions");
        }

        [Fact]
        public void Ejections_HappenInHostMatches()
        {
            ContentBundle content = ContentLoader.Load(ContentDir);
            HostOptions options = HostOptions.Parse(new[] { "--eval", "--content", ContentDir, "--seeds", "1-100", "--threads", "2" });

            IReadOnlyList<string> lines = EvalRunner.Run(options, content, HostBrains.Scripted(new BotsSettings(), content));

            int right = 0;
            int wrong = 0;
            int meetings = 0;
            foreach (string line in lines)
            {
                using (JsonDocument doc = JsonDocument.Parse(line))
                {
                    right += doc.RootElement.GetProperty("correct_ejections").GetInt32();
                    wrong += doc.RootElement.GetProperty("wrong_ejections").GetInt32();
                    meetings += doc.RootElement.GetProperty("meetings").GetInt32();
                }
            }

            _out.WriteLine("EJECTIONS per 100 scripted matches (seeds 1-100): right " + right + ", wrong " + wrong + ", meetings " + meetings);
            Assert.True(right >= 5, "only " + right + " correct ejections per 100 matches (wrong " + wrong + "): the ejection rewards would give no signal");
        }

        [Fact]
        public void EjectedLearnerTrasher_GetsEjectedReward()
        {
            // learner Trasher with only the ejection weight left (default -0.3): the staged meeting votes it out
            MatchRunner r = TrainHostTests.NewRunner(TrainHostTests.Options("--seed", "8", "--learner", "polluter", "--matches", "8", "--rw-win", "0", "--rw-task", "0", "--rw-report", "0", "--rw-fix", "0", "--rw-hit", "0", "--rw-dump", "0",
                "--rw-repollute", "0", "--rw-victim", "0", "--rw-jam", "0", "--rw-cleanerout", "0", "--rw-ejectright", "0", "--rw-ejectwrong", "0"));
            float total = 0f;
            TrainHostTests.StageAccusation(r, () =>
            {
                byte[] f = TrainHostTests.Frames(r);
                for (int a = 0; a < r.AgentCount; a++)
                {
                    total += TrainHostTests.Reward(r, f, a);
                }
            });

            Assert.True(r.PollutersEjected > 0, "the staged meeting ejected nobody, so the test checked nothing");
            Assert.Equal(-0.3f * r.PollutersEjected, total, 3);
        }

        /// <summary>Stays quiet in the discussion (unless Talks); in the voting stage votes for the lowest living seat other than itself.</summary>
        private sealed class LowestOtherSeatDecider : IMeetingDecider
        {
            public string Name => "test";

            /// <summary>Says "hmm" in the discussion instead of staying quiet.</summary>
            public bool Talks { get; set; }

            public int Asked { get; private set; }

            /// <summary>The seat each asked seat was told to vote for, last answer.</summary>
            public Dictionary<byte, byte> VoteOf { get; } = new Dictionary<byte, byte>();

            public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
            {
                if (state.Stage != MeetingStage.Voting)
                {
                    return Task.FromResult(Talks ? MeetingDecision.Say("test", "hmm") : MeetingDecision.Quiet("test"));
                }

                Asked++;
                byte target = state.Living.Where(s => s != state.Self && !state.Partners.Contains(s)).Min();
                VoteOf[state.Self] = target;
                return Task.FromResult(new MeetingDecision { Vote = target, Source = "test" });
            }
        }
    }
}
