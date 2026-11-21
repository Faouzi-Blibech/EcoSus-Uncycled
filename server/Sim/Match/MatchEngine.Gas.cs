using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.World;

namespace Sim.Match
{
    public sealed partial class MatchEngine
    {
        /// <summary>The room whose gas warning is running; null when none runs.</summary>
        private ZoneDef? _gasRoom;

        /// <summary>Active seconds until the running warning ends.</summary>
        private float _gasLeft;

        // ---------- toxic gas (spec section 6) ----------

        private CommandResult ApplyGas(PlayerState p, GasCommand c)
        {
            if (Phase != MatchPhase.Playing)
            {
                return Reject(RejectReason.WrongPhase);
            }

            if (!p.IsActive || !p.IsPolluter)
            {
                return Reject(RejectReason.NotAllowed);
            }

            if (p.Ability != TrasherAbility.Gas)
            {
                return Reject(RejectReason.NotUnlocked);
            }

            ZoneDef? room = null;
            foreach (ZoneDef z in Map.GasRooms)
            {
                if (z.Id == c.RoomId)
                {
                    room = z;
                    break;
                }
            }

            if (room == null)
            {
                return Reject(RejectReason.TargetInvalid);
            }

            if (_gasRoom != null || ActiveTime + Epsilon < p.GasReadyAt)
            {
                return Reject(RejectReason.OnCooldown);
            }

            _gasRoom = room;
            _gasLeft = Config.GasWarningS;
            p.GasReadyAt = ActiveTime + Config.GasCooldownS;
            Emit(new GasWarningEvent(room.Id, Config.GasWarningS));
            return CommandResult.Ok;
        }

        /// <summary>Runs on active time only: a meeting or an ejection freezes the warning.</summary>
        private void TickGas(float dt)
        {
            ZoneDef? room = _gasRoom;
            if (room == null)
            {
                return;
            }

            _gasLeft -= dt;
            if (_gasLeft > Epsilon)
            {
                return;
            }

            _gasRoom = null;
            _gasLeft = 0f;
            var inside = new List<PlayerState>();
            foreach (PlayerState q in _players)
            {
                if (q.IsActive && q.IsCitizen && room.Contains(q.Position))
                {
                    inside.Add(q);
                }
            }

            foreach (PlayerState q in inside)
            {
                if (q.Masks > 0)
                {
                    q.Masks--;
                    Emit(new MaskUsedEvent(q.Seat));
                }
                else
                {
                    MakeOut(q, OutCause.Gas);
                }
            }

            Emit(new GasDoneEvent(room.Id));
        }
    }
}
