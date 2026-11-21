using System;
using System.Collections.Generic;

namespace Sim.Uncycled
{
    /// <summary>
    /// One shared sabotage cooldown, counted on ActiveTime from the END of the previous sabotage;
    /// the first sabotage is locked for the opening seconds of the match (trasher.start_cooldown_s).
    /// Frozen (shown as full) while a sabotage is running.
    /// </summary>
    public sealed class SabotageCooldown
    {
        private readonly float _cooldownSeconds;

        public SabotageCooldown(float cooldownSeconds, float openingLockSeconds = 0f)
        {
            _cooldownSeconds = cooldownSeconds;
            ReadyAt = openingLockSeconds;
        }

        /// <summary>ActiveTime at which a new sabotage may start (when none is running).</summary>
        public float ReadyAt { get; private set; }

        public bool SabotageRunning { get; private set; }

        public float Remaining(float activeTime)
        {
            if (SabotageRunning)
            {
                return _cooldownSeconds;
            }

            return Math.Max(0f, ReadyAt - activeTime);
        }

        internal void OnStarted()
        {
            SabotageRunning = true;
        }

        internal void OnEnded(float activeTime)
        {
            SabotageRunning = false;
            ReadyAt = activeTime + _cooldownSeconds;
        }
    }

    /// <summary>Landfill fire: every fire_fix console must be held at the same time.</summary>
    public sealed class FireSystem
    {
        private readonly List<string> _consoleIds;
        private readonly byte?[] _holders;

        public FireSystem(IReadOnlyList<string> fireConsoleIds)
        {
            _consoleIds = new List<string>(fireConsoleIds);
            _holders = new byte?[_consoleIds.Count];
        }

        public bool Active { get; private set; }

        public float Countdown { get; internal set; }

        public IReadOnlyList<string> ConsoleIds => _consoleIds;

        public bool Available => _consoleIds.Count > 0;

        public bool AllHeld
        {
            get
            {
                if (_holders.Length == 0)
                {
                    return false;
                }

                foreach (byte? h in _holders)
                {
                    if (h == null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public byte? HolderAt(int index) => _holders[index];

        public int IndexOf(string consoleId) => _consoleIds.IndexOf(consoleId);

        public List<string> HeldConsoleIds()
        {
            var list = new List<string>();
            for (int i = 0; i < _holders.Length; i++)
            {
                if (_holders[i] != null)
                {
                    list.Add(_consoleIds[i]);
                }
            }

            return list;
        }

        internal void Start(float countdown)
        {
            Active = true;
            Countdown = countdown;
            ReleaseAll();
        }

        internal void Stop()
        {
            Active = false;
            Countdown = 0f;
            ReleaseAll();
        }

        /// <summary>Seat holds the console. False when someone else already holds it.</summary>
        internal bool TryHold(byte seat, int index)
        {
            byte? current = _holders[index];
            if (current != null && current.Value != seat)
            {
                return false;
            }

            Release(seat);
            _holders[index] = seat;
            return true;
        }

        internal void Release(byte seat)
        {
            for (int i = 0; i < _holders.Length; i++)
            {
                if (_holders[i] == seat)
                {
                    _holders[i] = null;
                }
            }
        }

        internal void ReleaseAt(int index)
        {
            _holders[index] = null;
        }

        private void ReleaseAll()
        {
            for (int i = 0; i < _holders.Length; i++)
            {
                _holders[i] = null;
            }
        }
    }

    /// <summary>
    /// Recycling Machine jam: deliveries are refused while Jammed. One Cleaner fixes it by holding fix at the
    /// machine for machine.fix_s; leaving range drops the repair (same shape as SmogSystem).
    /// </summary>
    public sealed class MachineSystem
    {
        public MachineSystem(string? consoleId)
        {
            ConsoleId = consoleId;
        }

        public string? ConsoleId { get; }

        public bool Available => ConsoleId != null;

        public bool Jammed { get; private set; }

        public byte? RepairSeat { get; private set; }

        public float RepairElapsed { get; private set; }

        internal void Jam()
        {
            Jammed = true;
            CancelRepair();
        }

        internal void Clear()
        {
            Jammed = false;
            CancelRepair();
        }

        internal void BeginRepair(byte seat)
        {
            RepairSeat = seat;
            RepairElapsed = 0f;
        }

        internal void AdvanceRepair(float dt)
        {
            RepairElapsed += dt;
        }

        internal void CancelRepair()
        {
            RepairSeat = null;
            RepairElapsed = 0f;
        }
    }

    /// <summary>
    /// Smog: citizen vision eases toward smog_min while active and back when cleared.
    /// One citizen clears it by staying at the smog panel for a fixed time.
    /// </summary>
    public sealed class SmogSystem
    {
        public SmogSystem(string? panelConsoleId)
        {
            PanelConsoleId = panelConsoleId;
        }

        public string? PanelConsoleId { get; }

        public bool Available => PanelConsoleId != null;

        public bool Active { get; private set; }

        /// <summary>0 = clear air, 1 = full smog.</summary>
        public float Level { get; private set; }

        public byte? RepairSeat { get; private set; }

        public float RepairElapsed { get; private set; }

        public float CitizenVision(float normalVision, float smogMinVision) =>
            normalVision + (smogMinVision - normalVision) * Level;

        internal void Start()
        {
            Active = true;
            CancelRepair();
        }

        internal void Clear()
        {
            Active = false;
            CancelRepair();
        }

        /// <summary>Moves Level toward the target over easeSeconds.</summary>
        internal void Ease(float dt, float easeSeconds)
        {
            float step = easeSeconds > 0f ? dt / easeSeconds : 1f;
            if (Active)
            {
                Level = Math.Min(1f, Level + step);
            }
            else
            {
                Level = Math.Max(0f, Level - step);
            }
        }

        internal void BeginRepair(byte seat)
        {
            RepairSeat = seat;
            RepairElapsed = 0f;
        }

        internal void AdvanceRepair(float dt)
        {
            RepairElapsed += dt;
        }

        internal void CancelRepair()
        {
            RepairSeat = null;
            RepairElapsed = 0f;
        }
    }
}
