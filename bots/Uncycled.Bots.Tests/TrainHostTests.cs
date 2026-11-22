using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Bots;
using Uncycled.Server.Content;
using Uncycled.Train;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public sealed class TrainHostTests
    {
        private static readonly string ContentDir = ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));

        internal static HostOptions Options(params string[] extra)
        {
            var args = new List<string> { "--content", ContentDir, "--matches", "1", "--seed", "7", "--threads", "1" };
            args.AddRange(extra);
            return HostOptions.Parse(args.ToArray());
        }

        internal static MatchRunner NewRunner(HostOptions o)
        {
            ContentBundle content = ContentLoader.Load(o.ContentDir);
            return new MatchRunner(o, 0, content, HostBrains.Scripted(BotsSettings.Load("none"), content));
        }

        internal static byte[] Frames(MatchRunner r)
        {
            var buf = new byte[r.AgentCount * r.FrameSize];
            r.WriteFrames(buf);
            return buf;
        }

        internal static float Reward(MatchRunner r, byte[] frames, int agent)
        {
            return BitConverter.ToSingle(frames, agent * r.FrameSize + r.RewardOffset);
        }

        private static bool Done(MatchRunner r, byte[] frames, int agent) => frames[agent * r.FrameSize + r.DoneOffset] != 0;

        /// <summary>Picks the k-th valid action from each agent's mask: deterministic, always valid.</summary>
        private static int[] PickActions(MatchRunner r, byte[] frames, int decision)
        {
            var actions = new int[r.AgentCount];
            for (int a = 0; a < r.AgentCount; a++)
            {
                int maskAt = a * r.FrameSize + r.MaskOffset;
                var valid = new List<int>();
                for (int i = 0; i < PolicyActionsV2.Count; i++)
                {
                    if (frames[maskAt + i] != 0)
                    {
                        valid.Add(i);
                    }
                }

                actions[a] = valid[(decision * 7 + a * 3) % valid.Count];
            }

            return actions;
        }

        [Fact]
        public void Host_SameSeedSameMatch()
        {
            MatchRunner a = NewRunner(Options());
            MatchRunner b = NewRunner(Options());
            Assert.Equal(6, a.AgentCount);
            for (int d = 0; d < 200; d++)
            {
                byte[] fa = Frames(a);
                byte[] fb = Frames(b);
                Assert.Equal(fa, fb);
                int[] actions = PickActions(a, fa, d);
                a.Advance(actions);
                b.Advance(actions);
            }
        }

        [Fact]
        public void Host_DoneThenNewMatch()
        {
            MatchRunner r = NewRunner(Options("--rw-win", "1", "--rw-task", "0", "--rw-report", "0", "--rw-fix", "0", "--rw-hit", "0", "--rw-dump", "0", "--rw-ejected", "0", "--rw-ejectright", "0", "--rw-ejectwrong", "0", "--rw-deliver", "0", "--rw-spot", "0", "--rw-heal", "0", "--rw-out", "0", "--rw-cleanerout", "0", "--rw-repollute", "0", "--rw-victim", "0", "--rw-jam", "0"));
            int agents = r.AgentCount;
            ulong firstSeed = r.Seed;
            bool sawDone = false;
            for (int d = 0; d < 6000 && !sawDone; d++)
            {
                byte[] f = Frames(r);
                if (Done(r, f, 0))
                {
                    sawDone = true;
                    for (int a = 0; a < agents; a++)
                    {
                        Assert.True(Done(r, f, a));
                        Assert.Equal(1f, Math.Abs(Reward(r, f, a)));
                    }

                    // citizens learn, so all agents are on the same team and share the sign
                    Assert.Equal(Reward(r, f, 0), Reward(r, f, agents - 1));
                    break;
                }

                for (int a = 0; a < agents; a++)
                {
                    Assert.Equal(0f, Reward(r, f, a));
                }

                r.Advance(PickActions(r, f, d));
            }

            Assert.True(sawDone, "a match must end within 6000 decisions");
            Assert.Equal(1, r.MatchesFinished);
            Assert.NotEqual(firstSeed, r.Seed);
            Assert.Equal(agents, r.AgentCount);

            // the done frame already shows the fresh match; one more decision there is playable and not done
            byte[] doneFrame = Frames(r);
            r.Advance(PickActions(r, doneFrame, 0));
            Assert.False(Done(r, Frames(r), 0));
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Host_TaskStepRewardsTheDoer()
        {
            MatchRunner r = NewRunner(Options("--rw-win", "0", "--rw-task", "1", "--rw-report", "0", "--rw-fix", "0", "--rw-hit", "0", "--rw-dump", "0", "--rw-ejected", "0"));
            float total = 0f;
            for (int d = 0; d < 400; d++)
            {
                byte[] f = Frames(r);
                for (int a = 0; a < r.AgentCount; a++)
                {
                    total += Reward(r, f, a);
                }

                var actions = new int[r.AgentCount];
                for (int a = 0; a < r.AgentCount; a++)
                {
                    int maskAt = a * r.FrameSize + r.MaskOffset;
                    actions[a] = f[maskAt + (int)BotAction.UseHere] != 0 ? (int)BotAction.UseHere
                        : f[maskAt + (int)BotAction.GoNextTask] != 0 ? (int)BotAction.GoNextTask
                        : (int)BotAction.Stay;
                }

                r.Advance(actions);
            }

            Assert.True(total >= 1f, "a citizen that walks to its task and uses it must earn the step reward, total=" + total);
        }

        [Fact]
        public void Host_InvalidActionActsAsStay()
        {
            // out of range, and in range but masked (a Trasher-only action for a Cleaner learner): both count and run as Stay
            int blocked = Array.IndexOf(PolicyActionsV2.Names.ToArray(), "SabotageSmog");
            foreach (int bad in new[] { 9999, -1, blocked })
            {
                MatchRunner r = NewRunner(Options());
                byte[] f = Frames(r);
                var actions = new int[r.AgentCount];
                for (int a = 0; a < actions.Length; a++)
                {
                    actions[a] = bad;
                    if (bad == blocked)
                    {
                        Assert.Equal(0, f[a * r.FrameSize + r.MaskOffset + blocked]);
                    }
                }

                r.Advance(actions);
                Assert.True(r.InvalidActions == r.AgentCount, "index " + bad + ": invalid actions " + r.InvalidActions);
                for (int a = 0; a < r.AgentCount; a++)
                {
                    Assert.Equal(BotAction.Stay, r.AgentExecutor(a).Current);
                }

                Assert.Equal(r.AgentCount * r.FrameSize, Frames(r).Length);
            }
        }

        [Fact]
        public void Protocol_RoundTrip()
        {
            const int obs = 5;
            const int actions = 3;
            var ms = new MemoryStream();
            string hash = new string('a', 64);
            PipeProtocol.WriteHandshake(ms, obs, actions, 4, hash);
            Assert.Equal(4 + 12 + 64, ms.Length);
            ms.Position = 0;
            Handshake h = PipeProtocol.ReadHandshake(ms);
            Assert.Equal(obs, h.ObsSize);
            Assert.Equal(actions, h.ActionCount);
            Assert.Equal(4, h.AgentCount);
            Assert.Equal(hash, h.SchemaHash);

            float[] o = { 0.5f, -1f, 0f, 1f, 0.25f };
            bool[] mask = { true, false, true };
            var frame = new byte[PipeProtocol.FrameSize(obs, actions)];
            Assert.Equal(obs * 4 + actions + 5, frame.Length);
            PipeProtocol.WriteFrame(frame, o, mask, -0.3f, true);
            Assert.Equal(BitConverter.GetBytes(0.5f), frame[0..4]);
            Frame back = PipeProtocol.ReadFrame(frame, obs, actions);
            Assert.Equal(o, back.Obs);
            Assert.Equal(mask, back.Mask);
            Assert.Equal(-0.3f, back.Reward);
            Assert.True(back.Done);

            var ms2 = new MemoryStream();
            PipeProtocol.WriteActions(ms2, new[] { 3, 0, 29, -1 });
            Assert.Equal(16, ms2.Length);
            ms2.Position = 0;
            var read = new int[4];
            Assert.True(PipeProtocol.ReadActions(ms2, read));
            Assert.Equal(new[] { 3, 0, 29, -1 }, read);
            Assert.False(PipeProtocol.ReadActions(ms2, read));
        }

        [Fact]
        public void Options_ParseDefaultsAndFlags()
        {
            HostOptions o = HostOptions.Parse(new[] { "--content", "c", "--learner", "polluter", "--matches", "4", "--rw-ejected", "-0.5" });
            Assert.Equal(Role.Polluter, o.Learner);
            Assert.Equal(4, o.Matches);
            Assert.Equal(1UL, o.Seed);
            Assert.Equal(-0.5f, o.Rewards.Ejected);
            Assert.Equal(0.05f, o.Rewards.Task);
            Assert.Throws<ArgumentException>(() => HostOptions.Parse(new[] { "--nope", "1" }));
        }

        [Fact]
        public void Eval_OneJsonLinePerSeedAndThreadCountDoesNotMatter()
        {
            HostOptions one = HostOptions.Parse(new[] { "--eval", "--content", ContentDir, "--seeds", "3-5", "--threads", "1" });
            HostOptions two = HostOptions.Parse(new[] { "--eval", "--content", ContentDir, "--seeds", "3-5", "--threads", "3" });
            ContentBundle content = ContentLoader.Load(ContentDir);
            Func<Role, IRandom, IBotBrain> brains = HostBrains.Scripted(BotsSettings.Load("none"), content);
            IReadOnlyList<string> a = EvalRunner.Run(one, content, brains);
            IReadOnlyList<string> b = EvalRunner.Run(two, content, brains);
            Assert.Equal(3, a.Count);
            Assert.Equal(a, b);
            foreach (string line in a)
            {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(line);
                System.Text.Json.JsonElement root = doc.RootElement;
                string winner = root.GetProperty("winner").GetString()!;
                Assert.True(winner == "citizens" || winner == "polluters", winner);
                Assert.False(string.IsNullOrEmpty(root.GetProperty("reason").GetString()));
                Assert.True(root.GetProperty("tasks_done").GetDouble() >= 0);
                Assert.True(root.GetProperty("meter").GetDouble() >= 0);
                Assert.True(root.GetProperty("correct_ejections").GetDouble() >= 0);
            }
        }

        [Fact]
        public void Host_NeverHandsBackDuringMeetingOrEjection()
        {
            MatchRunner r = NewRunner(Options("--matches", "4"));
            var rng = new XorShiftRandom(11);
            for (int d = 0; d < 2000; d++)
            {
                r.AdvanceRandom(rng);
                Assert.Equal(MatchPhase.Playing, r.Phase);
            }

            Assert.True(r.MeetingsHeld > 0, "the run must contain meetings for this test to mean anything");
        }

        [Fact]
        public void Host_MostDecisionsAreRealChoices()
        {
            MatchRunner r = NewRunner(Options("--matches", "4"));
            var rng = new XorShiftRandom(12);
            for (int d = 0; d < 1500; d++)
            {
                r.AdvanceRandom(rng);
            }

            double forced = r.ForcedDecisions / (double)r.Decisions;
            Assert.True(forced < 0.35, "forced Stay share " + forced);
        }

        [Fact]
        public void Host_RewardsFromAMeetingReachTheNextFrame()
        {
            // learner Trasher: the only reward left is the ejection penalty, which is paid inside a meeting.
            // v2 premise: a random learner is never accused by itself, so the test stages the accusation (see StageAccusation).
            MatchRunner r = NewRunner(Options("--seed", "8", "--learner", "polluter", "--matches", "8", "--rw-win", "0", "--rw-task", "0", "--rw-report", "0", "--rw-fix", "0", "--rw-hit", "0", "--rw-dump", "0", "--rw-ejected", "-1",
                "--rw-repollute", "0", "--rw-victim", "0", "--rw-jam", "0", "--rw-cleanerout", "0", "--rw-ejectright", "0", "--rw-ejectwrong", "0"));
            float total = 0f;
            StageAccusation(r, () =>
            {
                byte[] f = Frames(r);
                for (int a = 0; a < r.AgentCount; a++)
                {
                    total += Reward(r, f, a);
                }
            });

            Assert.Equal(-(float)r.PollutersEjected, total, 3);
            Assert.True(r.PollutersEjected > 0, "the staged meeting ejected nobody, so the test checked nothing");
        }

        [Fact]
        public void Host_MeetingResultsAndAccusationsReachTheLearnersHeat()
        {
            // v2 premise: nobody hits or dumps and a v1-action learner never re-pollutes, so the test stages it.
            // The learner Trasher re-pollutes a clean spot in sight of a scripted Cleaner (a witness record), the
            // Cleaner calls the meeting, and its talker (the rule decider, as in the live room) accuses the learner
            // and votes for it. The meeting result and the accusation must reach the learner's heat input.
            MatchRunner r = NewRunner(Options("--learner", "polluter", "--matches", "8", "--seed", "8"));
            ContentBundle content = ContentLoader.Load(ContentDir);
            var rng = new XorShiftRandom(21);
            float ready = content.Config.EmergencyMinSinceMeetingS + 2f;
            byte witness = 255;
            foreach (PlayerState p in r.Engine.Players)
            {
                if (witness == 255 && p.Role == Role.Citizen)
                {
                    witness = p.Seat;
                }
            }

            // the random learner may have turned the smog on (vision 1): wait for a clear moment to stage the act
            // it must not call an emergency meeting by chance meanwhile: the match has two calls and the staged act needs one
            int callMeeting = Array.IndexOf(PolicyActionsV2.Names.ToArray(), "CallMeeting");
            for (int d = 0; d < 4000 && (r.Engine.ActiveTime < ready || r.Engine.Smog.Active || r.Engine.ViewFor(witness).Me.Vision < 3f); d++)
            {
                r.AdvanceRandom(rng, callMeeting);
            }

            Assert.True(r.Engine.ViewFor(witness).Me.Vision >= 3f, "no clear moment to stage the act");

            Assert.Equal(0, r.MeetingsHeld); // nothing staged yet: the learner has not been accused
            MatchEngine e = r.Engine;
            byte learner = r.AgentSeat(0);
            BotMemory heat = r.AgentMemory(0);
            Assert.Equal(0f, heat.HeatAccusations(learner));
            Assert.Equal(0f, heat.HeatOf(learner));

            Sim.World.ConsoleDef spot = content.Map.ConsolesOfKind(ConsoleKind.Task)[0];
            e.Spots.Find(spot.Id)!.State = SpotState.Clean;
            e.TestTeleport(learner, spot.Position);
            e.TestTeleport(witness, NearAndInSight(content.Map, spot.Position));
            Assert.True(e.Apply(learner, new RepolluteCommand(spot.Id)).IsOk);

            r.AdvanceRandom(rng); // the Cleaner's brain takes the record into its memory
            Assert.NotEmpty(e.Witnesses.SeenBy(witness)); // the scripted Cleaner saw it
            e.TestTeleport(witness, content.Map.ConsolesOfKind(ConsoleKind.Emergency)[0].Position);
            Assert.True(e.Apply(witness, new CallMeetingCommand()).IsOk);

            for (int d = 0; d < 4000 && !(r.MeetingsHeld >= 1 && r.Phase == MatchPhase.Playing); d++)
            {
                r.AdvanceRandom(rng);
            }

            Assert.True(r.MeetingsHeld >= 1, "the staged meeting did not finish");
            Assert.True(heat.HeatAccusations(learner) > 0f, "the accusation did not reach the learner's heat");
            Assert.True(heat.HeatOf(learner) > 0f, "the learner's heat did not rise");
        }

        /// <summary>
        /// Stages the one thing a random learner Trasher never does by itself: the learner re-pollutes a clean spot in sight of a
        /// scripted Cleaner, which calls the meeting, accuses and votes. Plays on (random learner actions) until that meeting is over;
        /// onStep runs after every decision. Ends with no meeting resolved if the staging did not work: callers assert what they need.
        /// </summary>
        internal static void StageAccusation(MatchRunner r, Action onStep)
        {
            ContentBundle content = ContentLoader.Load(ContentDir);
            var rng = new XorShiftRandom(21);
            float ready = content.Config.EmergencyMinSinceMeetingS + 2f;
            byte witness = 255;
            foreach (PlayerState p in r.Engine.Players)
            {
                if (witness == 255 && p.Role == Role.Citizen)
                {
                    witness = p.Seat;
                }
            }

            // it must not call an emergency meeting by chance meanwhile: the match has two calls and the staged act needs one
            int callMeeting = Array.IndexOf(PolicyActionsV2.Names.ToArray(), "CallMeeting");
            for (int d = 0; d < 4000 && (r.Engine.ActiveTime < ready || r.Engine.Smog.Active || r.Engine.ViewFor(witness).Me.Vision < 3f); d++)
            {
                r.AdvanceRandom(rng, callMeeting);
                onStep();
            }

            Assert.True(r.Engine.ViewFor(witness).Me.Vision >= 3f, "no clear moment to stage the act");
            MatchEngine e = r.Engine;
            byte learner = r.AgentSeat(0);
            Sim.World.ConsoleDef spot = content.Map.ConsolesOfKind(ConsoleKind.Task)[0];
            e.Spots.Find(spot.Id)!.State = SpotState.Clean;
            e.TestTeleport(learner, spot.Position);
            e.TestTeleport(witness, NearAndInSight(content.Map, spot.Position));
            Assert.True(e.Apply(learner, new RepolluteCommand(spot.Id)).IsOk);

            r.AdvanceRandom(rng);
            onStep();
            Assert.NotEmpty(e.Witnesses.SeenBy(witness));
            e.TestTeleport(witness, content.Map.ConsolesOfKind(ConsoleKind.Emergency)[0].Position);
            Assert.True(e.Apply(witness, new CallMeetingCommand()).IsOk);

            int held = r.MeetingsHeld;
            for (int d = 0; d < 4000 && !(r.MeetingsHeld > held && r.Phase == MatchPhase.Playing); d++)
            {
                r.AdvanceRandom(rng);
                onStep();
            }

            Assert.True(r.MeetingsHeld > held, "the staged meeting did not finish");
        }

        private static Vec2 NearAndInSight(Sim.World.MapData map, Vec2 spot)
        {
            MapDistances dist = MapDistances.For(map);
            for (int i = 0; i < 16; i++)
            {
                double a = 2 * Math.PI * i / 16;
                Vec2 cell = dist.NearestWalkable(new Vec2(spot.X + (float)Math.Cos(a) * 2f, spot.Y + (float)Math.Sin(a) * 2f));
                if (Vec2.Distance(cell, spot) <= 2.6f && map.Sight.HasLineOfSight(cell, spot))
                {
                    return cell;
                }
            }

            return dist.NearestWalkable(spot);
        }

        [Fact]
        public void Host_EjectedSeatIsKnownOutByEveryLearner()
        {
            MatchRunner r = NewRunner(Options("--seed", "8", "--learner", "polluter", "--matches", "8"));
            StageAccusation(r, () => { });

            Assert.True(r.Ejections > 0 && r.LastEjectedSeat != null, "the staged meeting ejected nobody, so the test checked nothing");
            byte ejected = r.LastEjectedSeat!.Value;
            int others = 0;
            for (int a = 0; a < r.AgentCount; a++)
            {
                if (r.AgentSeat(a) == ejected)
                {
                    continue;
                }

                Assert.True(r.AgentMemory(a).IsKnownOut(ejected), "agent " + a + " does not know seat " + ejected + " was ejected");
                others++;
            }

            Assert.True(others > 0, "no other learner to check");
        }

        [Fact]
        public void Host_DumpSeenOnlyByThePartnerCountsAsUnseen()
        {
            TestMatch m = TestMatch.Create(3);
            var polluters = new List<byte>();
            byte citizen = 255;
            foreach (PlayerState p in m.Engine.Players)
            {
                if (p.Role == Role.Polluter)
                {
                    polluters.Add(p.Seat);
                }
                else
                {
                    citizen = p.Seat;
                }
            }

            var pos = new Vec2(1f, 1f);
            var partnerOnly = new CueEvent(new[] { polluters[1] }, CueKind.Dump, polluters[0], pos);
            var withCitizen = new CueEvent(new[] { polluters[1], citizen }, CueKind.Dump, polluters[0], pos);
            Assert.False(MatchRunner.CueCountsAsSeen(partnerOnly, m.Engine));
            Assert.True(MatchRunner.CueCountsAsSeen(withCitizen, m.Engine));
        }

        [Fact]
        public void Options_BotsConfigExplicitAndMissing()
        {
            string path = Path.Combine(Path.GetTempPath(), "uncycled-bots-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{ \"decision_interval_s\": 1.0, \"follow_cap_s\": 9 }");
            try
            {
                HostOptions explicitOpts = HostOptions.Parse(new[] { "--bots", path });
                Assert.Equal(1.0f, explicitOpts.Settings.DecisionIntervalS);
                Assert.Equal(9f, explicitOpts.Settings.FollowCapS);
            }
            finally
            {
                File.Delete(path);
            }

            var warnings = new List<string>();
            HostOptions missing = HostOptions.Parse(new[] { "--bots", path });
            missing.Warn = warnings.Add;
            Assert.Equal(0.5f, missing.Settings.DecisionIntervalS);
            Assert.Single(warnings);
            Assert.Contains("bots.json", warnings[0]);
        }

        [Fact]
        public void Host_DecisionIntervalComesFromBotsJson()
        {
            string path = Path.Combine(Path.GetTempPath(), "uncycled-bots-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{ \"decision_interval_s\": 1.0 }");
            try
            {
                MatchRunner r = NewRunner(Options("--bots", path));
                float t0 = r.MatchTime;
                r.Advance(PickActions(r, Frames(r), 0));
                Assert.Equal(1.0f, r.MatchTime - t0, 2);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Eval_SeedsStayClearOfTrainingSeeds()
        {
            Assert.True(HostOptions.EvalSeedStart >= 100000UL);
        }
    }
}
