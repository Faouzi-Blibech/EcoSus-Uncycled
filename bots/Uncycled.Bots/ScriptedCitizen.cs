using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>
    /// Scripted Cleaner (v2): leave a gas-warned room, heal yourself or call the free meeting when infected, pick from an open
    /// upgrade offer, call an emergency meeting after seeing a Trasher act (EmergencyRule), fix a jammed machine, repair smog, report a fresh body, bring a full bag to the
    /// machine, clean the nearest dirty spot, wander when every spot is clean. Votes by suspicion.
    /// </summary>
    public sealed class ScriptedCitizen : ScriptedBrain
    {
        private const int FixMaxPathTiles = 6;
        private const float TakenRadius = 2.5f;
        private const float TakenGraceS = 2f;
        private const int SmogMaxPathTiles = 25;
        private const float WanderStayChance = 0.3f;
        private const float EarlyDeliverChance = 0.2f;

        private readonly BotsSettings _settings;
        private readonly EmergencyRule _emergency;

        public ScriptedCitizen(BotsSettings settings, IRandom rng, IReadOnlyList<TaskTypeDef>? taskTypes = null)
            : base(settings, rng, taskTypes)
        {
            _settings = settings;
            _emergency = new EmergencyRule(settings, rng);
        }

        internal override void Watch(SeatView view) => _emergency.Watch(view, Mem, Mem.Map);

        internal override bool StillValid(BotAction action, SeatView view, bool[] fullMask) =>
            fullMask[(int)action] || (action == BotAction.CallMeeting && _emergency.Wanted && _emergency.Allowed(view, Mem, Mem.Map));

        internal override void Started(BotAction action, SeatView view)
        {
            if (action == BotAction.CallMeeting && _emergency.Wanted)
            {
                _emergency.NoteStart(Exec);
            }
        }

        internal override Choice Choose(SeatView view, bool[] mask)
        {
            MapData map = Mem.Map;
            Vec2 me = view.Me.Position;
            MapDistances dist = MapDistances.For(map);

            // 0. the gas warning of the room I stand in (public to everyone): walk out, before anything else
            if (mask[(int)BotAction.LeaveGas])
            {
                return Same(BotAction.LeaveGas, true);
            }

            // 0b. a gas warning runs: do not walk into the warned room (drop a walk that goes there)
            ZoneDef? warned = ActionQuery.WarnedRoomToAvoid(view, Mem, map);
            if (warned != null && Exec.WalksInto(warned))
            {
                return Choice.Now(BotAction.Stay);
            }

            // 1. infected: heal yourself, else call the one free meeting
            if (mask[(int)BotAction.HealSelf])
            {
                return Choice.Now(BotAction.HealSelf);
            }

            if (ActionQuery.Infected(view) && mask[(int)BotAction.CallMeeting])
            {
                return Choice.Now(BotAction.CallMeeting);
            }

            // 2. an upgrade offer is open: pick
            if (mask[(int)BotAction.PickUpgrade])
            {
                return Choice.Now(BotAction.PickUpgrade);
            }

            // 2b. a Trasher act seen and the roll passed: walk to the emergency table and call a meeting (a fresh body is reported instead, below)
            if (!mask[(int)BotAction.ReportNearest] && _emergency.Active(view, Mem, map, Exec))
            {
                return Exec.Busy && Exec.Current == BotAction.CallMeeting ? Choice.Keep : Choice.Now(BotAction.CallMeeting);
            }

            // 3. the machine is jammed: fix it when it is close or while carrying bags
            ConsoleDef? machine = ActionQuery.MachineConsole(map);
            if (mask[(int)BotAction.FixMachine] && machine != null)
            {
                if (Exec.Busy && Exec.Current == BotAction.FixMachine)
                {
                    return Choice.Keep;
                }

                // someone already stands at the machine: they are fixing it (one repairer at a time), leave it to them,
                // but only for a repair's time (fix_s + 2 s from the first sighting of the jam): after that the
                // one standing there is a bystander, and the jam has no end of its own
                bool inGrace = Mem.MachineJamSeenAt != null && view.MatchTime - Mem.MachineJamSeenAt.Value <= Mem.Config.MachineFixS + TakenGraceS;
                bool taken = inGrace && ActionQuery.SomeoneNear(view, machine.Position, TakenRadius) && Vec2.Distance(me, machine.Position) > TakenRadius;
                int d = dist.Path(me, machine.Position);
                if (!taken && d != MapDistances.Unreachable && (d <= FixMaxPathTiles || view.Me.Bags > 0))
                {
                    return Choice.Now(BotAction.FixMachine);
                }
            }

            // 4. smog: some Cleaners walk to the panel and repair it
            if (ActionQuery.SmogActive(view))
            {
                if (mask[(int)BotAction.RepairSmog])
                {
                    return Exec.Busy && Exec.Current == BotAction.RepairSmog ? Choice.Keep : Choice.Now(BotAction.RepairSmog);
                }

                ConsoleDef? panel = ActionQuery.NthOfKind(map, ConsoleKind.SmogFix, 0);
                if (panel != null && mask[(int)BotAction.GoSmogPanel] && Rng.NextFloat() < _settings.Skill
                    && dist.Path(me, panel.Position) is int p && p != MapDistances.Unreachable && p <= SmogMaxPathTiles)
                {
                    return Same(BotAction.GoSmogPanel, true);
                }
            }

            // 5. a fresh body in sight: report it
            if (mask[(int)BotAction.ReportNearest])
            {
                return Same(BotAction.ReportNearest, true);
            }

            // 6. bags: a full Cleaner brings them to the machine (a backpack carrier also when no spot is left)
            bool full = view.Me.Bags >= view.Me.BagCap;
            bool anyDirty = mask[(int)BotAction.GoDirtySpot];
            if (view.Me.Bags > 0 && !view.MachineJammed && (full || !anyDirty || Rng.NextFloat() < EarlyDeliverChance * (view.Me.Bags / (float)view.Me.BagCap)))
            {
                if (mask[(int)BotAction.Deliver])
                {
                    return Same(BotAction.Deliver, false);
                }

                if (mask[(int)BotAction.GoMachine])
                {
                    return Same(BotAction.GoMachine, false);
                }
            }

            // 7. the nearest dirty spot
            if (mask[(int)BotAction.FinishSpot])
            {
                return Same(BotAction.FinishSpot, false);
            }

            if (anyDirty)
            {
                return Same(BotAction.GoDirtySpot, false);
            }

            // 8. a full Cleaner at a jammed machine with nothing else to do waits at it (the jam is fixed above); else wander
            if (Exec.Busy)
            {
                return Choice.Keep;
            }

            if (Rng.NextFloat() < WanderStayChance)
            {
                return Choice.Do(BotAction.Stay);
            }

            int zones = Math.Min(3, map.Zones.Count);
            return zones == 0 ? Choice.Do(BotAction.Stay) : Choice.Do(BotAction.GoZone0 + Rng.NextInt(zones));
        }

        internal override void Prepare(BotAction action, SeatView view)
        {
            if (action == BotAction.PickUpgrade && Mem.UpgradeOffer != null)
            {
                Exec.UpgradeChoice = PickUpgrade(Mem.UpgradeOffer);
            }
        }

        /// <summary>A weighted draw from the offer: a backpack or a tool pays most, a heal or a mask is insurance.</summary>
        private UpgradeKind PickUpgrade(IReadOnlyList<UpgradeKind> offer)
        {
            float Weight(UpgradeKind k) => ActionQuery.UpgradeWeight(k);

            float total = offer.Sum(Weight);
            float roll = Rng.NextFloat() * total;
            foreach (UpgradeKind k in offer)
            {
                roll -= Weight(k);
                if (roll <= 0f)
                {
                    return k;
                }
            }

            return offer[offer.Count - 1];
        }

        /// <summary>Keep the running action when it is already this one.</summary>
        private Choice Same(BotAction action, bool urgent)
        {
            if (Exec.Busy && Exec.Current == action)
            {
                return Choice.Keep;
            }

            return urgent ? Choice.Now(action) : Choice.Do(action);
        }
    }
}
