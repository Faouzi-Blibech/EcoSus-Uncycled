namespace Uncycled.Bots.Meeting
{
    /// <summary>Receives each decision the talker really sends (a phrase or a vote), for the per-match decision log.</summary>
    public interface IDecisionSink
    {
        void Sent(MeetingState state, MeetingDecision sent);
    }
}
