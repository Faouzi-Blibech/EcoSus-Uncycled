using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Bots.Meeting;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public class MeetingTalkerTests
    {
        // ---------- state helpers (pure rules) ----------

        private static readonly string[] MapZones = { "depot", "market", "garden" };

        private static MeetingState State(
            Role role = Role.Citizen,
            byte self = 0,
            byte[]? partners = null,
            byte[]? living = null,
            MeetingStage stage = MeetingStage.Discussion,
            IEnumerable<EvidenceItem>? evidence = null,
            IEnumerable<ChatAct>? chat = null,
            Dictionary<byte, float>? suspicion = null,
            Dictionary<byte, float>? heat = null)
        {
            return new MeetingState
            {
                Self = self,
                Role = role,
                Partners = partners ?? new byte[0],
                Living = living ?? new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 },
                Stage = stage,
                StageLeft = 30f,
                Evidence = (evidence ?? new EvidenceItem[0]).ToList(),
                Chat = (chat ?? new ChatAct[0]).ToList(),
                Suspicion = suspicion ?? new Dictionary<byte, float>(),
                Heat = heat ?? new Dictionary<byte, float>(),
                Zones = MapZones,
            };
        }

        private static ChatAct Act(byte speaker, string id, string kind, byte? seat = null, string? zone = null)
        {
            return new ChatAct(speaker, id, kind, seat, zone, 10f);
        }

        // ---------- floor ----------

        [Fact]
        public void Floor_OneBotLineEvery2sAcrossBots()
        {
            var floor = new MeetingFloor();
            Assert.True(floor.TryTake(10f));
            Assert.False(floor.TryTake(10f));
            Assert.False(floor.TryTake(11.5f));
            Assert.True(floor.TryTake(12.01f));
            Assert.False(floor.TryTake(13f));
        }

        // ---------- grounding ----------

        [Fact]
        public void Grounding_CitizenNeverClaimsAnUnseenAct()
        {
            MeetingState none = State();
            foreach (MeetingDecision claim in new[]
            {
                MeetingDecision.Say("fake", "saw_dump", 3, "depot"),
                MeetingDecision.Say("fake", "saw_hit", 3, "depot"),
                MeetingDecision.Say("fake", "near_pile", 3, "depot"),
                MeetingDecision.Say("fake", "i_was", null, "market"),
            })
            {
                MeetingDecision g = Grounding.Check(claim, none);
                Assert.True(!g.Speak || g.PhraseId == "hmm", claim.PhraseId + " became " + g.PhraseId);
            }

            // what it did see passes unchanged
            MeetingState seen = State(evidence: new[]
            {
                new EvidenceItem(EvidenceItem.SawDump, 3, "depot", 50f),
                new EvidenceItem(EvidenceItem.MyZone, null, "market", 60f),
            });
            MeetingDecision ok = Grounding.Check(MeetingDecision.Say("fake", "saw_dump", 3, "depot"), seen);
            Assert.Equal("saw_dump", ok.PhraseId);
            Assert.Equal((byte?)3, ok.Seat);
            Assert.Equal("depot", ok.Zone);
            Assert.Equal("i_was", Grounding.Check(MeetingDecision.Say("fake", "i_was", null, "market"), seen).PhraseId);

            // a real sighting in the wrong zone is corrected to what it holds, never kept as claimed
            MeetingDecision fixedZone = Grounding.Check(MeetingDecision.Say("fake", "saw_dump", 3, "garden"), seen);
            Assert.Equal("saw_dump", fixedZone.PhraseId);
            Assert.Equal("depot", fixedZone.Zone);
        }

        [Fact]
        public void Grounding_NeverNamesOutSeatsOrSelf()
        {
            foreach (Role role in new[] { Role.Citizen, Role.Polluter })
            {
                MeetingState s = State(role, living: new byte[] { 0, 1, 2, 3, 4 }, suspicion: new Dictionary<byte, float> { { 2, 50f }, { 6, 50f } });
                foreach (byte bad in new byte[] { 0, 6 })
                {
                    foreach (string id in new[] { "sus", "where", "agree", "doubt", "lying", "vote" })
                    {
                        MeetingDecision g = Grounding.Check(MeetingDecision.Say("fake", id, bad), s);
                        Assert.True(g.Seat != bad, role + " " + id + " named " + bad);
                    }

                    MeetingDecision v = Grounding.Check(new MeetingDecision { Vote = bad, Source = "fake" }, State(role, living: new byte[] { 0, 1, 2, 3, 4 }, stage: MeetingStage.Voting));
                    Assert.Null(v.Vote);
                    Assert.True(v.SkipVote);
                }
            }
        }

        [Fact]
        public void Grounding_PolluterNeverAccusesPartner()
        {
            MeetingState s = State(Role.Polluter, partners: new byte[] { 1 }, suspicion: new Dictionary<byte, float> { { 1, 90f } });
            foreach (string id in new[] { "sus", "saw_dump", "saw_hit", "near_pile", "doubt", "lying", "vote" })
            {
                MeetingDecision g = Grounding.Check(MeetingDecision.Say("fake", id, 1, "depot"), s);
                Assert.True(g.Seat != 1, id + " named the partner");
            }

            // a seat that accused the partner: agreeing with it would accuse the partner too
            MeetingState accused = State(Role.Polluter, partners: new byte[] { 1 }, chat: new[] { Act(4, "sus", "accuse", 1) });
            Assert.True(Grounding.Check(MeetingDecision.Say("fake", "agree", 4), accused).Seat != 4);

            MeetingState voting = State(Role.Polluter, partners: new byte[] { 1 }, stage: MeetingStage.Voting);
            MeetingDecision v = Grounding.Check(new MeetingDecision { Vote = 1, Source = "fake" }, voting);
            Assert.Null(v.Vote);
            Assert.True(v.SkipVote);
        }

        [Fact]
        public void Grounding_PolluterMayLieAboutItsZone()
        {
            MeetingState s = State(Role.Polluter, partners: new byte[] { 1 }, evidence: new[] { new EvidenceItem(EvidenceItem.MyZone, null, "depot", 60f) });

            MeetingDecision alibi = Grounding.Check(MeetingDecision.Say("fake", "i_was", null, "market"), s);
            Assert.Equal("i_was", alibi.PhraseId);
            Assert.Equal("market", alibi.Zone);

            MeetingDecision framing = Grounding.Check(MeetingDecision.Say("fake", "saw_dump", 4, "garden"), s);
            Assert.Equal("saw_dump", framing.PhraseId);
            Assert.Equal((byte?)4, framing.Seat);

            // a zone that is not on the map is never sent, lie or not
            MeetingDecision nowhere = Grounding.Check(MeetingDecision.Say("fake", "i_was", null, "moon"), s);
            Assert.NotEqual("moon", nowhere.Zone);
        }

        [Fact]
        public void Grounding_PartnerDefenceOnlyWhenPartnerHotAndSelfCool()
        {
            Func<float, float, MeetingState> state = (partnerHeat, selfHeat) => State(
                Role.Polluter,
                partners: new byte[] { 1 },
                heat: new Dictionary<byte, float> { { 1, partnerHeat }, { 0, selfHeat } });
            MeetingDecision vouch = MeetingDecision.Say("fake", "with", 1, "market");

            Assert.Equal("with", Grounding.Check(vouch, state(0.5f, 0.05f)).PhraseId);
            Assert.NotEqual("with", Grounding.Check(vouch, state(0.05f, 0.05f)).PhraseId); // partner not hot
            Assert.NotEqual("with", Grounding.Check(vouch, state(0.5f, 0.5f)).PhraseId); // self not cool

            // at most once per meeting
            MeetingState already = State(
                Role.Polluter,
                partners: new byte[] { 1 },
                heat: new Dictionary<byte, float> { { 1, 0.5f }, { 0, 0.05f } },
                chat: new[] { Act(0, "with", "alibi", 1, "garden") });
            Assert.NotEqual("with", Grounding.Check(vouch, already).PhraseId);

            // calling out the seat that accused the partner counts as the same defence
            MeetingState accuser = State(
                Role.Polluter,
                partners: new byte[] { 1 },
                heat: new Dictionary<byte, float> { { 1, 0.5f }, { 0, 0.05f } },
                chat: new[] { Act(4, "sus", "accuse", 1) });
            Assert.Equal("doubt", Grounding.Check(MeetingDecision.Say("fake", "doubt", 4), accuser).PhraseId);
            MeetingState cool = State(Role.Polluter, partners: new byte[] { 1 }, chat: new[] { Act(4, "sus", "accuse", 1) });
            Assert.NotEqual("doubt", Grounding.Check(MeetingDecision.Say("fake", "doubt", 4), cool).PhraseId);
        }

        [Fact]
        public void Grounding_PartnerDefenceIsOncePerMeetingAcrossCallOutAndVouch()
        {
            Dictionary<byte, float> heat = new Dictionary<byte, float> { { 1, 0.5f }, { 0, 0.05f } };
            ChatAct[] accusers = { Act(4, "sus", "accuse", 1), Act(5, "sus", "accuse", 1) };

            // it already called out seat 4, who accused the partner: a second call-out or a vouch would be a second defence
            MeetingState calledOut = State(Role.Polluter, partners: new byte[] { 1 }, heat: heat, chat: accusers.Concat(new[] { Act(0, "doubt", "react", 4) }));
            Assert.NotEqual("lying", Grounding.Check(MeetingDecision.Say("fake", "lying", 5), calledOut).PhraseId);
            Assert.NotEqual("with", Grounding.Check(MeetingDecision.Say("fake", "with", 1, "market"), calledOut).PhraseId);

            // the first one is fine
            MeetingState fresh = State(Role.Polluter, partners: new byte[] { 1 }, heat: heat, chat: accusers);
            Assert.Equal("lying", Grounding.Check(MeetingDecision.Say("fake", "lying", 5), fresh).PhraseId);
        }

        [Fact]
        public void Grounding_CitizenVouchesOnlyForASeatItSawThere()
        {
            MeetingState s = State(evidence: new[]
            {
                new EvidenceItem(EvidenceItem.MyZone, null, "market", 60f),
                new EvidenceItem(EvidenceItem.NearPile, 3, "market", 61f),
            });

            MeetingDecision saw = Grounding.Check(MeetingDecision.Say("fake", "with", 3, "market"), s);
            Assert.Equal("with", saw.PhraseId);
            Assert.Equal((byte?)3, saw.Seat);

            // seat 4 was never seen there: no vouching for it, only its own alibi
            MeetingDecision unseen = Grounding.Check(MeetingDecision.Say("fake", "with", 4, "market"), s);
            Assert.Equal("i_was", unseen.PhraseId);
            Assert.Null(unseen.Seat);
        }

        [Fact]
        public void Grounding_ASecondNotMeBecomesTheAlibiThenSilence()
        {
            ChatAct accusation = Act(2, "sus", "accuse", 0);
            var zone = new EvidenceItem(EvidenceItem.MyZone, null, "market", 60f);
            MeetingDecision notMe = MeetingDecision.Say("fake", "not_me");

            Assert.Equal("not_me", Grounding.Check(notMe, State(evidence: new[] { zone }, chat: new[] { accusation })).PhraseId);

            MeetingState said = State(evidence: new[] { zone }, chat: new[] { accusation, Act(0, "not_me", "react") });
            MeetingDecision second = Grounding.Check(notMe, said);
            Assert.Equal("i_was", second.PhraseId);
            Assert.Equal("market", second.Zone);

            MeetingState gave = State(evidence: new[] { zone }, chat: new[] { accusation, Act(0, "not_me", "react"), Act(0, "i_was", "alibi", null, "market") });
            Assert.Equal("hmm", Grounding.Check(notMe, gave).PhraseId);
        }

        [Fact]
        public void Rule_AlibiIsTheOwnZoneNearestTheClueNotTheLatestOne()
        {
            var clue = new ClueInfo(1, PileKind.Dump, "depot", 100f, WasteType.Glass, null);
            MeetingState s = State(
                evidence: new[]
                {
                    new EvidenceItem(EvidenceItem.MyZone, null, "market", 105f),
                    new EvidenceItem(EvidenceItem.MyZone, null, "garden", 150f),
                },
                chat: new[] { Act(2, "sus", "accuse", 0), Act(0, "not_me", "react") });
            s.Clues = new[] { clue };

            Assert.Equal("market", RuleDecider.Decide(s).Zone);
            Assert.Equal("market", Grounding.Check(MeetingDecision.Say("fake", "i_was", null, "garden"), s).Zone);
            Assert.Equal("market", Grounding.Check(MeetingDecision.Say("fake", "i_was", null, "market"), s).Zone);

            // no clue (a body report with no pile data): the latest zone, as before
            s.Clues = new ClueInfo[0];
            Assert.Equal("garden", RuleDecider.Decide(s).Zone);
        }

        // ---------- rule decider ----------

        [Fact]
        public void Rule_VotesTopSuspectAtThresholdElseSkip()
        {
            MeetingState hot = State(stage: MeetingStage.Voting, suspicion: new Dictionary<byte, float> { { 3, 45f }, { 4, 80f }, { 5, 20f } });
            MeetingDecision d = RuleDecider.Decide(hot);
            Assert.Equal((byte?)4, d.Vote);
            Assert.False(d.SkipVote);
            Assert.Equal("rule", d.Source);

            MeetingState cold = State(stage: MeetingStage.Voting, suspicion: new Dictionary<byte, float> { { 3, SuspicionModel.VoteThreshold - 1f } });
            MeetingDecision skip = RuleDecider.Decide(cold);
            Assert.Null(skip.Vote);
            Assert.True(skip.SkipVote);

            // exactly at the threshold counts; a polluter skips past its partner to the next suspect
            MeetingState edge = State(Role.Polluter, partners: new byte[] { 4 }, stage: MeetingStage.Voting, suspicion: new Dictionary<byte, float> { { 3, SuspicionModel.VoteThreshold }, { 4, 99f } });
            Assert.Equal((byte?)3, RuleDecider.Decide(edge).Vote);
        }

        // ---------- state building ----------

        [Fact]
        public void State_EvidenceHoldsOnlyWhatTheBotSaw()
        {
            TestMatch m = TestMatch.Create();
            SeatView live = m.Engine.ViewFor(0);
            PlayerView seen = live.Players[0];
            string zone = m.Map.ZoneAt(seen.Position);
            var mem = new BotMemory(m.Map, m.Config);
            var mine = new WitnessRecord(ActKind.Dump, 6, 0, 90f, "market", new Vec2(1f, 1f), 1, null);
            mem.Observe(live, 100f, new List<WitnessRecord> { mine });

            var clue = new ClueInfo(1, PileKind.Dump, zone, 105f, WasteType.Glass, null);
            var meeting = new MeetingView(MeetingStage.Discussion, 30f, 0, MeetingCause.Report, new List<ClueInfo> { clue }, new List<byte>(), false, null, 0f, 0, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
            var view = new SeatView(0, MatchPhase.Meeting, 30f, 130f, live.DeadlineS, live.DeadlineClock, 130f, 10f, 2, live.Me, new List<PlayerView>(), new List<PileView>(), null, meeting, null);

            MeetingState s = MeetingStateBuilder.Build(view, mem, new MeetingLog(m.Content.Phrases!), new SuspicionModel(), null);

            Assert.Contains(s.Evidence, e => e.Kind == EvidenceItem.SawDump && e.Seat == 6 && e.Zone == "market");
            Assert.Contains(s.Evidence, e => e.Kind == EvidenceItem.NearPile && e.Seat == seen.Seat && e.Zone == zone);
            Assert.Contains(s.Evidence, e => e.Kind == EvidenceItem.MyZone);
            // nothing about a seat the bot never saw, and the whole table is living
            var sighted = new HashSet<byte>(mem.SeenSeats) { 6 };
            Assert.All(s.Evidence, e => Assert.True(e.Seat == null || sighted.Contains(e.Seat.Value)));
            Assert.Equal(8, s.Living.Count);
            Assert.Equal(Role.Citizen, s.Role);
        }

        [Fact]
        public void State_TheAlibiZoneIsWhereTheBotWasAtTheClueTimeEvenIfItCameBack()
        {
            TestMatch m = TestMatch.Create();
            Vec2 Centre(string id)
            {
                Sim.World.ZoneDef z = m.Map.Zones.First(d => d.Id == id);
                return new Vec2(z.X + z.Width / 2f, z.Y + z.Height / 2f);
            }

            SeatView live = m.Engine.ViewFor(0);
            var mem = new BotMemory(m.Map, m.Config);
            foreach ((string zone, float at) in new[] { ("market", 100f), ("garden", 120f), ("market", 150f) })
            {
                var me = new MyView(0, Role.Citizen, new byte[0], null, PlayerStatus.Active, Centre(zone), 4f, 0, null, new List<TaskInfo>());
                mem.Observe(new SeatView(0, MatchPhase.Playing, 0f, at, live.DeadlineS, live.DeadlineClock, at, 10f, 2, me, new List<PlayerView>(), new List<PileView>(), null, null, null), at, new List<WitnessRecord>());
            }

            // the pile is in the depot at t=100, when the bot stood in the market; it was last in the market too
            var clue = new ClueInfo(1, PileKind.Dump, "depot", 100f, WasteType.Glass, null);
            var meeting = new MeetingView(MeetingStage.Discussion, 30f, 0, MeetingCause.Report, new List<ClueInfo> { clue }, new List<byte>(), false, null, 0f, 0, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
            var view = new SeatView(0, MatchPhase.Meeting, 30f, 155f, live.DeadlineS, live.DeadlineClock, 155f, 10f, 2, live.Me, new List<PlayerView>(), new List<PileView>(), null, meeting, null);

            MeetingState s = MeetingStateBuilder.Build(view, mem, new MeetingLog(m.Content.Phrases!), new SuspicionModel(), null);

            Assert.Equal("market", s.AlibiZone());
            Assert.Equal(m.Config.Seats, s.Seats);
            Assert.Equal(m.Config.Polluters, s.Polluters);
        }

        [Fact]
        public void State_EvidenceHasNoDuplicates()
        {
            TestMatch m = TestMatch.Create();
            SeatView live = m.Engine.ViewFor(0);
            PlayerView seen = live.Players[0];
            string zone = m.Map.ZoneAt(seen.Position);
            var mem = new BotMemory(m.Map, m.Config);
            mem.Observe(live, 100f, new List<WitnessRecord>());

            // two piles in the same zone within the near-pile window: one sighting must give one evidence item, not one per pile
            var clues = new List<ClueInfo> { new ClueInfo(1, PileKind.Dump, zone, 103f, WasteType.Glass, null), new ClueInfo(2, PileKind.Dump, zone, 107f, WasteType.Glass, null) };
            var meeting = new MeetingView(MeetingStage.Discussion, 30f, 0, MeetingCause.Report, clues, new List<byte>(), false, null, 0f, 0, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
            var view = new SeatView(0, MatchPhase.Meeting, 30f, 130f, live.DeadlineS, live.DeadlineClock, 130f, 10f, 2, live.Me, new List<PlayerView>(), new List<PileView>(), null, meeting, null);

            MeetingState s2 = MeetingStateBuilder.Build(view, mem, new MeetingLog(m.Content.Phrases!), new SuspicionModel(), null);

            Assert.Contains(s2.Evidence, e => e.Kind == EvidenceItem.NearPile && e.Seat == seen.Seat);
            Assert.Equal(s2.Evidence.Count, s2.Evidence.Select(e => (e.Kind, e.Seat, e.Zone, e.Time)).Distinct().Count());
        }

        // ---------- talker in a real meeting ----------

        private sealed class Fixed : IMeetingDecider
        {
            private readonly MeetingDecision _speak;
            private readonly MeetingDecision? _vote;

            public Fixed(string name, string phraseId, MeetingDecision? vote = null)
            {
                Name = name;
                _speak = MeetingDecision.Say(name, phraseId);
                _vote = vote;
            }

            public string Name { get; }

            public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
            {
                return Task.FromResult(state.Stage == MeetingStage.Voting ? (_vote ?? MeetingDecision.Quiet(Name)) : _speak);
            }
        }

        private sealed class Never : IMeetingDecider
        {
            public string Name => "never";

            public int Calls { get; private set; }

            public bool SawCancel { get; private set; }

            public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
            {
                Calls++;
                var tcs = new TaskCompletionSource<MeetingDecision>();
                ct.Register(() => SawCancel = true);
                return tcs.Task;
            }
        }

        private sealed class Sent
        {
            public Sent(byte seat, float time, MeetingStage stage, ICommand command, CommandResult result)
            {
                Seat = seat;
                Time = time;
                Stage = stage;
                Command = command;
                Result = result;
            }

            public byte Seat { get; }

            public float Time { get; }

            public MeetingStage Stage { get; }

            public ICommand Command { get; }

            public CommandResult Result { get; }
        }

        /// <summary>A real meeting (a witnessed dump is reported) driven by one MeetingTalker per chosen seat.</summary>
        private sealed class Rig
        {
            private readonly TestMatch _m;
            private readonly MeetingLog _log;
            private readonly Dictionary<byte, BotMemory> _mem = new Dictionary<byte, BotMemory>();
            private int _eventsSeen;

            public Rig()
            {
                _m = TestMatch.Create(2);
                _m.TickFor(11f);
                foreach (PlayerState p in _m.Engine.Players)
                {
                    // every bot has played until now: its own zone history is what an alibi is made from
                    var mem = new BotMemory(_m.Map, _m.Config);
                    mem.Observe(_m.Engine.ViewFor(p.Seat), _m.Engine.MatchTime, _m.Engine.Witnesses.SeenBy(p.Seat));
                    _mem[p.Seat] = mem;
                }

                byte polluter = _m.FirstSeatOf(Role.Polluter);
                SeatView pv = _m.Engine.ViewFor(polluter);
                PlayerView witness = pv.Players.First(p => Vec2.Distance(p.Position, pv.Me.Position) <= 1.0f && !pv.Me.Partners.Contains(p.Seat));
                Reporter = witness.Seat;
                Assert.True(_m.Engine.Apply(polluter, new DumpCommand()).IsOk);
                int pile = _m.Engine.Piles.All[0].Id;
                Assert.True(_m.Engine.Apply(Reporter, new ReportCommand(pile)).IsOk);
                Assert.Equal(MatchPhase.Meeting, _m.Engine.Phase);
                _log = new MeetingLog(_m.Content.Phrases!);
                MeetingStart = _m.Engine.MatchTime;
                _eventsSeen = _m.Events.Count;
            }

            public TestMatch Match => _m;

            public MeetingLog Log => _log;

            public byte Reporter { get; }

            public float MeetingStart { get; }

            public float VotingStart { get; private set; } = -1f;

            public List<Sent> Chat { get; } = new List<Sent>();

            public List<Sent> Votes { get; } = new List<Sent>();

            public double MaxStepMs { get; private set; }

            /// <summary>Another active seat that is none of the given seats.</summary>
            public byte OtherThan(params byte[] seats)
            {
                return _m.Engine.Players.First(p => p.IsActive && !seats.Contains(p.Seat)).Seat;
            }

            public void Run(Dictionary<byte, MeetingTalker> talkers, Func<MatchEngine, float, bool>? before = null)
            {
                MatchEngine e = _m.Engine;
                int guard = 0;
                while (e.Phase == MatchPhase.Meeting && guard++ < 4000)
                {
                    float now = e.MatchTime;
                    MeetingStage stage = e.Meeting!.Stage;
                    if (stage == MeetingStage.Voting && VotingStart < 0f)
                    {
                        VotingStart = now;
                    }

                    before?.Invoke(e, now);
                    foreach (KeyValuePair<byte, MeetingTalker> kv in talkers)
                    {
                        byte seat = kv.Key;
                        SeatView view = e.ViewFor(seat);
                        if (!_mem.TryGetValue(seat, out BotMemory? mem))
                        {
                            mem = new BotMemory(_m.Map, _m.Config);
                            _mem[seat] = mem;
                        }

                        mem.Observe(view, now, e.Witnesses.SeenBy(seat));
                        Stopwatch sw = Stopwatch.StartNew();
                        kv.Value.Step(e, seat, view, mem, _log, now, c =>
                        {
                            CommandResult r = e.Apply(seat, c);
                            var rec = new Sent(seat, now, stage, c, r);
                            if (c is ChatPhraseCommand)
                            {
                                Chat.Add(rec);
                            }
                            else if (c is VoteCommand)
                            {
                                Votes.Add(rec);
                            }
                        });
                        MaxStepMs = Math.Max(MaxStepMs, sw.Elapsed.TotalMilliseconds);
                    }

                    _m.Tick();
                    for (; _eventsSeen < _m.Events.Count; _eventsSeen++)
                    {
                        if (_m.Events[_eventsSeen] is ChatEvent ce)
                        {
                            _log.Add(ce, e.MatchTime);
                        }
                    }
                }
            }
        }

        private static MeetingTalker NewTalker(IMeetingDecider primary, IMeetingDecider fallback, MeetingFloor? floor = null, int seed = 1)
        {
            return new MeetingTalker(primary, fallback, floor ?? new MeetingFloor(), new Random(seed));
        }

        [Fact]
        public void Talker_AtMostThreeLinesWithHumanGaps()
        {
            var rig = new Rig();
            byte bot = rig.OtherThan(rig.Reporter);
            var talkers = new Dictionary<byte, MeetingTalker> { { bot, NewTalker(new Fixed("fake", "hmm"), new RuleDecider()) } };

            rig.Run(talkers);

            Assert.Equal(3, rig.Chat.Count);
            Assert.All(rig.Chat, c => Assert.True(c.Result.IsOk, "the engine rejected " + ((ChatPhraseCommand)c.Command).PhraseId));
            float first = rig.Chat[0].Time - rig.MeetingStart;
            Assert.InRange(first, 4f, 12f + 0.1f);
            for (int i = 1; i < rig.Chat.Count; i++)
            {
                Assert.InRange(rig.Chat[i].Time - rig.Chat[i - 1].Time, 6f, 15f + 0.1f);
            }

            Assert.All(rig.Chat, c => Assert.Equal(MeetingStage.Discussion, c.Stage));
        }

        [Fact]
        public void Talker_SharedFloorKeepsBotLines2sApart()
        {
            var rig = new Rig();
            var floor = new MeetingFloor();
            var talkers = new Dictionary<byte, MeetingTalker>();
            for (int i = 0; i < 4; i++)
            {
                byte bot = rig.OtherThan(new[] { rig.Reporter }.Concat(talkers.Keys).ToArray());
                talkers[bot] = NewTalker(new Fixed("fake", "hmm"), new RuleDecider(), floor, 10 + i);
            }

            rig.Run(talkers);

            List<float> times = rig.Chat.Select(c => c.Time).OrderBy(t => t).ToList();
            Assert.True(times.Count >= 8, "lines: " + times.Count);
            for (int i = 1; i < times.Count; i++)
            {
                Assert.True(times[i] - times[i - 1] >= MeetingFloor.MinGapS - 1e-3f, "lines " + (times[i] - times[i - 1]) + " s apart");
            }
        }

        private sealed class Spy : IMeetingDecider
        {
            public string Name => "spy";

            public List<string> MatchIds { get; } = new List<string>();

            public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
            {
                MatchIds.Add(state.MatchId);
                return Task.FromResult(MeetingDecision.Quiet("spy"));
            }
        }

        [Fact]
        public void Talker_PassesTheMatchIdToTheDecider()
        {
            var rig = new Rig();
            byte bot = rig.OtherThan(rig.Reporter);
            var spy = new Spy();
            MeetingTalker talker = NewTalker(spy, new RuleDecider());
            talker.MatchId = "ROOM7";

            rig.Run(new Dictionary<byte, MeetingTalker> { { bot, talker } });

            Assert.NotEmpty(spy.MatchIds);
            Assert.All(spy.MatchIds, id => Assert.Equal("ROOM7", id));
        }

        [Fact]
        public void Talker_RepliesToAnAccusationWithin3To8s()
        {
            var rig = new Rig();
            byte bot = rig.OtherThan(rig.Reporter);
            byte accuser = rig.OtherThan(rig.Reporter, bot);
            var talkers = new Dictionary<byte, MeetingTalker> { { bot, NewTalker(new RuleDecider(), new RuleDecider()) } };
            float accusedAt = -1f;

            rig.Run(talkers, (e, now) =>
            {
                if (accusedAt < 0f && now - rig.MeetingStart >= 1f)
                {
                    Assert.True(e.Apply(accuser, new ChatPhraseCommand("sus", new[] { ChatArg.ForSeat(bot) })).IsOk);
                    accusedAt = now;
                }

                return true;
            });

            var mine = rig.Chat.Where(c => c.Seat == bot).Select(c => (ChatPhraseCommand)c.Command).ToList();
            Assert.True(mine.Count >= 2, "lines: " + string.Join(",", mine.Select(c => c.PhraseId)));
            Assert.Equal("not_me", mine[0].PhraseId);
            Assert.InRange(rig.Chat[0].Time - accusedAt, 3f, 8f + 0.1f);
            Assert.Equal("i_was", mine[1].PhraseId);
            Assert.All(rig.Chat, c => Assert.True(c.Result.IsOk));
        }

        [Fact]
        public void Talker_VotesBetween3And20sIntoVoting()
        {
            var rig = new Rig();
            byte bot = rig.OtherThan(rig.Reporter);
            var talkers = new Dictionary<byte, MeetingTalker> { { bot, NewTalker(new RuleDecider(), new RuleDecider()) } };

            rig.Run(talkers);

            Sent vote = Assert.Single(rig.Votes);
            Assert.True(vote.Result.IsOk);
            Assert.InRange(vote.Time - rig.VotingStart, 3f, 20f + 0.1f);
        }

        [Fact]
        public void Talker_UsesFallbackWhenPrimaryIsLate()
        {
            var rig = new Rig();
            byte bot = rig.OtherThan(rig.Reporter);
            var never = new Never();
            var talkers = new Dictionary<byte, MeetingTalker>
            {
                { bot, NewTalker(never, new Fixed("rule", "idk", MeetingDecision.Quiet("rule"))) },
            };

            rig.Run(talkers);

            Assert.True(never.Calls >= 1);
            Assert.True(never.SawCancel, "the late decision is cancelled, not awaited");
            Assert.NotEmpty(rig.Chat);
            Assert.All(rig.Chat, c => Assert.Equal("idk", ((ChatPhraseCommand)c.Command).PhraseId));
            Assert.InRange(rig.Chat[0].Time - rig.MeetingStart, 4f, 12f + 0.1f);
            Sent vote = Assert.Single(rig.Votes);
            Assert.InRange(vote.Time - rig.VotingStart, 3f, 20f + 0.1f);
            Assert.True(rig.MaxStepMs < 250, "Step blocked for " + rig.MaxStepMs + " ms");
        }

        // ---------- asynchronous and reply behaviour ----------

        /// <summary>A primary decider whose answer arrives DelayS of match time after the call (released by the rig's clock).</summary>
        private sealed class Delayed : IMeetingDecider
        {
            private readonly MeetingDecision _speak;
            private readonly byte _vote;
            private readonly float _delayS;
            private readonly List<(TaskCompletionSource<MeetingDecision> Tcs, MeetingDecision Result, float[] Stamp)> _pending = new List<(TaskCompletionSource<MeetingDecision>, MeetingDecision, float[])>();

            public Delayed(MeetingDecision speak, byte vote, float delayS)
            {
                _speak = speak;
                _vote = vote;
                _delayS = delayS;
            }

            public string Name => "delayed";

            public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
            {
                var tcs = new TaskCompletionSource<MeetingDecision>();
                MeetingDecision result = state.Stage == MeetingStage.Voting ? new MeetingDecision { Vote = _vote, Source = "delayed" } : _speak;
                _pending.Add((tcs, result, new[] { float.NaN }));
                return tcs.Task;
            }

            public void Release(float now)
            {
                foreach ((TaskCompletionSource<MeetingDecision> tcs, MeetingDecision result, float[] stamp) in _pending)
                {
                    if (float.IsNaN(stamp[0]))
                    {
                        stamp[0] = now;
                    }

                    if (now - stamp[0] >= _delayS)
                    {
                        tcs.TrySetResult(result);
                    }
                }
            }
        }

        /// <summary>Says not_me when it is answering an accusation (Replying), else the given phrase, or nothing.</summary>
        private sealed class Replier : IMeetingDecider
        {
            private readonly string? _otherwise;

            public Replier(string? otherwise)
            {
                _otherwise = otherwise;
            }

            public string Name => "replier";

            public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
            {
                if (state.Replying)
                {
                    return Task.FromResult(MeetingDecision.Say("replier", "not_me"));
                }

                return Task.FromResult(_otherwise == null ? MeetingDecision.Quiet("replier") : MeetingDecision.Say("replier", _otherwise));
            }
        }

        private sealed class ListSink : IDecisionSink
        {
            public List<(MeetingState State, MeetingDecision Sent)> Items { get; } = new List<(MeetingState, MeetingDecision)>();

            public void Sent(MeetingState state, MeetingDecision sent) => Items.Add((state, sent));
        }

        private static void Accuse(MatchEngine e, byte accuser, byte target)
        {
            Assert.True(e.Apply(accuser, new ChatPhraseCommand("sus", new[] { ChatArg.ForSeat(target) })).IsOk);
        }

        [Fact]
        public void Talker_UsesAPrimaryDecisionThatFinishesOnALaterTick()
        {
            var rig = new Rig();
            byte bot = rig.OtherThan(rig.Reporter);
            var delayed = new Delayed(MeetingDecision.Say("delayed", "idk"), rig.Reporter, 1f);
            MeetingTalker talker = NewTalker(delayed, new Fixed("rule", "hmm", MeetingDecision.Quiet("rule")));

            rig.Run(new Dictionary<byte, MeetingTalker> { { bot, talker } }, (e, now) =>
            {
                delayed.Release(now);
                return true;
            });

            Assert.NotEmpty(rig.Chat);
            Assert.All(rig.Chat, c => Assert.Equal("idk", ((ChatPhraseCommand)c.Command).PhraseId));
            Assert.InRange(rig.Chat[0].Time - rig.MeetingStart, 4f, 12f + 0.1f);
            Sent vote = Assert.Single(rig.Votes);
            Assert.Equal((byte?)rig.Reporter, ((VoteCommand)vote.Command).Target);
            Assert.Equal("delayed", talker.LastSource);
        }

        [Fact]
        public void Talker_LeadSIsTheModelCallTimeout()
        {
            Func<float, List<string>> run = lead =>
            {
                var rig = new Rig();
                byte bot = rig.OtherThan(rig.Reporter);
                var delayed = new Delayed(MeetingDecision.Say("delayed", "idk"), rig.Reporter, 2f);
                var sink = new ListSink();
                MeetingTalker talker = NewTalker(delayed, new Fixed("rule", "hmm", MeetingDecision.Quiet("rule")));
                talker.LeadS = lead;
                talker.Sink = sink;
                rig.Run(new Dictionary<byte, MeetingTalker> { { bot, talker } }, (e, now) =>
                {
                    delayed.Release(now);
                    return true;
                });
                Assert.NotEmpty(rig.Chat);
                return sink.Items.Select(i => i.Sent.Source).Distinct().ToList();
            };

            Assert.Equal(new[] { "delayed" }, run(MeetingTalker.DefaultLeadS)); // started 3 s ahead, answers in 2 s: every line and the vote
            Assert.Equal(new[] { "rule" }, run(1f)); // started 1 s ahead, still pending at the planned moment
        }

        [Fact]
        public void Talker_ARefusedReplyFallsBackToTheRulePath()
        {
            var rig = new Rig();
            byte bot = rig.OtherThan(rig.Reporter);
            byte accuser = rig.OtherThan(rig.Reporter, bot);
            MeetingTalker talker = NewTalker(new Spy(), new RuleDecider()); // the primary always stays quiet
            float accusedAt = -1f;

            rig.Run(new Dictionary<byte, MeetingTalker> { { bot, talker } }, (e, now) =>
            {
                if (accusedAt < 0f && now - rig.MeetingStart >= 1f)
                {
                    Accuse(e, accuser, bot);
                    accusedAt = now;
                }

                return true;
            });

            Sent first = rig.Chat.First(c => c.Seat == bot);
            Assert.Equal("not_me", ((ChatPhraseCommand)first.Command).PhraseId);
            Assert.InRange(first.Time - accusedAt, 3f, 8f + 0.1f);
        }

        [Fact]
        public void Talker_AnAccusationDropsALineStillWaitingForTheFloor()
        {
            var rig = new Rig();
            byte bot = rig.OtherThan(rig.Reporter);
            byte accuser = rig.OtherThan(rig.Reporter, bot);
            var floor = new MeetingFloor();
            MeetingTalker talker = NewTalker(new Replier("hmm"), new RuleDecider(), floor);
            float accusedAt = -1f;

            rig.Run(new Dictionary<byte, MeetingTalker> { { bot, talker } }, (e, now) =>
            {
                float t = now - rig.MeetingStart;
                if (t < 14f)
                {
                    floor.TryTake(now); // another bot keeps the floor: the bot's first line waits, ready, until 14 s
                }

                if (accusedAt < 0f && t >= 13f)
                {
                    Accuse(e, accuser, bot);
                    accusedAt = now;
                }

                return true;
            });

            List<Sent> after = rig.Chat.Where(c => c.Seat == bot && c.Time > accusedAt).ToList();
            Assert.NotEmpty(after);
            Assert.Equal("not_me", ((ChatPhraseCommand)after[0].Command).PhraseId); // the stale line is gone, the reply comes first
            Assert.InRange(after[0].Time - accusedAt, 3f, 8f + 0.1f);
        }

        [Fact]
        public void Talker_AnswersAnAccusationAfterTheHostClearedTheLog()
        {
            var rig = new Rig();
            byte bot = rig.OtherThan(rig.Reporter);
            byte accuser = rig.OtherThan(rig.Reporter, bot);
            for (int i = 0; i < 5; i++)
            {
                rig.Log.Add(new ChatEvent(accuser, "hmm", new ChatArg[0]), 0f); // old acts the host has not cleared yet
            }

            int tick = 0;
            float accusedAt = -1f;
            MeetingTalker talker = NewTalker(new Replier(null), new Replier(null));
            rig.Run(new Dictionary<byte, MeetingTalker> { { bot, talker } }, (e, now) =>
            {
                if (++tick == 2)
                {
                    rig.Log.Clear(); // the host clears the log after the talker's first Step
                }

                if (accusedAt < 0f && tick > 2 && now - rig.MeetingStart >= 1f)
                {
                    Accuse(e, accuser, bot);
                    accusedAt = now;
                }

                return true;
            });

            Sent reply = Assert.Single(rig.Chat, c => c.Seat == bot);
            Assert.Equal("not_me", ((ChatPhraseCommand)reply.Command).PhraseId);
        }

        [Fact]
        public void Talker_AnswersAnAccusationInALogThatWasClearedAndRefilledBetweenTwoSteps()
        {
            var rig = new Rig();
            byte bot = rig.OtherThan(rig.Reporter);
            byte accuser = rig.OtherThan(rig.Reporter, bot);
            for (int i = 0; i < 2; i++)
            {
                rig.Log.Add(new ChatEvent(accuser, "hmm", new ChatArg[0]), 0f); // the talker has seen these 2 acts at its first Step
            }

            int tick = 0;
            MeetingTalker talker = NewTalker(new Replier(null), new Replier(null));
            rig.Run(new Dictionary<byte, MeetingTalker> { { bot, talker } }, (e, now) =>
            {
                if (++tick == 2)
                {
                    // the next meeting's log: cleared and already longer than what the talker had seen, the accusation first
                    rig.Log.Clear();
                    rig.Log.Add(new ChatEvent(accuser, "sus", new[] { ChatArg.ForSeat(bot) }), now);
                    rig.Log.Add(new ChatEvent(accuser, "hmm", new ChatArg[0]), now);
                    rig.Log.Add(new ChatEvent(accuser, "hmm", new ChatArg[0]), now);
                }

                return true;
            });

            Sent reply = Assert.Single(rig.Chat, c => c.Seat == bot);
            Assert.Equal("not_me", ((ChatPhraseCommand)reply.Command).PhraseId);
        }

        /// <summary>Says "I will vote for X" (or "no proof") in the discussion, then answers the vote stage with another seat.</summary>
        private sealed class Announcer : IMeetingDecider
        {
            private readonly byte? _announce;
            private readonly byte _otherVote;
            private readonly string _phrase;

            public Announcer(string phrase, byte? announce, byte otherVote)
            {
                _phrase = phrase;
                _announce = announce;
                _otherVote = otherVote;
            }

            public string Name => "fake";

            public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
            {
                if (state.Stage == MeetingStage.Voting)
                {
                    return Task.FromResult(new MeetingDecision { Vote = _otherVote, Source = "fake" });
                }

                return Task.FromResult(_announce == null ? MeetingDecision.Say("fake", "skip_no_proof") : MeetingDecision.Say("fake", _phrase, _announce));
            }
        }

        [Theory]
        [InlineData("vote")]
        [InlineData("sus")] // an accusation in the chat is an announcement too: a bot votes the seat it accused
        [InlineData("skip_no_proof")]
        public void Talker_VotesWhatItSaid(string phrase)
        {
            var rig = new Rig();
            byte bot = rig.Match.FirstSeatOf(Role.Polluter); // a polluter may name any seat that is not its partner
            var partners = rig.Match.Engine.ViewFor(bot).Me.Partners;
            byte[] others = rig.Match.Engine.Players.Where(p => p.IsActive && p.Seat != bot && !partners.Contains(p.Seat)).Select(p => p.Seat).ToArray();
            byte announced = others[0];
            byte decided = others[1];
            bool announcesASeat = phrase != "skip_no_proof";
            var talkers = new Dictionary<byte, MeetingTalker> { { bot, NewTalker(new Announcer(phrase, announcesASeat ? announced : (byte?)null, decided), new RuleDecider()) } };

            rig.Run(talkers);

            Assert.Contains(rig.Chat, c => ((ChatPhraseCommand)c.Command).PhraseId == phrase);
            Sent vote = Assert.Single(rig.Votes);
            Assert.True(vote.Result.IsOk);
            Assert.Equal(announcesASeat ? (byte?)announced : null, ((VoteCommand)vote.Command).Target);
        }

        // ---------- decision log ----------

        [Fact]
        public void Talker_ReportsEverySentDecisionToTheSinkWithItsReason()
        {
            Func<IMeetingDecider, ListSink> run = primary =>
            {
                var rig = new Rig();
                byte bot = rig.OtherThan(rig.Reporter);
                var sink = new ListSink();
                MeetingTalker talker = NewTalker(primary, new Fixed("rule", "hmm", MeetingDecision.Quiet("rule")));
                talker.Sink = sink;
                talker.MatchId = "ROOM9";
                rig.Run(new Dictionary<byte, MeetingTalker> { { bot, talker } });
                Assert.Equal(rig.Chat.Count + rig.Votes.Count, sink.Items.Count);
                Assert.NotEmpty(sink.Items);
                Assert.All(sink.Items, i => Assert.Equal("ROOM9", i.State.MatchId));
                return sink;
            };

            ListSink asked = run(new Fixed("fake", "idk", MeetingDecision.Quiet("fake")));
            Assert.All(asked.Items, i => Assert.Equal("fake", i.Sent.Source));
            Assert.All(asked.Items, i => Assert.Null(i.Sent.Reason));

            ListSink late = run(new Never());
            Assert.All(late.Items, i => Assert.Equal("rule", i.Sent.Source));
            Assert.All(late.Items, i => Assert.Equal("late", i.Sent.Reason));
        }

        [Fact]
        public void Talker_LogsAReplyTheModelDeclinedAsReplyDeclined()
        {
            var rig = new Rig();
            byte bot = rig.OtherThan(rig.Reporter);
            byte accuser = rig.OtherThan(rig.Reporter, bot);
            var sink = new ListSink();
            MeetingTalker talker = NewTalker(new Spy(), new RuleDecider());
            talker.Sink = sink;
            float accusedAt = -1f;

            rig.Run(new Dictionary<byte, MeetingTalker> { { bot, talker } }, (e, now) =>
            {
                if (accusedAt < 0f && now - rig.MeetingStart >= 1f)
                {
                    Accuse(e, accuser, bot);
                    accusedAt = now;
                }

                return true;
            });

            (MeetingState _, MeetingDecision reply) = sink.Items.First(i => i.Sent.PhraseId == "not_me");
            Assert.Equal("rule", reply.Source);
            Assert.Equal("reply_declined", reply.Reason);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly System.Net.HttpStatusCode _code;

            public StubHandler(System.Net.HttpStatusCode code)
            {
                _code = code;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(_code) { Content = new StringContent("{}") });
            }
        }

        [Fact]
        public void Talker_LogLineShowsTheClefFallbackReasonAndLatency()
        {
            var rig = new Rig();
            byte bot = rig.OtherThan(rig.Reporter);
            var lines = new List<string>();
            var log = new Uncycled.Bots.Neural.MeetingDecisionLog(lines.Add);
            var clef = new Uncycled.Bots.Neural.ClefDecider(new HttpClient(new StubHandler(System.Net.HttpStatusCode.InternalServerError)), "acc", "tok", new Uncycled.Bots.Neural.DailyBudget(100, () => DateTime.UtcNow), log, TimeSpan.FromSeconds(3));
            MeetingTalker talker = NewTalker(clef, new RuleDecider());
            talker.Sink = log;
            talker.MatchId = "ROOM5";

            rig.Run(new Dictionary<byte, MeetingTalker> { { bot, talker } });

            Assert.NotEmpty(lines);
            Assert.Equal(rig.Chat.Count + rig.Votes.Count, lines.Count);
            Assert.All(lines, l =>
            {
                System.Text.Json.JsonElement r = System.Text.Json.JsonDocument.Parse(l).RootElement;
                Assert.Equal("ROOM5", r.GetProperty("match").GetString());
                Assert.Equal("rule", r.GetProperty("source").GetString());
                // a vote that follows what the bot said in the chat is logged as such, no model call is made for it
                bool follows = r.GetProperty("stage").GetString() == "voting" && lines.Count(x => x.Contains("\"phrase\"")) > 0;
                Assert.Equal(follows ? "announced" : "http_500", r.GetProperty("fallback").GetString());
                Assert.True(r.GetProperty("latency_ms").GetInt64() >= 0);
            });
        }
    }
}
