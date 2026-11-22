using System.Text.Json;
using Sim.Match;
using Uncycled.Server.Content;
using Xunit;

namespace Uncycled.Server.Tests
{
    public class TuningTests
    {
        [Fact]
        public void Rooms_are_read_and_default_to_6_8_10_with_2_trashers_at_8()
        {
            using JsonDocument set = JsonDocument.Parse("{\"rooms\":{\"sizes\":[4,12],\"default_size\":12,\"trashers\":{\"4\":1,\"12\":3}}}");
            MatchConfig c = ContentLoader.ReadConfig(set.RootElement);
            Assert.Equal(new[] { 4, 12 }, c.RoomSizes);
            Assert.Equal(12, c.DefaultSize);
            Assert.Equal(3, c.TrashersFor(12));
            Assert.Equal(1, c.TrashersFor(4));

            using JsonDocument none = JsonDocument.Parse("{}");
            MatchConfig d = ContentLoader.ReadConfig(none.RootElement);
            Assert.Equal(new[] { 6, 8, 10 }, d.RoomSizes);
            Assert.Equal(8, d.DefaultSize);
            Assert.Equal(new[] { 1, 2, 2 }, new[] { d.TrashersFor(6), d.TrashersFor(8), d.TrashersFor(10) });
        }

        [Fact]
        public void The_real_tuning_json_holds_the_rooms_section()
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            using JsonDocument doc = JsonDocument.Parse(content.TuningJson);
            Assert.Equal(8, doc.RootElement.GetProperty("rooms").GetProperty("default_size").GetInt32());
            Assert.Equal(new[] { 6, 8, 10 }, content.Config.RoomSizes);
        }

        [Fact]
        public void ForSize_sets_seats_and_polluters_and_leaves_the_shared_config_alone()
        {
            var shared = new MatchConfig();
            MatchConfig six = shared.ForSize(6);
            Assert.Equal(6, six.Seats);
            Assert.Equal(1, six.Polluters);
            Assert.Equal(8, shared.Seats);
            Assert.Equal(2, shared.Polluters);
        }

        [Fact]
        public void ReturnToLobby_is_read_from_match_section()
        {
            using JsonDocument doc = JsonDocument.Parse("{\"match\":{\"return_to_lobby_s\":30}}");
            Assert.Equal(30f, ContentLoader.ReadConfig(doc.RootElement).ReturnToLobbyS);
        }

        [Fact]
        public void ReturnToLobby_defaults_to_15_when_missing()
        {
            using JsonDocument doc = JsonDocument.Parse("{}");
            Assert.Equal(15f, ContentLoader.ReadConfig(doc.RootElement).ReturnToLobbyS);
        }

        [Fact]
        public void DarkMult_is_read_from_vision_section()
        {
            using JsonDocument doc = JsonDocument.Parse("{\"vision\":{\"dark_mult\":0.4}}");
            Assert.Equal(0.4f, ContentLoader.ReadConfig(doc.RootElement).DarkMult);
        }

        [Fact]
        public void DarkMult_defaults_to_half_when_missing()
        {
            using JsonDocument doc = JsonDocument.Parse("{}");
            Assert.Equal(0.5f, ContentLoader.ReadConfig(doc.RootElement).DarkMult);
        }

