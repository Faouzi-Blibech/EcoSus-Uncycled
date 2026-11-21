using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots;
using Uncycled.Bots.Meeting;
using Uncycled.Bots.Neural;
using Uncycled.Server.Bots;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>
    /// Bots talk and vote in the meetings of a real room played in fast-forward (1 human who never acts,
    /// 7 bots with the scripted brains). A meeting is whatever the bots start by themselves.
    /// </summary>
    public class RoomMeetingTalkTests
    {
        /// <summary>Never answers; the talker must not wait for it.</summary>
        private sealed class NeverDecider : IMeetingDecider
        {
            public string Name => "never";

            public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct) =>
                new TaskCompletionSource<MeetingDecision>().Task;
        }

        private sealed class Probe
        {
            public readonly Dictionary<byte, int> ChatLines = new Dictionary<byte, int>();
            public readonly HashSet<byte> LivingBotsAtVote = new HashSet<byte>();
            public readonly HashSet<byte> Voted = new HashSet<byte>();
            public double MeetingSeconds;
            public int BotChat => ChatLines.Values.Sum();
        }

        private static RoomRig NewRig(MeetingKit? kit)
        {
            return new RoomRig(c => new BotBrains(new BotsSettings(), RoomRig.EmptyModelsDir(), c.Tasks, new CountingLog(), meeting: kit));
        }

        /// <summary>Runs until the bots start a meeting, then through it; reads each bot's own view like a client would.</summary>
        private static Probe RunOneMeeting(RoomRig rig)
        {
            rig.JoinAndStart();
            MatchEngine e = rig.Engine;
            int maxSteps = (int)((rig.Content.Config.DeadlineS * 3f + 300f) / RoomRig.Dt);
            for (int i = 0; i < maxSteps && e.Phase != MatchPhase.Meeting && e.Phase != MatchPhase.Ended; i++)
            {
                rig.Step();
            }

            Assert.Equal(MatchPhase.Meeting, e.Phase);
            float start = e.MatchTime;
            var probe = new Probe();
            for (int i = 0; i < 3000 && e.Phase == MatchPhase.Meeting; i++)
            {
                rig.Step();
                foreach (PlayerState p in e.Players)
                {
                    if (p.Seat == 0 || p.Status != PlayerStatus.Active)
                    {
                        continue;
                    }

                    MeetingView? m = e.ViewFor(p.Seat).Meeting;
                    if (m == null)
                    {
                        continue;
                    }

                    probe.ChatLines[p.Seat] = Math.Max(probe.ChatLines.GetValueOrDefault(p.Seat), m.MyChatCount);
                    if (m.Stage == MeetingStage.Voting)
                    {
                        probe.LivingBotsAtVote.Add(p.Seat);
                        if (m.MyVoteCast)
                        {
                            probe.Voted.Add(p.Seat);
                        }
                    }
                }
            }

            probe.MeetingSeconds = e.MatchTime - start;
            return probe;
        }

        [Fact]
        public void Room_BotsChatAndVoteInAMeeting()
        {
            var lines = new ConcurrentQueue<string>();
            using var kit = new MeetingKit(new RuleDecider(), new MeetingDecisionLog(lines.Enqueue));
            RoomRig rig = NewRig(kit);

            Probe p = RunOneMeeting(rig);

            Assert.True(p.BotChat >= 3, "bot chat lines in the discussion: " + p.BotChat);
            Assert.NotEmpty(p.LivingBotsAtVote);
            Assert.Equal(p.LivingBotsAtVote.OrderBy(s => s), p.Voted.OrderBy(s => s));

            // the room fed its chat log from the engine's ChatEvents: the bots read what was said
            Assert.NotNull(rig.Room.MeetingChat);
            Assert.True(rig.Room.MeetingChat!.Acts.Count >= p.BotChat, "log acts " + rig.Room.MeetingChat.Acts.Count);

            // each sent decision was logged once: one line per chat line and one per vote
            string[] voteLines = lines.Where(l => l.Contains("\"stage\":\"voting\"")).ToArray();
            Assert.Equal(p.Voted.Count, voteLines.Length);
        }

        [Fact]
        public async Task Room_MeetingNeverWaitsForTheModel()
        {
            var lines = new ConcurrentQueue<string>();
            using var kit = new MeetingKit(new NeverDecider(), new MeetingDecisionLog(lines.Enqueue));
            RoomRig rig = NewRig(kit);
            MatchConfig cfg = rig.Content.Config;

            Probe? p = null;
            Task run = Task.Run(() => p = RunOneMeeting(rig));
            Task first = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(60)));
            Assert.True(first == run, "a room tick waited for a decider that never answers");
            await run;

            Assert.True(p!.MeetingSeconds <= cfg.DiscussionS + cfg.VotingS + cfg.ResultsS + 2f, "the meeting ran " + p.MeetingSeconds + " s");
            Assert.NotEmpty(p.LivingBotsAtVote);
            Assert.Equal(p.LivingBotsAtVote.OrderBy(s => s), p.Voted.OrderBy(s => s));
            Assert.All(lines, l => Assert.Contains("\"source\":\"rule\"", l));
            Assert.Contains(lines, l => l.Contains("\"fallback\":\"late\""));
        }

        [Fact]
        public void Server_shutdown_disposes_the_decision_file_and_the_room_ticks_never_do()
        {
            string path = Path.Combine(Path.GetTempPath(), "uncycled-meeting-" + Guid.NewGuid().ToString("N") + ".log");
            try
            {
                var kit = new MeetingKit(new RuleDecider(), MeetingDecisionLog.ToFile(path));
                RoomRig rig = NewRig(kit);
                RunOneMeeting(rig);

                // the room is still ticking and its tick did not close the log: a later line is still accepted
                kit.Log.Note("written after the meeting");
                rig.Brains.Dispose(); // what the host does at shutdown

                string text = File.ReadAllText(path);
                Assert.Contains("\"stage\":\"voting\"", text);
                Assert.Contains("written after the meeting", text);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
