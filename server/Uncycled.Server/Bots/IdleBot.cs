using Sim.Core;
using Sim.Match;
using Uncycled.Bots;

namespace Uncycled.Server.Bots;

/// <summary>
/// Last-resort bot brain: never moves, votes skip. Used only when a seat's scripted brain has failed
/// (see GuardedBrain). It speaks the same commands as a human.
/// </summary>
public sealed class IdleBot : IBotBrain
{
    private const float ThinkEveryS = 0.5f;
    private float _timer;

    public void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send)
    {
        if (engine.Phase != MatchPhase.Meeting)
        {
            _timer = 0f;
            return;
        }

        _timer -= dt;
        if (_timer > 0f)
        {
            return;
        }

        _timer = ThinkEveryS;
        SeatView view = engine.ViewFor(seat);
        MeetingView? meeting = view.Meeting;
        if (meeting == null || view.Me.Status != PlayerStatus.Active)
        {
            return;
        }

        if (meeting.Stage == MeetingStage.Voting && !meeting.MyVoteCast)
        {
            send(new VoteCommand(null));
        }
    }
}
