using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>
    /// Walks a cached A* path at a fixed speed. Next gives the position the bot should move to this
    /// step; the caller sends it as a move command. A goal expires after path length / speed x 1.5
    /// + 3 s so a bot that cannot get there never freezes on it.
    /// </summary>
    public sealed class Navigator
    {
        private const float ArriveEps = 0.02f;
        private const float OffPathDistance = 1.6f;
        private const float TimeoutFactor = 1.5f;
        private const float TimeoutExtraS = 3f;
        private const float StraightDot = 0.999f;
        private const float MinLen = 1e-6f;

        private readonly WalkGrid _grid;
        private readonly float _speed;
        private List<Vec2> _path = new List<Vec2>();
        private int _idx;
        private Vec2 _goal;
        private bool _active;
        private float _deadline;

        public Navigator(WalkGrid grid, float speedTilesPerS)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _speed = speedTilesPerS;
        }

        public bool Arrived { get; private set; }

        public bool TimedOut { get; private set; }

        /// <summary>True while a goal is set and neither reached nor expired.</summary>
        public bool HasGoal => _active;

        public Vec2 Goal => _goal;

        /// <summary>Plans a path; false (and no goal) when none exists.</summary>
        public bool SetGoal(Vec2 from, Vec2 goal, float now)
        {
            Clear();
            if (!Plan(from, goal))
            {
                return false;
            }

            _goal = goal;
            _active = true;
            _deadline = now + (_path.Count - 1) / _speed * TimeoutFactor + TimeoutExtraS;
            return true;
        }

        /// <summary>Position to move to, or null when arrived, timed out or no goal.</summary>
        public Vec2? Next(Vec2 current, float dt, float now)
        {
            if (!_active)
            {
                return null;
            }

            if (now > _deadline)
            {
                TimedOut = true;
                _active = false;
                return null;
            }

            AdvanceIndex(current);
            if (_idx < _path.Count && Vec2.Distance(current, _path[_idx]) > OffPathDistance)
            {
                // Pushed off the path (a rejected move, a teleport): plan again from here.
                if (!Plan(current, _goal))
                {
                    TimedOut = true;
                    _active = false;
                    return null;
                }

                AdvanceIndex(current);
            }

            if (_idx >= _path.Count)
            {
                Arrived = true;
                _active = false;
                return null;
            }

            float remaining = _speed * dt;
            Vec2 pos = current;
            int i = _idx;
            while (remaining > MinLen && i < _path.Count)
            {
                Vec2 d = _path[i] - pos;
                float len = d.Length;
                if (len <= remaining)
                {
                    pos = _path[i];
                    remaining -= len;
                    i++;
                    if (i < _path.Count && len > MinLen)
                    {
                        // Stop exactly on a turn so the move segment never cuts a corner.
                        Vec2 nd = _path[i] - pos;
                        float nl = nd.Length;
                        if (nl > MinLen && (d.X * nd.X + d.Y * nd.Y) / (len * nl) < StraightDot)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    pos = pos + d * (remaining / len);
                    remaining = 0f;
                }
            }

            return pos;
        }

        public void Clear()
        {
            _path = new List<Vec2>();
            _idx = 0;
            _active = false;
            Arrived = false;
            TimedOut = false;
        }

        private bool Plan(Vec2 from, Vec2 goal)
        {
            List<Vec2>? path = AStar.FindPath(_grid, from, goal);
            if (path == null || path.Count == 0)
            {
                return false;
            }

            _path = path;
            _idx = 0;
            return true;
        }

        /// <summary>Moves the open-waypoint index past every waypoint the bot has reached or passed.</summary>
        private void AdvanceIndex(Vec2 current)
        {
            while (_idx < _path.Count)
            {
                Vec2 wp = _path[_idx];
                bool passed = Vec2.Distance(current, wp) <= ArriveEps;
                if (!passed && _idx > 0)
                {
                    Vec2 dir = wp - _path[_idx - 1];
                    Vec2 rel = current - wp;
                    passed = rel.X * dir.X + rel.Y * dir.Y >= 0f && Vec2.Distance(current, wp) < OffPathDistance;
                }

                if (!passed)
                {
                    return;
                }

                _idx++;
            }
        }
    }
}
