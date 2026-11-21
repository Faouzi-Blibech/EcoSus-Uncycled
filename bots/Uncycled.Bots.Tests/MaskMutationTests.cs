using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// Mask and observation checks that fail when the matching engine rule is dropped from the code
    /// (each one was proven against a mutation, see the fix-round report).
    /// </summary>
    public class MaskMutationTests
    {
        private static bool[] MaskOf(TestMatch m, byte seat, BotMemory? mem = null)
        {
            mem ??= new BotMemory(m.Map, m.Config);
            SeatView view = m.Engine.ViewFor(seat);
            mem.Observe(view, m.Engine.MatchTime, m.Engine.Witnesses.SeenBy(seat));
            return ActionCatalogue.Mask(view, mem, m.Map);
        }

        [Fact]
        public void Mask_PolluterNextToOnlyItsPartnerCannotHit()
        {
            TestMatch m = TestMatch.Create(3);
            var polluters = m.Engine.Players.Where(p => p.Role == Role.Polluter).Select(p => p.Seat).ToList();
            Assert.Equal(2, polluters.Count);

            // both polluters meet at the far smog panel while the citizens stay at the spawn
            Crowd.Do(m, polluters, BotAction.GoSmogPanel, 14f);
            m.TickFor(2f);

            byte p = polluters[0];
            SeatView view = m.Engine.ViewFor(p);
            Assert.Equal(polluters[1], Assert.Single(view.Players).Seat);
            Assert.True(Vec2.Distance(view.Players[0].Position, view.Me.Position) <= m.Config.HitRange);
            Assert.True(view.Me.Cooldowns!.Hit <= 0f);

            Assert.False(MaskOf(m, p)[(int)BotAction.HitNearest]);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Mask_NoHitWhileHitCooldownRuns()
        {
            TestMatch m = TestMatch.Create(2);
            m.TickFor(11f);
            byte polluter = m.FirstSeatOf(Role.Polluter);
            SeatView before = m.Engine.ViewFor(polluter);
            PlayerView victim = before.Players.First(p => Vec2.Distance(p.Position, before.Me.Position) <= m.Config.HitRange
                && !before.Me.Partners.Contains(p.Seat));
            Assert.True(MaskOf(m, polluter)[(int)BotAction.HitNearest]);

            Assert.True(m.Engine.Apply(polluter, new HitCommand(victim.Seat)).IsOk);
            m.Tick();

            SeatView after = m.Engine.ViewFor(polluter);
            Assert.True(after.Me.Cooldowns!.Hit > 0f);
            Assert.False(MaskOf(m, polluter)[(int)BotAction.HitNearest]);
        }

        [Fact]
        public void Mask_EmergencyNeedsFifteenSecondsSinceMeeting()
        {
            TestMatch m = TestMatch.Create(1);
            var driver = new BotDriver(m, 3);
            driver.Begin(BotAction.GoEmergency);
            driver.RunUntilIdle(20f);
            Assert.True(Vec2.Distance(driver.View.Me.Position, m.Map.FindConsole("meeting_button")!.Position) <= 1.2f);
            Assert.True(m.Engine.MatchTime < m.Config.EmergencyMinSinceMeetingS - 2f);

            Assert.False(ActionCatalogue.Mask(driver.View, driver.Memory, m.Map)[(int)BotAction.CallMeeting]);

            while (m.Engine.MatchTime < m.Config.EmergencyMinSinceMeetingS + 1f)
            {
                driver.Step();
            }

            Assert.True(ActionCatalogue.Mask(driver.View, driver.Memory, m.Map)[(int)BotAction.CallMeeting]);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Mask_OutSeatGetsOnlyStay()
        {
            TestMatch m = TestMatch.Create(2);
            m.TickFor(11f);
            byte polluter = m.FirstSeatOf(Role.Polluter);
            SeatView pv = m.Engine.ViewFor(polluter);
            PlayerView victim = pv.Players.First(p => Vec2.Distance(p.Position, pv.Me.Position) <= m.Config.HitRange
                && !pv.Me.Partners.Contains(p.Seat));
            for (int i = 0; i < 3000 && m.Engine.Find(victim.Seat)!.IsActive; i++)
            {
                m.Engine.Apply(polluter, new HitCommand(victim.Seat));
                m.Tick();
            }

            Assert.Equal(PlayerStatus.Out, m.Engine.ViewFor(victim.Seat).Me.Status);
            bool[] mask = MaskOf(m, victim.Seat);
            for (int i = 0; i < mask.Length; i++)
            {
                Assert.Equal(i == (int)BotAction.Stay, mask[i]);
            }
        }

        [Fact]
        public void Observation_PartnerFlagOnlyForARealPartner()
        {
            TestMatch m = TestMatch.Create(3);
            m.TickFor(2f);
            int[] flags = Enumerable.Range(0, ObservationBuilder.PlayerSlots)
                .Select(i => ObservationBuilder.FieldNames.ToList().IndexOf("player" + i + ".partner")).ToArray();
            Assert.DoesNotContain(-1, flags);

            int positives = 0;
            foreach (PlayerState p in m.Engine.Players)
            {
                var mem = new BotMemory(m.Map, m.Config);
                SeatView view = m.Engine.ViewFor(p.Seat);
                mem.Observe(view, m.Engine.MatchTime, m.Engine.Witnesses.SeenBy(p.Seat));
                var obs = new float[ObservationBuilder.Size];
                ObservationBuilder.Build(view, mem, m.Map, m.Config, m.Engine.MatchTime, obs);

                int expected = 0;
                var sorted = view.Players.OrderBy(q => Vec2.Distance(q.Position, view.Me.Position)).ThenBy(q => q.Seat).Take(ObservationBuilder.PlayerSlots).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    bool partner = view.Me.Partners.Contains(sorted[i].Seat);
                    Assert.Equal(partner ? 1f : 0f, obs[flags[i]]);
                    expected += partner ? 1 : 0;
                    positives += partner ? 1 : 0;
                }

                if (p.Role == Role.Citizen)
                {
                    Assert.Equal(0, expected);
                    Assert.All(flags, f => Assert.Equal(0f, obs[f]));
                }
            }

            Assert.True(positives > 0, "no polluter saw its partner; the flag was never checked as set");
        }
    }
}
