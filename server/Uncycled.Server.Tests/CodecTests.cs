using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Sim.Core;
using Sim.Match;
using Uncycled.Server.Net;
using Xunit;

namespace Uncycled.Server.Tests
{
    public class CodecTests
    {
        private static ClientMessage Parse(string json)
        {
            bool ok = Codec.TryParse(Encoding.UTF8.GetBytes(json), out ClientMessage? msg);
            Assert.True(ok);
            Assert.NotNull(msg);
            return msg!;
        }

        [Fact]
        public void Move_RoundTrips()
        {
            ClientMessage m = Parse("{\"t\":\"move\",\"pos\":[12.5,8.25],\"seq\":42}");
            Assert.True(Codec.TryToCommand(m, out ICommand? cmd, out string? error));
            Assert.Null(error);
            var move = Assert.IsType<MoveCommand>(cmd);
            Assert.Equal(12.5f, move.Position.X);
            Assert.Equal(8.25f, move.Position.Y);
            Assert.Equal(42L, move.Seq);
        }

        [Fact]
        public void Vote_SkipAndTarget()
        {
            Assert.True(Codec.TryToCommand(Parse("{\"t\":\"vote\",\"skip\":true}"), out ICommand? skip, out _));
            Assert.True(Assert.IsType<VoteCommand>(skip).IsSkip);

            Assert.True(Codec.TryToCommand(Parse("{\"t\":\"vote\",\"target\":3}"), out ICommand? vote, out _));
            Assert.Equal((byte)3, Assert.IsType<VoteCommand>(vote).Target);
        }

        [Fact]
        public void ChatPhrase_ParsesSeatAndZoneArgs()
        {
            ClientMessage m = Parse("{\"t\":\"chat_phrase\",\"id\":\"saw_dump\",\"args\":[2,\"market\"]}");
            Assert.True(Codec.TryToCommand(m, out ICommand? cmd, out _));
            var chat = Assert.IsType<ChatPhraseCommand>(cmd);
            Assert.Equal("saw_dump", chat.PhraseId);
            Assert.Equal(2, chat.Args.Count);
            Assert.Equal(ChatArgKind.Seat, chat.Args[0].Kind);
            Assert.Equal((byte)2, chat.Args[0].Seat);
            Assert.Equal(ChatArgKind.Zone, chat.Args[1].Kind);
            Assert.Equal("market", chat.Args[1].Zone);
        }

        [Theory]
        [InlineData("{\"t\":\"move\",\"pos\":[1e999,2],\"seq\":1}", "bad_coords")]
        [InlineData("{\"t\":\"move\",\"pos\":[1],\"seq\":1}", "bad_field")]
        [InlineData("{\"t\":\"move\",\"pos\":[1,2]}", "bad_field")]
        [InlineData("{\"t\":\"hit\",\"target\":999}", "bad_field")]
        [InlineData("{\"t\":\"sabotage\",\"kind\":\"rain\"}", "bad_field")]
        [InlineData("{\"t\":\"teleport\"}", "unknown_type")]
        public void BadInput_IsRejectedWithCode(string json, string expected)
        {
            ClientMessage m = Parse(json);
            Assert.False(Codec.TryToCommand(m, out ICommand? cmd, out string? error));
            Assert.Null(cmd);
            Assert.Equal(expected, error);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[1,2,3]")]
        [InlineData("{\"x\":1}")]
        [InlineData("{\"t\":5}")]
        public void NonMessages_DoNotParse(string json)
        {
            Assert.False(Codec.TryParse(Encoding.UTF8.GetBytes(json), out ClientMessage? msg));
            Assert.Null(msg);
        }

        [Fact]
        public void ServerMessages_AreValidJsonWithExpectedFields()
        {
            using (JsonDocument pong = JsonDocument.Parse(Codec.Pong(12.5, 99.25)))
            {
                Assert.Equal("pong", pong.RootElement.GetProperty("t").GetString());
                Assert.Equal(12.5, pong.RootElement.GetProperty("c").GetDouble());
                Assert.Equal(99.25, pong.RootElement.GetProperty("s").GetDouble());
            }

            using (JsonDocument rej = JsonDocument.Parse(Codec.Rejected("hit", "on_cooldown")))
            {
                Assert.Equal("action_rejected", rej.RootElement.GetProperty("t").GetString());
                Assert.Equal("hit", rej.RootElement.GetProperty("action").GetString());
                Assert.Equal("on_cooldown", rej.RootElement.GetProperty("reason").GetString());
            }

            using (JsonDocument hello = JsonDocument.Parse(Codec.HelloOk()))
            {
                Assert.Equal("hello_ok", hello.RootElement.GetProperty("t").GetString());
            }
        }

        [Fact]
        public void Meeting_started_carries_the_living_seats()
        {
            var ev = new MeetingStartedEvent(2, MeetingCause.Report, null, new List<Sim.Uncycled.ClueInfo>(), 0.5f, 60f, new List<byte> { 0, 2, 5 });

            using (JsonDocument doc = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(ev)!))
            {
                Assert.Equal("meeting_started", doc.RootElement.GetProperty("t").GetString());
                Assert.Equal(new[] { 0, 2, 5 }, doc.RootElement.GetProperty("living").EnumerateArray().Select(x => x.GetInt32()).ToArray());
            }
        }

