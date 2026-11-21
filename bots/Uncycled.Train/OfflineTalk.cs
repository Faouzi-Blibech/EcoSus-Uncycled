using System;
using Uncycled.Bots;
using Uncycled.Bots.Meeting;

namespace Uncycled.Train
{
    /// <summary>
    /// Meeting talk for the offline runners (eval, training host): the same MeetingTalker the live room gives
    /// a bot, with the rule decider only (meeting.clef_for = none): no model, no network call. Without it
    /// the bots would vote alone, on their own evidence, in a game with no chat, which is stricter than the room.
    /// </summary>
    internal static class OfflineTalk
    {
        /// <summary>The brain with meeting talk; the brain itself when it keeps no memory for a talker. The caller registers the result with the feed.</summary>
        public static IBotBrain Wrap(IBotBrain brain, MeetingFeed feed, MeetingFloor floor, byte seat, ulong matchSeed)
        {
            if (!(brain is ITalkingSeat) || feed.Log == null)
            {
                return brain;
            }

            var rule = new RuleDecider();
            var rng = new Random(unchecked((int)(matchSeed * 2654435761UL) + seat));
            return new TalkingBrain(brain, new MeetingTalker(rule, rule, floor, rng), feed.Log);
        }
    }
}
