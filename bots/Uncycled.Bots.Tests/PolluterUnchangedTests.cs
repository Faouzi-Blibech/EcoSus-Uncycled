using System;
using System.Linq;
using System.Text;
using Sim.Core;
using Sim.Match;
using Xunit;
using Xunit.Abstractions;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// The citizen commitment must not touch polluters (their fake tasks included). Only the polluter
    /// seats are driven, so nothing a citizen does can change the world; the commands they send on a
    /// fixed seed were recorded before the commitment change and must stay byte for byte the same.
    /// </summary>
    public class PolluterUnchangedTests
    {
        private readonly ITestOutputHelper _out;

        public PolluterUnchangedTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static string Trace(ulong seed)
        {
            TestMatch m = TestMatch.Create(seed);
            var rig = new BrainRig(m, seed);
            foreach (PlayerState p in m.Engine.Players)
            {
                if (p.Role != Role.Polluter)
                {
                    rig.Skip.Add(p.Seat);
                }
            }

            rig.RunUntil(() => m.Engine.Phase == MatchPhase.Ended, 240f);
            var sb = new StringBuilder();
            foreach (SentCommand c in rig.Sent)
            {
                sb.Append(c.Seat).Append('@').Append(c.MatchTime.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(':').Append(c.Command.GetType().Name).Append(':').Append(c.Result.IsOk ? "ok" : "no").Append('\n');
            }

            return sb.ToString();
        }

        private static string Hash(string text)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b => b.ToString("x2")));
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Polluter_CommandsOnAFixedSeedAreUnchanged()
        {
            string t1 = Trace(3);
            string t2 = Trace(11);
            int commands = t1.Count(c => c == '\n') + t2.Count(c => c == '\n');
            string hash = Hash(t1 + "|" + t2);
            _out.WriteLine("polluter commands " + commands + " hash " + hash);
            Assert.Contains("FakeTask", string.Join(",", Enum.GetNames(typeof(Uncycled.Bots.BotAction))));
            Assert.True(commands > 300, "the polluters must have acted, commands=" + commands);
            Assert.Equal("a14c2ccbcb94b47dda0391a283da2888101ed2f9d9fe494bf52d98853361e868", hash);
        }
    }
}
