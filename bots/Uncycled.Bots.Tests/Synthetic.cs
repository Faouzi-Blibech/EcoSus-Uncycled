using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// A hand-made world for one bot seat: the test sets where everything is and gets a SeatView.
    /// No engine runs; commands the executor sends are recorded and moves are applied to the position.
    /// </summary>
    internal sealed class SyntheticBot
    {
        public const float Dt = 0.05f;

        private readonly TestMatch _match;
        private readonly MapData _map;

        public SyntheticBot(TestMatch match, Role role = Role.Citizen, Vec2? position = null, MapData? map = null)
        {
            _match = match;
            _map = map ?? match.Map;
            Role = role;
            Position = position ?? new Vec2(10.5f, 12.5f);
            Nav = new Navigator(_map.Walk, match.Config.SpeedTilesPerS);
            Memory = new BotMemory(_map, match.Config);
            Executor = new ActionExecutor(Nav, new MoveSender()) { Memory = Memory, TaskTypes = match.Content.Tasks };
        }

        public Role Role { get; }

        public byte Seat { get; set; }

        public Vec2 Position { get; set; }

        public MatchPhase Phase { get; set; } = MatchPhase.Playing;

        public PlayerStatus Status { get; set; } = PlayerStatus.Active;

        public float MatchTime { get; set; } = 30f;

        public int EmergenciesLeft { get; set; } = 2;

        public int Poison { get; set; }

        public List<byte> Partners { get; } = new List<byte>();

        public List<PlayerView> Players { get; } = new List<PlayerView>();

        public List<PileView> Piles { get; } = new List<PileView>();

        public List<TaskInfo> Tasks { get; } = new List<TaskInfo>();

        public List<SpotView> Spots { get; } = new List<SpotView>();

        public SabotageView? Sabotage { get; set; }

        public bool MachineJammed { get; set; }

        public MeetingView? Meeting { get; set; }

        public CooldownView? Cooldowns { get; set; }

        public Navigator Nav { get; }

        public BotMemory Memory { get; }

        public ActionExecutor Executor { get; }

        public List<ICommand> Sent { get; } = new List<ICommand>();

        public SeatView View()
        {
            CooldownView? cd = Cooldowns ?? (Role == Role.Polluter ? new CooldownView(0f, 0f, 0f) : null);
            var me = new MyView(Seat, Role, Partners, null, Status, Position, 4f, Role == Role.Citizen ? Poison : (int?)null, cd, Tasks);
            return new SeatView(Seat, Phase, 0f, MatchTime, 600f, DeadlineClock.MatchTime, MatchTime, 10f, EmergenciesLeft,
                me, Players, Piles, Sabotage, Meeting, null, null, Spots, MachineJammed);
        }

        public void Observe()
        {
            Memory.Observe(View(), MatchTime, new List<WitnessRecord>());
        }

        public bool[] Mask() => ActionCatalogue.FullMask(View(), Memory, _map);

        public void Begin(BotAction action)
        {
            Executor.Begin(action, View(), _map, MatchTime);
        }

        public void Step()
        {
            Observe();
            Executor.Step(View(), Dt, MatchTime, Send);
            MatchTime += Dt;
        }

        public float RunUntilIdle(float maxSeconds)
        {
            int max = (int)Math.Round(maxSeconds / Dt);
            int i = 0;
            while (i < max && (i == 0 || Executor.Busy))
            {
                Step();
                i++;
            }

            return i * Dt;
        }

        private void Send(ICommand command)
        {
            Sent.Add(command);
            if (command is MoveCommand move)
            {
                Position = move.Position;
            }
        }

        public static PlayerView Player(byte seat, float x, float y) => new PlayerView(seat, new Vec2(x, y), seat, false);

        /// <summary>A tiny map for reach tests: width x height open cells, an optional wall column.</summary>
        public static MapData TinyMap(int width, int height, int? wallColumn, IReadOnlyList<ConsoleDef> consoles, IReadOnlyList<ZoneDef>? zones = null)
        {
            var walk = new bool[width * height];
            var sight = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool wall = y == 0 || y == height - 1 || x == 0 || x == width - 1 || (wallColumn != null && x == wallColumn.Value);
                    walk[y * width + x] = !wall;
                    sight[y * width + x] = wall;
                }
            }

            return new MapData(width, height, walk, sight, zones ?? new List<ZoneDef>(), consoles, new List<Vec2> { new Vec2(1.5f, 1.5f) }, "tiny", "v1");
        }
    }
}
