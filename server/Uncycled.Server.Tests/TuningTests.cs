using System.Text.Json;
using Sim.Match;
using Uncycled.Server.Content;
using Xunit;

namespace Uncycled.Server.Tests
{
    public class TuningTests
    {
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
        public void Sabotage_start_cooldown_is_read_and_defaults_to_10()
        {
            using JsonDocument set = JsonDocument.Parse("{\"sabotage\":{\"start_cooldown_s\":12}}");
            Assert.Equal(12f, ContentLoader.ReadConfig(set.RootElement).SabotageStartCooldownS);
            using JsonDocument none = JsonDocument.Parse("{}");
            Assert.Equal(10f, ContentLoader.ReadConfig(none.RootElement).SabotageStartCooldownS);
        }

        [Fact]
        public void The_real_tuning_json_holds_the_sabotage_start_cooldown()
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null));
            using JsonDocument doc = JsonDocument.Parse(content.TuningJson);
            Assert.Equal(10f, doc.RootElement.GetProperty("sabotage").GetProperty("start_cooldown_s").GetSingle());
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
