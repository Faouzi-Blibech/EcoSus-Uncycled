using Uncycled.Bots;
using Uncycled.Bots.Meeting;
using Uncycled.Bots.Neural;

namespace Uncycled.Server.Bots;

/// <summary>
/// What every room shares for bot meeting talk: the decider that is asked first (Clef-flash when the
/// two Cloudflare variables are set, else the rule decider), the rule decider that always answers
/// when it is late or fails, and the decision log. One per server process, so one HttpClient and one
/// DailyBudget exist. Dispose it at server shutdown, never on a room tick (Dispose can wait for the
/// log file writer for up to 5 s).
/// </summary>
public sealed class MeetingKit : IDisposable
{
    public const string LogFileEnv = "UNCYCLED_MEETING_LOG";

    private readonly IDisposable? _owned;

    public MeetingKit(IMeetingDecider primary, MeetingDecisionLog log, float leadS = MeetingTalker.DefaultLeadS, IDisposable? owned = null)
    {
        Primary = primary;
        Log = log;
        LeadS = leadS;
        _owned = owned;
    }

    public IMeetingDecider Primary { get; }

    /// <summary>The synchronous rule path every talker falls back to.</summary>
    public IMeetingDecider Fallback { get; } = new RuleDecider();

    public MeetingDecisionLog Log { get; }

    /// <summary>Seconds before a planned line that the primary decider is started (the model call timeout).</summary>
    public float LeadS { get; }

    /// <summary>
    /// Clef-flash when CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN are both set, else the rule path
    /// alone. Decisions go to UNCYCLED_MEETING_LOG (a file) when set, else to the logger. Secrets are never logged.
    /// </summary>
    public static MeetingKit FromEnvironment(BotsSettings settings, ILogger logger, Func<string, string?>? getEnv = null)
    {
        Func<string, string?> env = getEnv ?? Environment.GetEnvironmentVariable;
        string? file = env(LogFileEnv);
        MeetingDecisionLog log = string.IsNullOrWhiteSpace(file)
            ? new MeetingDecisionLog(line => logger.LogInformation("meeting {Line}", line))
            : MeetingDecisionLog.ToFile(file.Trim());

        var http = new HttpClient();
        var timeout = TimeSpan.FromSeconds(settings.MeetingTimeoutS);
        var budget = new DailyBudget(settings.MeetingDailyCallCap, () => DateTime.UtcNow);
        ClefDecider? clef = ClefDecider.FromEnvironment(http, budget, log, timeout, env);
        IMeetingDecider primary = Choose(settings.MeetingClefFor, clef);
        logger.LogInformation(
            "Bot meeting talk: clef_for {ClefFor}, {Decider} (daily cap {Cap}, timeout {Timeout} s)",
            settings.MeetingClefFor,
            clef == null ? "rule path only (no Cloudflare keys)" : primary.Name,
            settings.MeetingDailyCallCap,
            settings.MeetingTimeoutS);
        return new MeetingKit(primary, log, settings.MeetingTimeoutS, http);
    }

    /// <summary>"vote": rules talk and the model votes; "all": the model talks and votes; "none" (default), anything else, or no keys: rules only.</summary>
    internal static IMeetingDecider Choose(string clefFor, ClefDecider? clef)
    {
        var rule = new RuleDecider();
        if (clef == null)
        {
            return rule;
        }

        switch (clefFor)
        {
            case "all":
                return clef;
            case "vote":
                return new StageDecider(rule, clef);
            default:
                return rule;
        }
    }

    public void Dispose()
    {
        Log.Dispose();
        _owned?.Dispose();
    }
}

/// <summary>One match's shared meeting state in a room: the floor (one bot line per 2 s) and the feed that owns the chat log.</summary>
public sealed class RoomMeeting
{
    public RoomMeeting(MeetingLog log, string matchId)
    {
        Feed = new MeetingFeed(log);
        MatchId = matchId;
    }

    public MeetingFloor Floor { get; } = new MeetingFloor();

    public MeetingFeed Feed { get; }

    public MeetingLog Log => Feed.Log!;

    public string MatchId { get; }
}