        [Fact]
        public void The_real_tuning_json_holds_dark_mult()
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            using JsonDocument doc = JsonDocument.Parse(content.TuningJson);
            Assert.Equal(0.5f, doc.RootElement.GetProperty("vision").GetProperty("dark_mult").GetSingle());
        }

        [Fact]
        public void The_start_cooldown_is_one_key_under_trasher_and_defaults_to_10()
        {
            using JsonDocument set = JsonDocument.Parse("{\"trasher\":{\"start_cooldown_s\":12}}");
            Assert.Equal(12f, ContentLoader.ReadConfig(set.RootElement).TrasherStartCooldownS);
            using JsonDocument none = JsonDocument.Parse("{}");
            Assert.Equal(10f, ContentLoader.ReadConfig(none.RootElement).TrasherStartCooldownS);
        }

        [Fact]
        public void The_trasher_and_machine_keys_are_read_and_the_real_file_holds_them()
        {
            using JsonDocument set = JsonDocument.Parse("{\"trasher\":{\"repollute_cooldown_s\":7,\"unlock_after\":2,\"start_cooldown_s\":5},\"machine\":{\"fix_s\":9}}");
            MatchConfig c = ContentLoader.ReadConfig(set.RootElement);
            Assert.Equal((7f, 2, 5f, 9f), (c.RepolluteCooldownS, c.UnlockAfter, c.TrasherStartCooldownS, c.MachineFixS));
            using JsonDocument none = JsonDocument.Parse("{}");
            MatchConfig d = ContentLoader.ReadConfig(none.RootElement);
            Assert.Equal((20f, 3, 10f, 4f), (d.RepolluteCooldownS, d.UnlockAfter, d.TrasherStartCooldownS, d.MachineFixS));

            MatchConfig real = ContentLoader.Load(ContentLoader.ResolveDir(null)).Config;
            Assert.Equal((20f, 3, 10f, 4f), (real.RepolluteCooldownS, real.UnlockAfter, real.TrasherStartCooldownS, real.MachineFixS));
            Assert.Equal((30f, 90f, 5f), (real.VirusCooldownS, real.GasCooldownS, real.GasWarningS));
        }

        [Fact]
        public void The_virus_keys_are_read_and_the_real_file_holds_them()
        {
            using JsonDocument set = JsonDocument.Parse("{\"virus\":{\"start_s\":50,\"fatal_s\":8,\"grow_s\":70,\"heal_range\":2}}");
            MatchConfig c = ContentLoader.ReadConfig(set.RootElement);
            Assert.Equal((50f, 8f, 70f, 2f), (c.VirusStartS, c.VirusFatalS, c.VirusGrowS, c.HealRange));
            using JsonDocument none = JsonDocument.Parse("{}");
            MatchConfig d = ContentLoader.ReadConfig(none.RootElement);
            Assert.Equal((60f, 10f, 90f, 1.5f), (d.VirusStartS, d.VirusFatalS, d.VirusGrowS, d.HealRange));

            MatchConfig real = ContentLoader.Load(ContentLoader.ResolveDir(null)).Config;
            Assert.Equal((60f, 10f, 90f, 1.5f), (real.VirusStartS, real.VirusFatalS, real.VirusGrowS, real.HealRange));
        }

        [Fact]
        public void The_real_tuning_json_holds_the_start_cooldown_only_under_trasher()
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            using JsonDocument doc = JsonDocument.Parse(content.TuningJson);
            Assert.Equal(10f, doc.RootElement.GetProperty("trasher").GetProperty("start_cooldown_s").GetSingle());
            Assert.False(doc.RootElement.GetProperty("sabotage").TryGetProperty("start_cooldown_s", out _));
        }

        [Fact]
        public void The_real_tuning_json_sets_up_the_v2_spots_bags_and_meter()
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            MatchConfig c = content.Config;
            Assert.Equal(1, c.BagCap);
            Assert.Equal(3, c.BackpackCap);
            Assert.Equal(0.5f, c.ToolTimeMult);
            Assert.Equal(6f, c.BagMeterAdd);          // 17 deliveries to win (balance pass on tbs_01, 22 Nov)
            Assert.Equal(5f, c.RepolluteMeterSub);
            Assert.Equal(60f, c.SpotRelitterS);
            Assert.Equal((6f, 20f, 5), (c.DashTiles, c.DashCooldownS, c.DashCleansRefill));
            Assert.Equal(0f, c.MeterPassivePerS);
            Assert.Equal(420f, c.DeadlineS);
            Assert.Equal(0, c.TasksCommon + c.TasksShort + c.TasksLong);
        }

        [Fact]
        public void Report_and_clean_ranges_are_read_and_default_to_the_code_values()
        {
            using JsonDocument set = JsonDocument.Parse("{\"report_range\":2.5,\"dump\":{\"clean_range\":1.75}}");
            MatchConfig c = ContentLoader.ReadConfig(set.RootElement);
            Assert.Equal(2.5f, c.ReportRange);
            Assert.Equal(1.75f, c.CleanRange);

            using JsonDocument none = JsonDocument.Parse("{}");
            MatchConfig d = ContentLoader.ReadConfig(none.RootElement);
            Assert.Equal(3f, d.ReportRange);
            Assert.Equal(1f, d.CleanRange);
        }

        [Fact]
        public void The_real_tuning_json_holds_the_report_and_clean_ranges()
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            using JsonDocument doc = JsonDocument.Parse(content.TuningJson);
            Assert.Equal(3f, doc.RootElement.GetProperty("report_range").GetSingle());
            Assert.Equal(1f, doc.RootElement.GetProperty("dump").GetProperty("clean_range").GetSingle());
            Assert.Equal(3f, content.Config.ReportRange);
            Assert.Equal(1f, content.Config.CleanRange);
        }

        [Fact]
        public void Smog_fix_time_is_read_and_defaults_to_3()
        {
            using JsonDocument set = JsonDocument.Parse("{\"sabotage\":{\"smog_fix_s\":4.5}}");
            Assert.Equal(4.5f, ContentLoader.ReadConfig(set.RootElement).SmogFixS);
            using JsonDocument none = JsonDocument.Parse("{}");
            Assert.Equal(3f, ContentLoader.ReadConfig(none.RootElement).SmogFixS);
        }

        [Fact]
        public void The_real_tuning_json_holds_the_smog_fix_time()
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            using JsonDocument doc = JsonDocument.Parse(content.TuningJson);
            Assert.Equal(3f, doc.RootElement.GetProperty("sabotage").GetProperty("smog_fix_s").GetSingle());
            Assert.Equal(3f, content.Config.SmogFixS);
        }
    }
}