        [Fact]
        public void Welcome_meeting_state_carries_my_chat()
        {
            var map = new Sim.World.MapData(
                2, 2, new bool[4], new bool[4], new List<Sim.World.ZoneDef>(), new List<Sim.World.ConsoleDef>(), new List<Vec2> { new Vec2(0.5f, 0.5f) }, "test", "v1");
            var content = new Uncycled.Server.Content.ContentBundle(
                new MatchConfig(), "{}", new List<Sim.Uncycled.TaskTypeDef>(), "[]", map);
            var me = new MyView(
                1, Role.Citizen, new List<byte>(), null, PlayerStatus.Active, new Vec2(0.5f, 0.5f), 4f, null, null, new List<TaskInfo>());
            var meeting = new MeetingView(
                MeetingStage.Discussion, 20f, 2, MeetingCause.Report, new List<Sim.Uncycled.ClueInfo>(), new List<byte>(), false, null, 0.5f, 3, new List<byte> { 0, 1, 4 });
            var view = new SeatView(
                1, MatchPhase.Meeting, 20f, 100f, 600f, DeadlineClock.MatchTime, 500f, 10f, 2, me, new List<PlayerView>(), new List<PileView>(), null, meeting, null);

            string json = Uncycled.Server.Rooms.Wire.Welcome("BCDF", 1, "tok", 0, content, "playing", view, new List<Uncycled.Server.Rooms.Seat>(), _ => false);

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement m = doc.RootElement.GetProperty("state").GetProperty("meeting");
                Assert.Equal(3, m.GetProperty("my_chat").GetInt32());
                Assert.Equal(new[] { 0, 1, 4 }, m.GetProperty("living").EnumerateArray().Select(x => x.GetInt32()).ToArray());
                Assert.True(m.TryGetProperty("my_vote", out _));
            }
        }

