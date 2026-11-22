using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>
    /// A Cleaner that sees a Trasher act walks to the emergency table and calls a meeting with the chance
    /// BotsSettings.EmergencyOnWitnessChance, one roll per act, only while the seat has emergency calls left, after its reaction
    /// delay. An act is only what the seat sees: a re-pollute it witnessed (the engine's witness record), the machine jam going
    /// up with the machine in its sight, or a gas warning going up with the gas room in its sight (or the seat inside it). The
    /// public alarms heard from afar are no trigger, and a planted virus is seen by nobody but the planter.
    /// One instance per seat: the scripted Cleaner and the live NeuralBrain use Watch/Active/Allowed (the base class adds the
    /// reaction delay), the training host's network seats use Watch/Holds/Step like the report rule.
    /// Trashers are not forced.
    /// </summary>
    public sealed class EmergencyRule
    {
        private const float SightEps = 0.001f;

        private readonly BotsSettings _settings;
        private readonly IRandom _rng;
        private bool _init;
        private int _seen;
        private bool _prevJam;
        private bool _prevGas;
        private bool _armed;
        private float _delayLeft;
        private bool _running;

        public EmergencyRule(BotsSettings settings, IRandom rng)
        {
            _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
            _rng = rng ?? throw new System.ArgumentNullException(nameof(rng));
        }

        /// <summary>True from a passed roll until the walk to the table ends (or the call stopped being allowed).</summary>
        public bool Wanted { get; private set; }

        /// <summary>Call once per tick, after the memory observed the view: finds the acts seen since the last call and rolls once for each.</summary>
        public void Watch(SeatView view, BotMemory mem, MapData map)
        {
            bool jam = view.MachineJammed;
            bool gas = view.Gas != null;
            int count = mem.Witnessed.Count;
            if (!_init)
            {
                // what a seat took over mid-match already knows is no new act
                _init = true;
                _seen = count;
                _prevJam = jam;
                _prevGas = gas;
                return;
            }

            if (count < _seen)
            {
                _seen = count;
            }

            int acts = 0;
            for (int i = _seen; i < count; i++)
            {
                if (mem.Witnessed[i].Act == ActKind.Repollute)
                {
                    acts++;
                }
            }

            _seen = count;
            // the two public alarms count only for a seat that sees what they hit: the machine in its sight, the gas room in sight or around it
            if (jam && !_prevJam && SeesMachine(view, map))
            {
                acts++;
            }

            if (gas && !_prevGas && SeesGasRoom(view, map))
            {
                acts++;
            }

            _prevJam = jam;
            _prevGas = gas;
            if (!ActionQuery.Playing(view) || view.Me.Role != Role.Citizen)
            {
                Wanted = false;
                return;
            }

            for (int i = 0; i < acts; i++)
            {
                if (Allowed(view, mem, map) && _rng.NextFloat() < _settings.EmergencyOnWitnessChance)
                {
                    Wanted = true;
                }
            }
        }

        private static bool InSight(SeatView view, MapData map, Vec2 point) =>
            Vec2.Distance(view.Me.Position, point) <= view.Me.Vision + SightEps && map.Sight.HasLineOfSight(view.Me.Position, point);

        private static bool SeesMachine(SeatView view, MapData map)
        {
            ConsoleDef? machine = ActionQuery.MachineConsole(map);
            return machine != null && InSight(view, map, machine.Position);
        }

        /// <summary>The seat stands in the warned room, or sees its centre or the point of it nearest to the seat.</summary>
        private static bool SeesGasRoom(SeatView view, MapData map)
        {
            ZoneDef? room = null;
            foreach (ZoneDef z in map.GasRooms)
            {
                if (z.Id == view.Gas!.Room)
                {
                    room = z;
                    break;
                }
            }

            if (room == null)
            {
                return false;
            }

            Vec2 me = view.Me.Position;
            if (room.Contains(me))
            {
                return true;
            }

            var nearest = new Vec2(Clamp(me.X, room.X, room.X + room.Width), Clamp(me.Y, room.Y, room.Y + room.Height));
            return InSight(view, map, nearest) || InSight(view, map, new Vec2(room.X + room.Width / 2f, room.Y + room.Height / 2f));
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>A call is possible now: a call left, no fire, the meeting cooldown over, a table to walk to that no gas warning keeps the seat out of.</summary>
        public bool Allowed(SeatView view, BotMemory mem, MapData map)
        {
            if (!ActionQuery.Playing(view) || !ActionCatalogue.EmergencyAllowed(view, mem))
            {
                return false;
            }

            ConsoleDef? table = ActionQuery.NthOfKind(map, ConsoleKind.Emergency, 0);
            if (table == null)
            {
                return false;
            }

            ZoneDef? warned = ActionQuery.WarnedRoomToAvoid(view, mem, map);
            return warned == null || !warned.Contains(table.Position);
        }

        /// <summary>Scripted Cleaner and live NeuralBrain: the rule has the seat now (a roll passed and the call is still allowed, or its walk runs).</summary>
        public bool Active(SeatView view, BotMemory mem, MapData map, ActionExecutor exec)
        {
            if (!ActionQuery.Playing(view))
            {
                Reset();
                return false;
            }

            if (_running)
            {
                if (exec.Busy && exec.Current == BotAction.CallMeeting)
                {
                    return true;
                }

                _running = false;
                Wanted = false;
                return false;
            }

            if (Wanted && !Allowed(view, mem, map))
            {
                Wanted = false;
            }

            return Wanted;
        }

        /// <summary>Call when the walk to the table has begun.</summary>
        public void NoteStart(ActionExecutor exec)
        {
            _running = exec.Busy && exec.Current == BotAction.CallMeeting;
            if (!_running)
            {
                Wanted = false;
            }
        }

        /// <summary>Training host: true while the rule has the seat (reaction delay, walk, or a roll passed and the call is allowed). The frame is a placeholder.</summary>
        public bool Holds(SeatView view, BotMemory mem, MapData map, ActionExecutor exec)
        {
            if (_armed && !Allowed(view, mem, map))
            {
                _armed = false;
                Wanted = false;
            }

            return _armed || Active(view, mem, map, exec);
        }

        /// <summary>Training host: advance a held seat by dt seconds (call only while Holds is true): arm the reaction delay, then start the walk.</summary>
        public void Step(float dt, SeatView view, BotMemory mem, MapData map, ActionExecutor exec, float now)
        {
            if (_running)
            {
                return;
            }

            if (!_armed)
            {
                _armed = true;
                _delayLeft = ScriptedBrain.ReactionDelayOf(_settings, _rng);
            }

            _delayLeft -= dt;
            if (_delayLeft > 0f)
            {
                return;
            }

            _armed = false;
            if (Wanted && Allowed(view, mem, map))
            {
                new PolicyChoice(BotAction.CallMeeting).ApplyTo(exec);
                exec.Begin(BotAction.CallMeeting, view, map, now);
                NoteStart(exec);
            }
            else
            {
                Wanted = false;
            }
        }

        private void Reset()
        {
            Wanted = false;
            _armed = false;
            _running = false;
        }
    }
}
