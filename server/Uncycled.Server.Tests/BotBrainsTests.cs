using System;
using System.IO;
using Sim.Core;
using Uncycled.Bots;
using Uncycled.Server.Bots;
using Uncycled.Server.Content;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>Loading the role models once at startup, with the failure cached too.</summary>
    public class BotBrainsTests
    {
        private static ContentBundle Content() => ContentLoader.Load(ContentLoader.ResolveDir(null));

        private static string ModelsDirWithFixture()
        {
            string dir = RoomRig.EmptyModelsDir();
            string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
            foreach (string role in new[] { "citizen", "polluter" })
            {
                File.Copy(Path.Combine(fixtures, "tiny.onnx"), Path.Combine(dir, role + ".onnx"));
                File.Copy(Path.Combine(fixtures, "tiny.json"), Path.Combine(dir, role + ".json"));
            }

            return dir;
        }

        [Fact]
        public void Both_role_models_load_once_when_the_brain_is_neural()
        {
            var log = new CountingLog();
            var settings = new BotsSettings { Brain = "neural" };

            using var brains = new BotBrains(settings, ModelsDirWithFixture(), Content().Tasks, log);

            Assert.True(brains.HasModel(Role.Citizen));
            Assert.True(brains.HasModel(Role.Polluter));
            Assert.NotNull(brains.Create(Role.Citizen, 1));
            Assert.Equal(0, log.Warnings);
        }

        [Fact]
        public void A_model_of_the_v2_contract_loads_for_both_roles()
        {
            // v2 shape and v2 schema hash: what a network trained on the v2 policy list carries
            string dir = RoomRig.EmptyModelsDir();
            foreach (string role in new[] { "citizen", "polluter" })
            {
                Uncycled.Bots.Tests.SyntheticOnnx.Write(dir, role, ObservationBuilderV2.Size, PolicyActionsV2.Count, SchemaV2.Hash);
            }

            var log = new CountingLog();

            using var brains = new BotBrains(new BotsSettings { Brain = "neural" }, dir, Content().Tasks, log);

            Assert.True(brains.HasModel(Role.Citizen));
            Assert.True(brains.HasModel(Role.Polluter));
            Assert.Equal(0, log.Warnings);
        }

        [Fact]
        public void A_v1_model_is_refused_and_both_roles_play_scripted()
        {
            string dir = RoomRig.EmptyModelsDir();
            string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
            foreach (string role in new[] { "citizen", "polluter" })
            {
                File.Copy(Path.Combine(fixtures, "tiny_v1.onnx"), Path.Combine(dir, role + ".onnx"));
                File.Copy(Path.Combine(fixtures, "tiny_v1.json"), Path.Combine(dir, role + ".json"));
            }

            var log = new CountingLog();

            using var brains = new BotBrains(new BotsSettings { Brain = "neural" }, dir, Content().Tasks, log);

            Assert.False(brains.HasModel(Role.Citizen));
            Assert.False(brains.HasModel(Role.Polluter));
            Assert.Equal(2, log.Warnings);
            Assert.NotNull(brains.Create(Role.Citizen, 1));
        }

        [Fact]
        public void A_model_that_does_not_load_is_reported_once_per_role_and_never_reopened()
        {
            string dir = RoomRig.EmptyModelsDir();
            File.WriteAllText(Path.Combine(dir, "citizen.onnx"), "this is not a network");
            var log = new CountingLog();
            var settings = new BotsSettings { Brain = "neural" };

            using var brains = new BotBrains(settings, dir, Content().Tasks, log);
            Assert.Equal(2, log.Warnings); // citizen: corrupt, polluter: missing

            for (int i = 0; i < 14; i++)
            {
                Assert.NotNull(brains.Create(i % 2 == 0 ? Role.Citizen : Role.Polluter, (byte)i));
            }

            Assert.False(brains.HasModel(Role.Citizen));
            Assert.False(brains.HasModel(Role.Polluter));
            Assert.Equal(2, log.Warnings);
        }

        [Fact]
        public void The_scripted_setting_loads_no_model()
        {
            using var brains = new BotBrains(new BotsSettings(), ModelsDirWithFixture(), Content().Tasks, new CountingLog());

            Assert.False(brains.HasModel(Role.Citizen));
        }

        [Fact]
        public void The_brain_override_switches_a_scripted_bots_json_to_neural()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uncycled-bots-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "bots.json"), "{ \"brain\": \"scripted\" }");
            Directory.Move(ModelsDirWithFixture(), Path.Combine(dir, "models"));

            using BotBrains scripted = BotBrains.Load(dir, Content(), new CountingLog());
            using BotBrains neural = BotBrains.Load(dir, Content(), new CountingLog(), "neural");

            Assert.False(scripted.HasModel(Role.Citizen));
            Assert.True(neural.HasModel(Role.Citizen));
        }
    }
}