        [Fact]
        public void Welcome_lists_every_seat_with_out_flag()
        {
            var map = new Sim.World.MapData(
                2, 2, new bool[4], new bool[4], new List<Sim.World.ZoneDef>(), new List<Sim.World.ConsoleDef>(), new List<Vec2> { new Vec2(0.5f, 0.5f) }, "test", "v1");
            var content = new Uncycled.Server.Content.ContentBundle(
                new MatchConfig(), "{}", new List<Sim.Uncycled.TaskTypeDef>(), "[]", map);
            var seats = new List<Uncycled.Server.Rooms.Seat>();
            string[] names = { "Ana", "Bo", "Cy" };
            for (int i = 0; i < 3; i++)
            {
                seats.Add(new Uncycled.Server.Rooms.Seat((byte)i)
                {
                    Name = names[i],
                    Color = (byte)(i + 4),
                    Control = i == 1 ? Uncycled.Server.Rooms.SeatControl.Bot : Uncycled.Server.Rooms.SeatControl.Human,
                });
            }

            string json = Uncycled.Server.Rooms.Wire.Welcome("BCDF", 0, "tok", 0, content, "playing", null, seats, s => s == 2);

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement arr = doc.RootElement.GetProperty("seats");
                Assert.Equal(3, arr.GetArrayLength());
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(i, arr[i].GetProperty("seat").GetInt32());
                    Assert.Equal(names[i], arr[i].GetProperty("name").GetString());
                    Assert.Equal(i + 4, arr[i].GetProperty("color").GetInt32());
                    Assert.Equal(i != 1, arr[i].GetProperty("human").GetBoolean());
                    Assert.Equal(i == 2, arr[i].GetProperty("out").GetBoolean());
                }
            }
        }

        [Fact]
        public void MatchStarted_lists_every_seat_with_name()
        {
            var seats = new List<Uncycled.Server.Rooms.Seat>();
            string[] names = { "Ana", "Bot 2", "Cy" };
            for (int i = 0; i < 3; i++)
            {
                seats.Add(new Uncycled.Server.Rooms.Seat((byte)i)
                {
                    Name = names[i],
                    Color = (byte)(i + 4),
                    Control = i == 1 ? Uncycled.Server.Rooms.SeatControl.Bot : Uncycled.Server.Rooms.SeatControl.Human,
                });
            }

            seats.Add(new Uncycled.Server.Rooms.Seat(3));
            var ev = new MatchStartedEvent(0, Role.Citizen, new List<byte>(), null, new List<TaskInfo>(), new Vec2(1f, 2f), 5f);

            string json = Uncycled.Server.Rooms.Wire.Event(ev, seats, s => s == 2)!;

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement arr = doc.RootElement.GetProperty("seats");
                Assert.Equal(3, arr.GetArrayLength());
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(i, arr[i].GetProperty("seat").GetInt32());
                    Assert.Equal(names[i], arr[i].GetProperty("name").GetString());
                    Assert.Equal(i + 4, arr[i].GetProperty("color").GetInt32());
                    Assert.Equal(i != 1, arr[i].GetProperty("human").GetBoolean());
                    Assert.Equal(i == 2, arr[i].GetProperty("out").GetBoolean());
                }
            }
        }

        [Fact]
        public void SpotDone_ParsesTheSpotId_AndRejectsAMissingOne()
        {
            Assert.True(Codec.TryToCommand(Parse("{\"t\":\"spot_done\",\"spot\":\"market_bins\"}"), out ICommand? cmd, out _));
            Assert.Equal("market_bins", Assert.IsType<SpotDoneCommand>(cmd).SpotId);

            Assert.False(Codec.TryToCommand(Parse("{\"t\":\"spot_done\"}"), out _, out string? error));
            Assert.Equal("bad_field", error);
        }

        [Fact]
        public void SpotEvents_UseKindSpotAndSpotCleaned()
        {
            var useOk = new UseOkEvent(2, "market_bins", ConsoleKind.Task, null, "sort_waste", null);
            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(useOk)!))
            {
                Assert.Equal("use_ok", d.RootElement.GetProperty("t").GetString());
                Assert.Equal("spot", d.RootElement.GetProperty("kind").GetString());
                Assert.Equal("sort_waste", d.RootElement.GetProperty("type").GetString());
                Assert.False(d.RootElement.TryGetProperty("task", out _));
            }

            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new SpotCleanedEvent(2, "market_bins", 1))!))
            {
                Assert.Equal("spot_cleaned", d.RootElement.GetProperty("t").GetString());
                Assert.Equal("market_bins", d.RootElement.GetProperty("spot").GetString());
                Assert.Equal(1, d.RootElement.GetProperty("bags").GetInt32());
            }
        }

        [Theory]
        [InlineData("backpack", UpgradeKind.Backpack)]
        [InlineData("tool", UpgradeKind.Tool)]
        [InlineData("heal", UpgradeKind.Heal)]
        [InlineData("mask", UpgradeKind.Mask)]
        public void PickUpgrade_ParsesTheItem(string item, UpgradeKind expected)
        {
            Assert.True(Codec.TryToCommand(Parse("{\"t\":\"pick_upgrade\",\"item\":\"" + item + "\"}"), out ICommand? cmd, out _));
            Assert.Equal(expected, Assert.IsType<PickUpgradeCommand>(cmd).Item);
        }

        [Theory]
        [InlineData("{\"t\":\"pick_upgrade\"}")]
        [InlineData("{\"t\":\"pick_upgrade\",\"item\":\"sword\"}")]
        public void PickUpgrade_RejectsAMissingOrUnknownItem(string json)
        {
            Assert.False(Codec.TryToCommand(Parse(json), out _, out string? error));
            Assert.Equal("bad_field", error);
        }

        [Fact]
        public void MachineEvents_UseTheProtocolNames()
        {
            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new UseOkEvent(2, "machine", ConsoleKind.Machine, null, null, null))!))
            {
                Assert.Equal("machine", d.RootElement.GetProperty("kind").GetString());
            }

            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new DeliveredEvent(2, 2, 20f))!))
            {
                Assert.Equal("delivered", d.RootElement.GetProperty("t").GetString());
                Assert.Equal(2, d.RootElement.GetProperty("bags").GetInt32());
                Assert.Equal(20.0, d.RootElement.GetProperty("meter").GetDouble(), 3);
            }

            var items = new List<UpgradeKind> { UpgradeKind.Backpack, UpgradeKind.Tool, UpgradeKind.Heal, UpgradeKind.Mask };
            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new UpgradeOfferEvent(2, items))!))
            {
                Assert.Equal("upgrade_offer", d.RootElement.GetProperty("t").GetString());
                var names = new List<string>();
                foreach (JsonElement i in d.RootElement.GetProperty("items").EnumerateArray())
                {
                    names.Add(i.GetString()!);
                }

                Assert.Equal(new[] { "backpack", "tool", "heal", "mask" }, names);
            }

            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new UpgradeOkEvent(2, UpgradeKind.Mask))!))
            {
                Assert.Equal("upgrade_ok", d.RootElement.GetProperty("t").GetString());
                Assert.Equal("mask", d.RootElement.GetProperty("item").GetString());
            }
        }

        [Theory]
        [InlineData(RejectReason.NoBags, "no_bags")]
        [InlineData(RejectReason.MachineJammed, "machine_jammed")]
        [InlineData(RejectReason.NoItem, "no_item")]
        public void MachineRejectReasons_HaveWireNames(RejectReason reason, string wire)
        {
            Assert.Equal(wire, WireNames.Of(reason));
        }

        [Fact]
        public void MeterFull_HasTheWireName()
        {
            Assert.Equal("meter_full", WireNames.Of(GameOverReason.MeterFull));
        }

        [Theory]
        [InlineData(RejectReason.BagsFull, "bags_full")]
        [InlineData(RejectReason.SpotClean, "spot_clean")]
        [InlineData(RejectReason.SpotDirty, "spot_dirty")]
        public void NewRejectReasons_HaveWireNames(RejectReason reason, string wire)
        {
            Assert.Equal(wire, WireNames.Of(reason));
        }

        [Fact]
        public void TrasherCommands_Parse()
        {
            Assert.True(Codec.TryToCommand(Parse("{\"t\":\"repollute\",\"spot\":\"market_bins\"}"), out ICommand? cmd, out _));
            Assert.Equal("market_bins", Assert.IsType<RepolluteCommand>(cmd).SpotId);
            Assert.False(Codec.TryToCommand(Parse("{\"t\":\"repollute\"}"), out _, out string? error));
            Assert.Equal("bad_field", error);

            Assert.True(Codec.TryToCommand(Parse("{\"t\":\"pick_ability\",\"ability\":\"gas\"}"), out cmd, out _));
            Assert.Equal(TrasherAbility.Gas, Assert.IsType<PickAbilityCommand>(cmd).Ability);
            Assert.True(Codec.TryToCommand(Parse("{\"t\":\"pick_ability\",\"ability\":\"virus\"}"), out cmd, out _));
            Assert.Equal(TrasherAbility.Virus, Assert.IsType<PickAbilityCommand>(cmd).Ability);
            Assert.False(Codec.TryToCommand(Parse("{\"t\":\"pick_ability\",\"ability\":\"none\"}"), out _, out _));

            Assert.True(Codec.TryToCommand(Parse("{\"t\":\"sabotage\",\"kind\":\"machine\"}"), out cmd, out _));
            Assert.Equal(SabotageKind.Machine, Assert.IsType<SabotageCommand>(cmd).Kind);
        }

        [Fact]
        public void TrasherEvents_HaveTheirWireShape()
        {
            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new RepolluteOkEvent(1, 20f, 2))!))
            {
                Assert.Equal("repollute_ok", d.RootElement.GetProperty("t").GetString());
                Assert.Equal(20f, d.RootElement.GetProperty("cooldown").GetSingle());
                Assert.Equal(2, d.RootElement.GetProperty("count").GetInt32());
            }

            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new AbilityUnlockedEvent(1))!))
            {
                Assert.Equal("ability_unlocked", d.RootElement.GetProperty("t").GetString());
                Assert.Equal(new[] { "virus", "gas" }, d.RootElement.GetProperty("choices").EnumerateArray().Select(x => x.GetString()).ToArray());
            }

            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new AbilityOkEvent(1, TrasherAbility.Gas))!))
            {
                Assert.Equal("ability_ok", d.RootElement.GetProperty("t").GetString());
                Assert.Equal("gas", d.RootElement.GetProperty("ability").GetString());
            }

            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new CueEvent(new byte[] { 2 }, CueKind.Repollute, 1, new Vec2(3f, 4f)))!))
            {
                Assert.Equal("repollute", d.RootElement.GetProperty("kind").GetString());
            }

            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new SabotageStartedEvent(SabotageKind.Machine, null))!))
            {
                Assert.Equal("machine", d.RootElement.GetProperty("kind").GetString());
                Assert.False(d.RootElement.TryGetProperty("countdown", out _));
            }

            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new SabotageEndedEvent(SabotageKind.Machine, SabotageEndResult.Fixed))!))
            {
                Assert.Equal("machine", d.RootElement.GetProperty("kind").GetString());
                Assert.Equal("fixed", d.RootElement.GetProperty("result").GetString());
            }

            Assert.Equal("not_unlocked", WireNames.Of(RejectReason.NotUnlocked));
        }

        [Fact]
        public void Gas_Command_And_Events_HaveTheirWireShape()
        {
            Assert.True(Codec.TryToCommand(Parse("{\"t\":\"gas\",\"room\":\"gas_market\"}"), out ICommand? cmd, out _));
            Assert.Equal("gas_market", Assert.IsType<GasCommand>(cmd).RoomId);
            Assert.False(Codec.TryToCommand(Parse("{\"t\":\"gas\"}"), out _, out string? error));
            Assert.Equal("bad_field", error);

            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new GasWarningEvent("gas_dark", 3f))!))
            {
                Assert.Equal("gas_warning", d.RootElement.GetProperty("t").GetString());
                Assert.Equal("gas_dark", d.RootElement.GetProperty("room").GetString());
                Assert.Equal(3f, d.RootElement.GetProperty("in_s").GetSingle());
            }

            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new GasDoneEvent("gas_dark"))!))
            {
                Assert.Equal("gas_done", d.RootElement.GetProperty("t").GetString());
                Assert.Equal("gas_dark", d.RootElement.GetProperty("room").GetString());
            }

            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new MaskUsedEvent(3))!))
            {
                Assert.Equal("mask_used", d.RootElement.GetProperty("t").GetString());
            }

            using (JsonDocument d = JsonDocument.Parse(Uncycled.Server.Rooms.Wire.Event(new YouAreOutEvent(3, OutCause.Gas))!))
            {
                Assert.Equal("gas", d.RootElement.GetProperty("cause").GetString());
            }
        }

        [Theory]
        [InlineData("Alice", "Alice")]
        [InlineData("  Bob  ", "Bob")]
        [InlineData("", null)]
        [InlineData("   ", null)]
        [InlineData("ThisNameIsFarTooLong", null)]
        public void CleanName_Validates(string raw, string? expected)
        {
            Assert.Equal(expected, Codec.CleanName(raw));
        }
    }
}
