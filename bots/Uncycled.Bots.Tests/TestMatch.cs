using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Uncycled.Server.Content;

namespace Uncycled.Bots.Tests
{
    /// <summary>Builds a real match from the content folder: greybox map, tuning, task types.</summary>
    internal sealed class TestMatch
    {
        public const float Dt = 0.05f;

        private TestMatch(ContentBundle content, MatchEngine engine)
        {
            Content = content;
            Engine = engine;
        }

        public ContentBundle Content { get; }

        public MatchEngine Engine { get; }

        public MapData Map => Content.Map;

        public MatchConfig Config => Content.Config;

        /// <summary>All events produced by Tick since creation (test observation only).</summary>
        public List<GameEvent> Events { get; } = new List<GameEvent>();

        public static TestMatch Create(ulong seed = 1, bool startPlaying = true)
        {
            string dir = ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
            ContentBundle content = ContentLoader.Load(dir);
            var seats = new List<SeatSetup>();
            for (int i = 0; i < content.Config.Seats; i++)
            {
                seats.Add(new SeatSetup((byte)i, "bot" + i, (byte)i));
            }

            var engine = new MatchEngine(content.Config, content.Map, seats, content.Tasks, new XorShiftRandom(seed), content.Phrases);
            var match = new TestMatch(content, engine);
            if (startPlaying)
            {
                while (engine.Phase == MatchPhase.Intro)
                {
                    match.Tick();
                }
            }

            return match;
        }

        public void Tick(float dt = Dt)
        {
            Events.AddRange(Engine.Tick(dt));
        }

        public void TickFor(float seconds)
        {
            int n = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < n; i++)
            {
                Tick();
            }
        }

        /// <summary>
        /// A dump pile at the polluter's feet with a witness record for every seat that sees it: what the v1 dump
        /// action left behind (the action is gone in v2, so the evidence is seeded).
        /// </summary>
        public Pile SeedDump(byte polluter)
        {
            PlayerState p = Engine.Find(polluter) ?? throw new InvalidOperationException("no seat " + polluter);
            string zone = Map.ZoneAt(p.Position);
            Pile pile = Engine.Piles.AddDump(p.Position, zone, Engine.MatchTime, p.Signature!.Value, polluter);
            foreach (PlayerState q in Engine.Players)
            {
                if (q != p && q.IsActive && Engine.CanSee(q.Seat, p.Position))
                {
                    Engine.Witnesses.Add(new WitnessRecord(ActKind.Dump, polluter, q.Seat, Engine.MatchTime, zone, p.Position, pile.Id, null));
                }
            }

            return pile;
        }

        /// <summary>
        /// Puts a seat out the v2 way: an emergency meeting at the button where every living seat votes it out. Ends
        /// back in Playing, everyone at the spawns (the v1 way, poison hits, is gone).
        /// </summary>
        public void EjectByVote(byte seat)
        {
            TickFor(Config.EmergencyMinSinceMeetingS + 1f);
            PlayerState caller = Engine.Players.First(p => p.IsActive && p.Seat != seat);
            Engine.TestTeleport(caller.Seat, Map.ConsolesOfKind(ConsoleKind.Emergency)[0].Position);
            if (!Engine.Apply(caller.Seat, new CallMeetingCommand()).IsOk)
            {
                throw new InvalidOperationException("the emergency meeting was refused");
            }

            for (int i = 0; i < 4000 && Engine.Meeting?.Stage != MeetingStage.Voting; i++)
            {
                Tick();
            }

            foreach (PlayerState p in Engine.Players.Where(p => p.IsActive))
            {
                Engine.Apply(p.Seat, new VoteCommand(seat));
            }

            for (int i = 0; i < 4000 && Engine.Phase != MatchPhase.Playing && Engine.Phase != MatchPhase.Ended; i++)
            {
                Tick();
            }

            if (Engine.Find(seat)!.IsActive)
            {
                throw new InvalidOperationException("the seat was not voted out");
            }
        }

        /// <summary>Test-only peek at hidden roles to choose which seat to drive.</summary>
        public byte FirstSeatOf(Role role)
        {
            foreach (PlayerState p in Engine.Players)
            {
                if (p.Role == role)
                {
                    return p.Seat;
                }
            }

            throw new InvalidOperationException("no seat with role " + role);
        }
    }
}
