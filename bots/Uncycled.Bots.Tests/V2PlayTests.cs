using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Bots.Meeting;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>The scripted bots play the v2 rules: clean spots, deliver, upgrade, heal, fix the machine; Trashers re-pollute and use an ability.</summary>
    public class V2PlayTests
    {
        private const float PlaySeconds = 180f;

        private static int Count<T>(IEnumerable<GameEvent> events) where T : GameEvent => events.Count(e => e is T);

        /// <summary>Only one seat thinks (the others stand at their spawns): lets a test set up one situation.</summary>
        private static BrainRig OneSeat(TestMatch m, byte seat, ulong seed = 5)
        {
            var rig = new BrainRig(m, seed);
            for (byte s = 0; s < m.Config.Seats; s++)
            {
                if (s != seat)
                {
                    rig.Skip.Add(s);
                }
            }

            return rig;
        }

        // ---------- a whole round ----------

        private sealed class Round
        {
            public TestMatch Match = null!;
            public List<SentCommand> Sent = null!;
            public MatchEngine Engine => Match.Engine;
        }

        private static Round PlayFor(ulong seed, float seconds)
        {
            TestMatch m = TestMatch.Create(seed, startPlaying: false);
            var rig = new BrainRig(m, seed);
            int max = (int)(seconds / TestMatch.Dt);
            for (int i = 0; i < max && m.Engine.Phase != MatchPhase.Ended; i++)
            {
                rig.Step();
            }

            return new Round { Match = m, Sent = rig.Sent };
        }

        private static readonly Lazy<Round> Full = new Lazy<Round>(() => PlayFor(3, PlaySeconds));

        [Fact]
        public void ScriptedCitizens_RaiseMeter()
        {
            Round r = Full.Value;
            bool metered = r.Engine.Meter.Value > 0f || r.Engine.Result?.Reason == GameOverReason.MeterFull;
            Assert.True(metered, "meter " + r.Engine.Meter.Value);
            Assert.True(Count<DeliveredEvent>(r.Match.Events) >= 1, "no delivery in " + PlaySeconds + " s");
            Assert.True(Count<SpotCleanedEvent>(r.Match.Events) >= 1);
        }

        [Fact]
        public void ScriptedBots_NeverSendAnOutOfPlayCommand()
        {
            Round r = Full.Value;
            Assert.DoesNotContain(r.Sent, c => c.Command is HitCommand || c.Command is DumpCommand || c.Command is CleanStartCommand || c.Command is TaskStepCommand
                || (c.Command is SabotageCommand s && s.Kind == SabotageKind.Fire));
            Assert.DoesNotContain(r.Sent, c => c.Command is FixCommand f && f.ConsoleId.StartsWith("fire"));
        }

        [Fact]
        public void ScriptedPolluter_Repollutes_WhenUnwatched()
        {
            Round r = Full.Value;
            Assert.True(Count<RepolluteOkEvent>(r.Match.Events) >= 1, "no re-pollute in " + PlaySeconds + " s");
        }

        [Fact]
        public void ScriptedPolluter_UsesUnlockedAbility()
        {
            TestMatch m = TestMatch.Create(11, startPlaying: false);
            byte pol = m.FirstSeatOf(Role.Polluter);
            m.Engine.Find(pol)!.Repollutes = m.Config.UnlockAfter - 1; // two re-pollutes done: the next one unlocks
            var rig = new BrainRig(m, 11);
            int max = (int)((m.Config.DeadlineS * 3f + 300f) / TestMatch.Dt);
            bool used = false;
            for (int i = 0; i < max && m.Engine.Phase != MatchPhase.Ended && !used; i++)
            {
                rig.Step();
                used = m.Events.Any(e => (e is VirusPlantedEvent v && v.Seat == pol) || (e is GasWarningEvent && m.Engine.Find(pol)!.Ability == TrasherAbility.Gas));
            }

            string why = "ended " + m.Engine.Result?.Reason + " at " + m.Engine.MatchTime.ToString("0") + " s, repollutes " + m.Engine.Find(pol)!.Repollutes;
            Assert.True(m.Events.Any(e => e is AbilityUnlockedEvent u && u.Seat == pol), "no unlock: " + why);
            Assert.True(m.Events.Any(e => e is AbilityOkEvent a && a.Seat == pol), "no pick: " + why);
            Assert.True(used, "ability never used: " + why);
        }

        // ---------- one Cleaner, one situation ----------

        [Fact]
        public void InfectedCitizenBot_HealsSelf_WhenHoldingHeal()
        {
            TestMatch m = TestMatch.Create(2);
            byte cit = m.FirstSeatOf(Role.Citizen);
            PlayerState p = m.Engine.Find(cit)!;
            p.Heals = 1;
            p.InfectedDeathAt = m.Engine.ActiveTime + 40f;
            BrainRig rig = OneSeat(m, cit);

            rig.RunUntil(() => m.Engine.Find(cit)!.InfectedDeathAt == null, 8f);

            Assert.Null(m.Engine.ViewFor(cit).Me.InfectedS);
            Assert.Equal(0, m.Engine.ViewFor(cit).Me.Heals);
            Assert.Contains(m.Events, e => e is HealedEvent h && h.Seat == cit && h.By == cit);
        }

        [Fact]
        public void InfectedCitizenBot_WithoutHeal_CallsItsFreeMeeting()
        {
            TestMatch m = TestMatch.Create(2);
            byte cit = m.FirstSeatOf(Role.Citizen);
            m.Engine.Find(cit)!.InfectedDeathAt = m.Engine.ActiveTime + 40f;
            BrainRig rig = OneSeat(m, cit);

            rig.RunUntil(() => m.Engine.Phase == MatchPhase.Meeting, 8f);

            Assert.Equal(MatchPhase.Meeting, m.Engine.Phase);
            Assert.Equal(cit, m.Engine.Meeting!.CalledBy);
        }

        [Fact]
        public void CleanerBot_PicksFromAnOpenUpgradeOffer()
        {
            TestMatch m = TestMatch.Create(2);
            byte cit = m.FirstSeatOf(Role.Citizen);
            m.Engine.Find(cit)!.UpgradeOffer = new List<UpgradeKind> { UpgradeKind.Backpack, UpgradeKind.Mask };
            BrainRig rig = OneSeat(m, cit);

            rig.RunUntil(() => m.Engine.OpenUpgradeOffer(cit) == null, 8f);

            Assert.Null(m.Engine.OpenUpgradeOffer(cit));
            UpgradeOkEvent ok = Assert.Single(m.Events.OfType<UpgradeOkEvent>());
            Assert.Contains(ok.Item, new[] { UpgradeKind.Backpack, UpgradeKind.Mask });
        }

        [Fact]
        public void CleanerBot_FixesTheJammedMachine_WhenItIsNear()
        {
            TestMatch m = TestMatch.Create(2);
            byte cit = m.FirstSeatOf(Role.Citizen);
            byte pol = m.FirstSeatOf(Role.Polluter);
            m.TickFor(m.Config.TrasherStartCooldownS + 1f);
            Assert.True(m.Engine.Apply(pol, new SabotageCommand(SabotageKind.Machine)).IsOk);
            Vec2 machine = m.Map.ConsolesOfKind(ConsoleKind.Machine)[0].Position;
            Vec2 near = new MapDistances_Helper(m).WalkableNear(machine, 4f);
            m.Engine.TestTeleport(cit, near);
            BrainRig rig = OneSeat(m, cit);

            rig.RunUntil(() => !m.Engine.Machine.Jammed, 20f);

            Assert.False(m.Engine.Machine.Jammed, "the jam was not fixed");
            Assert.False(m.Engine.ViewFor(cit).MachineJammed);
        }

        [Fact]
        public void CleanerBot_DeliversItsBagAtTheMachine()
        {
            TestMatch m = TestMatch.Create(2);
            byte cit = m.FirstSeatOf(Role.Citizen);
            m.Engine.Find(cit)!.Bags = 1;
            BrainRig rig = OneSeat(m, cit);

            rig.RunUntil(() => m.Engine.Find(cit)!.Bags == 0, 40f);

            Assert.Equal(0, m.Engine.Find(cit)!.Bags);
            Assert.Equal(m.Config.BagMeterAdd, m.Engine.Meter.Value, 2);
        }

        // ---------- what a Cleaner remembers ----------

        [Fact]
        public void Cleaner_RemembersASeenRepollute_AndSuspectsTheTrasher()
        {
            TestMatch m = TestMatch.Create(2);
            byte cit = m.FirstSeatOf(Role.Citizen);
            byte pol = m.FirstSeatOf(Role.Polluter);
            Sim.World.ConsoleDef console = m.Map.ConsolesOfKind(ConsoleKind.Task)[0];
            m.TickFor(m.Config.TrasherStartCooldownS + 1f);
            Sim.Uncycled.Spot spot = m.Engine.Spots.Find(console.Id)!;
            spot.State = SpotState.Clean;
            m.Engine.TestTeleport(pol, console.Position);
            m.Engine.TestTeleport(cit, new MapDistances_Helper(m).WalkableNear(console.Position, 2.5f));
            m.Tick();
            Assert.True(m.Engine.Apply(pol, new RepolluteCommand(console.Id)).IsOk);
            m.Tick();

            var mem = new BotMemory(m.Map, m.Config);
            SeatView view = m.Engine.ViewFor(cit);
            mem.Observe(view, m.Engine.MatchTime, m.Engine.Witnesses.SeenBy(cit));
            var suspicion = new SuspicionModel();
            suspicion.Update(mem, view);

            Assert.Equal(SuspicionModel.DirectWeight, suspicion.Score(pol));
            Assert.Equal(pol, suspicion.Vote(view, Role.Citizen, new byte[0]));
        }

        [Fact]
        public void MeetingState_ASeenRepolluteIsEvidenceOfADump_NotAHit()
        {
            TestMatch m = TestMatch.Create();
            SeatView live = m.Engine.ViewFor(0);
            var mem = new BotMemory(m.Map, m.Config);
            var seen = new WitnessRecord(ActKind.Repollute, 6, 0, 90f, "market", new Vec2(1f, 1f), null, null);
            mem.Observe(live, 100f, new List<WitnessRecord> { seen });
            var meeting = new MeetingView(MeetingStage.Discussion, 30f, 0, MeetingCause.Emergency, new List<ClueInfo>(), new List<byte>(), false, null, 0f, 0, new byte[] { 0, 1, 2, 6 });
            var view = new SeatView(0, MatchPhase.Meeting, 30f, 130f, live.DeadlineS, live.DeadlineClock, 130f, 10f, 2, live.Me, new List<PlayerView>(), new List<PileView>(), null, meeting, null);

            MeetingState s = MeetingStateBuilder.Build(view, mem, new MeetingLog(m.Content.Phrases!), new SuspicionModel(), null);

            Assert.Contains(s.Evidence, e => e.Kind == EvidenceItem.SawDump && e.Seat == 6 && e.Zone == "market");
            Assert.DoesNotContain(s.Evidence, e => e.Kind == EvidenceItem.SawHit);
        }

        [Fact]
        public void MeetingState_WithoutALivingList_UsesTheSeatsItKnows_NotZeroToSeatsMinusOne()
        {
            TestMatch m = TestMatch.Create();
            SeatView live = m.Engine.ViewFor(0);
            var mem = new BotMemory(m.Map, m.Config);
            var meeting = new MeetingView(MeetingStage.Discussion, 30f, 0, MeetingCause.Emergency, new List<ClueInfo>(), new List<byte>(), false, null, 0f);
            var view = new SeatView(0, MatchPhase.Meeting, 30f, 130f, live.DeadlineS, live.DeadlineClock, 130f, 10f, 2, live.Me, new List<PlayerView>(), new List<PileView>(), null, meeting, null);
            var log = new MeetingLog(m.Content.Phrases!);

            // a room that shrank: a human still sits on seat 9, there is no seat 4
            MeetingState real = MeetingStateBuilder.Build(view, mem, log, new SuspicionModel(), null, new byte[] { 0, 1, 2, 3, 9 });
            Assert.Equal(new byte[] { 0, 1, 2, 3, 9 }, real.Living.OrderBy(x => x).ToArray());

            // no list at all: the bot's own seat and the seats it has met, never invented ids
            MeetingState known = MeetingStateBuilder.Build(view, mem, log, new SuspicionModel(), null);
            Assert.Equal(new byte[] { 0 }, known.Living.ToArray());
        }

        // ---------- the fake-clean tell ----------

        /// <summary>A Cleaner stands in the zone and watches seat 3 at a dirty spot for the given seconds, then seat 3 steps away.</summary>
        private static (SyntheticBot Bot, Sim.World.ConsoleDef Spot) WatchSeat3(TestMatch m, float standS, Action<SyntheticBot, Sim.World.ConsoleDef>? during = null)
        {
            var bot = new SyntheticBot(m, Role.Citizen) { Seat = 0, MatchTime = 100f };
            Sim.World.ConsoleDef spot = m.Map.ConsolesOfKind(ConsoleKind.Task)[0];
            bot.Position = new MapDistances_Helper(m).WalkableNear(spot.Position, 3f);
            bot.Spots.Add(new SpotView(spot.Id, SpotState.Dirty));
            bot.Players.Add(SyntheticBot.Player(3, spot.Position.X, spot.Position.Y));
            for (float t = 0f; t < standS; t += 0.5f)
            {
                bot.Observe();
                during?.Invoke(bot, spot);
                bot.MatchTime += 0.5f;
            }

            bot.Players.Clear();
            bot.Players.Add(SyntheticBot.Player(3, spot.Position.X + 4f, spot.Position.Y));
            bot.Observe();
            return (bot, spot);
        }

        [Fact]
        public void FakeClean_APlayerWhoLeavesADirtySpotDirtyAfterACleanTime_IsSuspicious()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, _) = WatchSeat3(m, 6f);

            WitnessRecord r = Assert.Single(bot.Memory.Inferred);
            Assert.Equal((byte)3, r.Actor);
            var suspicion = new SuspicionModel();
            suspicion.Update(bot.Memory, bot.View());
            Assert.Equal(SuspicionModel.FakeCleanWeight, suspicion.Score(3));
            // one tell is a cue, not a vote: it needs a second one
            Assert.True(SuspicionModel.FakeCleanWeight <= 25f);
            Assert.True(SuspicionModel.FakeCleanWeight < SuspicionModel.VoteThreshold);
            Assert.Null(suspicion.Vote(bot.View(), Role.Citizen, new byte[0]));
        }

        [Fact]
        public void FakeClean_ASabotageThatStartsMidClean_InterruptsAnHonestCleaner_NoRecord()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, _) = WatchSeat3(m, 6f, (b, spot) =>
            {
                if (b.MatchTime >= 104f)
                {
                    b.Sabotage = new SabotageView(SabotageKind.Smog, null, new List<string>(), 1f);
                }
            });

            Assert.Empty(bot.Memory.Inferred);
        }

        [Fact]
        public void FakeClean_AGasWarningThatStartsMidClean_InterruptsAnHonestCleaner_NoRecord()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, _) = WatchSeat3(m, 6f, (b, spot) =>
            {
                b.Memory.GasWarnedRoom = b.MatchTime >= 104f ? "gas_market" : null;
            });

            Assert.Empty(bot.Memory.Inferred);
        }

        [Fact]
        public void FakeClean_AJamThatStartsMidClean_InterruptsAnHonestCleaner_NoRecord()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, _) = WatchSeat3(m, 6f, (b, spot) =>
            {
                b.MachineJammed = b.MatchTime >= 104f;
            });

            Assert.Empty(bot.Memory.Inferred);
        }

        [Fact]
        public void FakeClean_ALeaverSeenNextAtTheSmogPanel_IsDropped()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, Sim.World.ConsoleDef spot) = WatchSeat3(m, 6f);
            Assert.Single(bot.Memory.Inferred); // a plain leave is still a tell

            bot.Sabotage = new SabotageView(SabotageKind.Smog, null, new List<string>(), 1f);
            Sim.World.ConsoleDef panel = m.Map.ConsolesOfKind(ConsoleKind.SmogFix)[0];
            bot.MatchTime += 8f;
            bot.Players.Clear();
            bot.Players.Add(SyntheticBot.Player(3, panel.Position.X, panel.Position.Y));
            bot.Observe();

            Assert.Empty(bot.Memory.Inferred);
        }

        [Fact]
        public void FakeClean_ALeaverSeenNextAtTheJammedMachine_IsDropped()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, _) = WatchSeat3(m, 6f);
            Assert.Single(bot.Memory.Inferred);

            bot.MachineJammed = true;
            Sim.World.ConsoleDef machine = m.Map.ConsolesOfKind(ConsoleKind.Machine)[0];
            bot.MatchTime += 8f;
            bot.Players.Clear();
            bot.Players.Add(SyntheticBot.Player(3, machine.Position.X, machine.Position.Y));
            bot.Observe();

            Assert.Empty(bot.Memory.Inferred);
        }

        [Fact]
        public void FakeClean_ALeaverSeenElsewhereKeepsTheRecord()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, Sim.World.ConsoleDef spot) = WatchSeat3(m, 6f);

            bot.Sabotage = new SabotageView(SabotageKind.Smog, null, new List<string>(), 1f);
            bot.MatchTime += 8f;
            bot.Players.Clear();
            bot.Players.Add(SyntheticBot.Player(3, spot.Position.X + 5f, spot.Position.Y));
            bot.Observe();

            Assert.Single(bot.Memory.Inferred);
        }

        [Fact]
        public void FakeClean_NotWhenThePlayerWasOnlyPassingBy()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, _) = WatchSeat3(m, 1.5f);

            Assert.Empty(bot.Memory.Inferred);
        }

        [Fact]
        public void FakeClean_NotWhenTheSpotTurnedCleanWhileThePlayerStood_AnHonestCleaner()
        {
            TestMatch m = TestMatch.Create();
            (SyntheticBot bot, _) = WatchSeat3(m, 6f, (b, spot) =>
            {
                if (b.MatchTime >= 104f)
                {
                    b.Spots.Clear();
                    b.Spots.Add(new SpotView(spot.Id, SpotState.Clean));
                }
            });

            Assert.Empty(bot.Memory.Inferred);
        }

        // ---------- the observation ----------

        [Fact]
        public void Observation_MeterIsTheCleanliness_NotAPollution()
        {
            TestMatch m = TestMatch.Create();
            m.Engine.Meter.Add(40f);
            SeatView view = m.Engine.ViewFor(0);
            var mem = new BotMemory(m.Map, m.Config);
            var obs = new float[ObservationBuilder.Size];

            ObservationBuilder.Build(view, mem, m.Map, m.Config, m.Engine.MatchTime, obs);

            int i = ObservationBuilder.FieldNames.ToList().IndexOf("world.meter");
            Assert.Equal(0.4f, obs[i], 3);
        }

        [Fact]
        public void Eval_ReportsTheV2Counters()
        {
            string dir = Uncycled.Server.Content.ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
            Uncycled.Server.Content.ContentBundle content = Uncycled.Server.Content.ContentLoader.Load(dir);
            var options = Uncycled.Train.HostOptions.Parse(new[] { "--eval", "--content", dir, "--seeds", "1-2", "--threads", "1" });

            IReadOnlyList<string> lines = Uncycled.Train.EvalRunner.Run(options, content, Uncycled.Train.HostBrains.Scripted(new BotsSettings(), content));

            Assert.Equal(2, lines.Count);
            foreach (string line in lines)
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(line))
                {
                    foreach (string key in new[] { "deliveries", "repollutes", "abilities_used", "wrong_ejections", "correct_ejections", "meter" })
                    {
                        Assert.True(doc.RootElement.TryGetProperty(key, out _), key + " missing in " + line);
                    }
                }
            }
        }

        [Fact]
        public void BotAction_V2MembersAreAppendedAtTheEnd_AndTheNetworksActionSetIsUnchanged()
        {
            string[] names = Enum.GetNames(typeof(BotAction));
            Assert.Equal("FakeTaskHere", names[ActionCatalogue.Count - 1]);
            Assert.Equal(
                new[] { "GoDirtySpot", "FinishSpot", "GoMachine", "Deliver", "PickUpgrade", "Repollute", "PickAbility", "PlantVirus", "GasRoom", "HealSelf", "FixMachine", "SabotageMachine" },
                names.Skip(ActionCatalogue.Count).Take(12).ToArray());
            Assert.Equal(ActionCatalogue.TotalCount, names.Length);
        }
    }

    /// <summary>Finds a walkable point near a spot for a test (the executor's own distance map).</summary>
    internal sealed class MapDistances_Helper
    {
        private readonly TestMatch _m;

        public MapDistances_Helper(TestMatch m)
        {
            _m = m;
        }

        public Vec2 WalkableNear(Vec2 centre, float radius)
        {
            MapDistances dist = MapDistances.For(_m.Map);
            for (int i = 0; i < 16; i++)
            {
                double a = 2 * Math.PI * i / 16;
                var p = new Vec2(centre.X + (float)Math.Cos(a) * radius, centre.Y + (float)Math.Sin(a) * radius);
                Vec2 cell = dist.NearestWalkable(p);
                if (Vec2.Distance(cell, centre) <= radius + 0.6f && _m.Map.Sight.HasLineOfSight(cell, centre) && dist.Path(cell, centre) != MapDistances.Unreachable)
                {
                    return cell;
                }
            }

            return dist.NearestWalkable(centre);
        }
    }
}
