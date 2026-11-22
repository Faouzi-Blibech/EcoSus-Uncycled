namespace Uncycled.Bots.Meeting
{
    /// <summary>Receives each decision the talker really sends (a phrase or a vote), for the per-match decision log.</summary>
    public interface IDecisionSink
    {
        void Sent(MeetingState state, MeetingDecision sent);
    }

    /// <summary>An IDecisionSink that also hears which bot seat called an emergency meeting and why (trigger: repollute, jam, gas, or other).</summary>
    public interface IEmergencyCallSink
    {
        void EmergencyCall(string matchId, byte seat, string trigger, string? zone);
    }
}
