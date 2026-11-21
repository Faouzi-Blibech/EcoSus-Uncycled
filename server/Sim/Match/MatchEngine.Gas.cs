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

        /// <summary>The Trasher that started the running warning.</summary>
        private byte _gasBy;

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
            _gasBy = p.Seat;
            p.GasReadyAt = ActiveTime + Config.GasCooldownS;
            Emit(new GasWarningEvent(room.Id, Config.GasWarningS));
            return CommandResult.Ok;
        }

        /// <summary>
        /// A meeting starting cancels a running warning (everyone respawns after it, so the gas would
        /// only hit an empty room): gas_done goes out, nobody is put out and the Trasher's cooldown is refunded.
        /// </summary>
        private void CancelGas()
        {
            ZoneDef? room = _gasRoom;
            if (room == null)
            {
                return;
            }

            _gasRoom = null;
            _gasLeft = 0f;
            PlayerState? by = Find(_gasBy);
            if (by != null)
            {
                by.GasReadyAt = 0f;
            }

            Emit(new GasDoneEvent(room.Id));
        }

        /// <summary>Runs on active time only (a meeting cancels the warning before it can run).</summary>
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
