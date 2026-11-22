using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// Under a policy nothing calls Prepare, so no hints are set: every v2 action that was appended must still
    /// send its command from its own defaults (the scripted brains only improve the choice with hints).
    /// </summary>
    public class NoHintTests
    {
        private static SyntheticBot Bot(TestMatch m, Role role) => new SyntheticBot(m, role) { Seat = 0, MatchTime = 100f };

        private static Sim.World.ConsoleDef Spot(TestMatch m) => m.Map.ConsolesOfKind(ConsoleKind.Task)[0];

        private static Sim.World.ConsoleDef Machine(TestMatch m) => m.Map.ConsolesOfKind(ConsoleKind.Machine)[0];

        private static Vec2 Near(TestMatch m, Sim.World.ConsoleDef c) => new MapDistances_Helper(m).WalkableNear(c.Position, 0.8f);

        /// <summary>Begin without hints, then run the action to its end.</summary>
        private static List<ICommand> Run(SyntheticBot bot, BotAction action, float maxS = 90f)
        {
            bot.Observe();
            bot.Begin(action);
            bot.RunUntilIdle(maxS);
            return bot.Sent;
        }

        [Fact]
        public void GoDirtySpot_BeginWithoutHints_SendsTheCommand()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Citizen);
            bot.Spots.Add(new SpotView(Spot(m).Id, SpotState.Dirty));

            List<ICommand> sent = Run(bot, BotAction.GoDirtySpot);

            Assert.Contains(sent, c => c is SpotDoneCommand d && d.SpotId == Spot(m).Id);
        }

        [Fact]
        public void FinishSpot_BeginWithoutHints_SendsTheCommand()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Citizen);
            bot.Position = Near(m, Spot(m));
            bot.Spots.Add(new SpotView(Spot(m).Id, SpotState.Dirty));

            List<ICommand> sent = Run(bot, BotAction.FinishSpot);

            Assert.Contains(sent, c => c is SpotDoneCommand d && d.SpotId == Spot(m).Id);
        }

        [Fact]
        public void GoMachine_BeginWithoutHints_SendsTheCommand()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Citizen);
            bot.Bags = 1;

            List<ICommand> sent = Run(bot, BotAction.GoMachine);

            Assert.Contains(sent, c => c is UseCommand u && u.ConsoleId == Machine(m).Id);
        }

        [Fact]
        public void Deliver_BeginWithoutHints_SendsTheCommand()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Citizen);
            bot.Bags = 1;
            bot.Position = Near(m, Machine(m));

            List<ICommand> sent = Run(bot, BotAction.Deliver);

            Assert.Contains(sent, c => c is UseCommand u && u.ConsoleId == Machine(m).Id);
        }

        [Fact]
        public void PickUpgrade_BeginWithoutHints_SendsTheCommand_PickedByRule()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Citizen);
            bot.Memory.UpgradeOffer = new List<UpgradeKind> { UpgradeKind.Mask, UpgradeKind.Tool, UpgradeKind.Backpack };

            List<ICommand> sent = Run(bot, BotAction.PickUpgrade);

            // the rule: a backpack, else a tool, else the first offered (not the first of the list by habit)
            Assert.Equal(UpgradeKind.Backpack, Assert.Single(sent.OfType<PickUpgradeCommand>()).Item);

            SyntheticBot other = Bot(m, Role.Citizen);
            other.Memory.UpgradeOffer = new List<UpgradeKind> { UpgradeKind.Mask, UpgradeKind.Tool };
            Assert.Equal(UpgradeKind.Tool, Assert.Single(Run(other, BotAction.PickUpgrade).OfType<PickUpgradeCommand>()).Item);
        }

        [Fact]
        public void Repollute_BeginWithoutHints_SendsTheCommand()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Polluter);
            bot.Position = Near(m, Spot(m));
            bot.Spots.Add(new SpotView(Spot(m).Id, SpotState.Clean));

            List<ICommand> sent = Run(bot, BotAction.Repollute);

            Assert.Contains(sent, c => c is RepolluteCommand r && r.SpotId == Spot(m).Id);
        }

        [Fact]
        public void PickAbility_BeginWithoutHints_SendsTheCommand_PickedByRule()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot none = Bot(m, Role.Polluter);

            // the rule: gas when a gas room holds someone to gas right now, else the virus
            Assert.Equal(TrasherAbility.Virus, Assert.Single(Run(none, BotAction.PickAbility).OfType<PickAbilityCommand>()).Ability);

            SyntheticBot victim = Bot(m, Role.Polluter);
            victim.Players.Add(SyntheticBot.Player(3, 4.5f, 4.5f)); // inside gas_market
            Assert.Equal(TrasherAbility.Gas, Assert.Single(Run(victim, BotAction.PickAbility).OfType<PickAbilityCommand>()).Ability);
        }

        [Fact]
        public void PlantVirus_BeginWithoutHints_SendsTheCommand()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Polluter);
            bot.Ability = TrasherAbility.Virus;
            bot.Position = Near(m, Spot(m));
            bot.Spots.Add(new SpotView(Spot(m).Id, SpotState.Dirty));

            List<ICommand> sent = Run(bot, BotAction.PlantVirus);

            Assert.Contains(sent, c => c is PlantVirusCommand);
        }

        [Fact]
        public void GasRoom_BeginWithoutHints_SendsTheCommand_ToTheRoomWithPlayers()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Polluter);
            bot.Ability = TrasherAbility.Gas;
            bot.Players.Add(SyntheticBot.Player(3, 4.5f, 4.5f)); // inside gas_market

            List<ICommand> sent = Run(bot, BotAction.GasRoom);

            Assert.Equal("gas_market", Assert.Single(sent.OfType<GasCommand>()).RoomId);
        }

        [Fact]
        public void GasRoom_IsMaskedOffWhenNoGasRoomHoldsAnyone()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Polluter);
            bot.Ability = TrasherAbility.Gas;
            bot.Observe();
            Assert.False(bot.Mask()[(int)BotAction.GasRoom]);

            bot.Players.Add(SyntheticBot.Player(3, 4.5f, 4.5f));
            bot.Observe();
            Assert.True(bot.Mask()[(int)BotAction.GasRoom]);
        }

        [Fact]
        public void HealSelf_BeginWithoutHints_SendsTheCommand()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Citizen);
            bot.Heals = 1;
            bot.InfectedS = 30f;

            List<ICommand> sent = Run(bot, BotAction.HealSelf);

            Assert.Contains(sent, c => c is HealCommand h && h.Target == 0);
        }

        [Fact]
        public void FixMachine_BeginWithoutHints_SendsTheCommand()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Citizen);
            bot.MachineJammed = true;
            bot.Position = Near(m, Machine(m));

            List<ICommand> sent = Run(bot, BotAction.FixMachine);

            Assert.Contains(sent, c => c is FixCommand f && f.ConsoleId == Machine(m).Id && f.Holding);
        }

        [Fact]
        public void SabotageMachine_BeginWithoutHints_SendsTheCommand()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Polluter);

            List<ICommand> sent = Run(bot, BotAction.SabotageMachine);

            Assert.Contains(sent, c => c is SabotageCommand s && s.Kind == SabotageKind.Machine);
        }

        [Fact]
        public void LeaveGas_BeginWithoutHints_SendsTheCommand_AndEndsOutsideTheRoom()
        {
            TestMatch m = TestMatch.Create();
            SyntheticBot bot = Bot(m, Role.Citizen);
            bot.Position = new Vec2(4.5f, 4.5f); // inside gas_market
            bot.Memory.GasWarnedRoom = "gas_market";

            List<ICommand> sent = Run(bot, BotAction.LeaveGas, 10f);

            Assert.Contains(sent, c => c is MoveCommand);
            Assert.False(m.Map.GasRooms.First(z => z.Id == "gas_market").Contains(bot.Position));
        }
    }
}
