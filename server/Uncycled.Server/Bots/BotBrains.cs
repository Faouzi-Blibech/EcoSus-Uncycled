using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Bots;
using Uncycled.Bots.Meeting;
using Uncycled.Bots.Neural;
using Uncycled.Server.Content;

namespace Uncycled.Server.Bots;

/// <summary>
/// Picks the brain of every bot seat. When bots.json (or UNCYCLED_BOTS_BRAIN) says "neural", the
/// model of each role is loaded once here, at startup, and the session (or the failure) is kept: a
/// room starting a match only builds brains from it and never touches a model file. A role without
/// a usable model plays the scripted brain. Every brain is wrapped in a GuardedBrain.
/// </summary>
public class BotBrains : IDisposable
{
    public const double DefaultSlowMs = 20.0;
    public const int DefaultSlowStrikes = 3;

    private readonly BotsSettings _settings;
    private readonly IReadOnlyList<TaskTypeDef> _tasks;
    private readonly ILogger _log;
    private readonly Func<Role, IRandom, IBotBrain>? _primary;
    private readonly double _slowMs;
    private readonly int _slowStrikes;
    private readonly MeetingKit? _meeting;
    private readonly Dictionary<Role, OnnxPolicy> _policies = new Dictionary<Role, OnnxPolicy>();

    /// <param name="primary">Replaces the chosen brain (tests); no model is loaded then.</param>
    public BotBrains(
        BotsSettings settings,
        string modelsDir,
        IReadOnlyList<TaskTypeDef> tasks,
        ILogger log,
        Func<Role, IRandom, IBotBrain>? primary = null,
        double slowMs = DefaultSlowMs,
        int slowStrikes = DefaultSlowStrikes,
        MeetingKit? meeting = null)
    {
        _settings = settings;
        _tasks = tasks;
        _log = log;
        _primary = primary;
        _slowMs = slowMs;
        _slowStrikes = slowStrikes;
        _meeting = meeting;
        if (primary == null && string.Equals(settings.Brain, "neural", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Role role in new[] { Role.Citizen, Role.Polluter })
            {
                LoadModel(role, NeuralBrainFactory.ModelPath(role, modelsDir));
            }
        }
    }

    /// <summary>
    /// UNCYCLED_BOTS_DIR when set, else the first "bots" folder holding bots.json found in ./bots,
    /// next to the binaries, or in any parent of the working directory or binaries.
    /// </summary>
    public static string ResolveDir(string? fromEnv)
    {
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            string? dir = Path.GetFullPath(start);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "bots");
                if (File.Exists(Path.Combine(candidate, "bots.json")))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "bots");
    }

    /// <summary>
    /// Settings from dir/bots.json (defaults when missing), models from dir/models.
    /// brainOverride ("scripted" or "neural", from UNCYCLED_BOTS_BRAIN) replaces the file's "brain".
    /// </summary>
    public static BotBrains Load(string dir, ContentBundle content, ILogger log, string? brainOverride = null)
    {
        BotsSettings settings = BotsSettings.Load(Path.Combine(dir, "bots.json"));
        if (!string.IsNullOrWhiteSpace(brainOverride))
        {
            settings.Brain = brainOverride.Trim();
        }

        string modelsDir = Path.Combine(dir, "models");
        log.LogInformation("Bot brains: setting {Brain}, skill {Skill}, models in {Dir}", settings.Brain, settings.Skill, modelsDir);
        return new BotBrains(settings, modelsDir, content.Tasks, log, meeting: MeetingKit.FromEnvironment(settings, log));
    }

    /// <summary>The shared state of one match in a room, or null when bots do not talk (no kit or no phrase catalogue).</summary>
    public RoomMeeting? NewRoomMeeting(PhraseCatalogue? phrases, string matchId, ChatWords? words = null) =>
        _meeting == null || phrases == null ? null : new RoomMeeting(new MeetingLog(phrases, words), matchId);

    /// <summary>True when this role plays a loaded network.</summary>
    internal bool HasModel(Role role) => _policies.ContainsKey(role);

    /// <summary>The guarded brain of one bot seat with the given hidden role.</summary>
    public virtual IBotBrain Create(Role role, byte seat, RoomMeeting? meeting = null)
    {
        string who = "seat " + seat + " (" + role + ")";
        IBotBrain primary;
        try
        {
            primary = ChoosePrimary(role);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Bot {Who}: brain could not be built; the scripted brain plays this seat", who);
            primary = Scripted(role);
        }

        primary = Talking(primary, seat, meeting);
        return new GuardedBrain(primary, () => Talking(Scripted(role), seat, meeting), _log, who, _slowMs, _slowStrikes);
    }

    /// <summary>The brain with meeting talk added; the brain itself when there is no meeting kit or room state, or it keeps no memory for a talker.</summary>
    private IBotBrain Talking(IBotBrain brain, byte seat, RoomMeeting? meeting)
    {
        if (_meeting == null || meeting == null || brain is not ITalkingSeat)
        {
            return brain;
        }

        var talker = new MeetingTalker(_meeting.Primary, _meeting.Fallback, meeting.Floor, new Random(Random.Shared.Next()))
        {
            LeadS = _meeting.LeadS,
            Sink = _meeting.Log,
            MatchId = meeting.MatchId,
        };
        var talking = new TalkingBrain(brain, talker, meeting.Log, message => _log.LogWarning("{Message}", message));
        meeting.Feed.Add(seat, talking, talking);
        return talking;
    }

    /// <summary>Releases the models and the meeting kit (it flushes the decision log file). Called by the host at shutdown, never from a room tick.</summary>
    public void Dispose()
    {
        _meeting?.Dispose();
        foreach (OnnxPolicy policy in _policies.Values)
        {
            policy.Dispose();
        }

        _policies.Clear();
    }

    private IBotBrain ChoosePrimary(Role role)
    {
        if (_primary != null)
        {
            return _primary(role, NewRandom());
        }

        if (_policies.TryGetValue(role, out OnnxPolicy? policy))
        {
            return new NeuralBrain(policy, Scripted(role), _settings, NewRandom(), _tasks, message => _log.LogWarning("{Message}", message));
        }

        return Scripted(role);
    }

    private IBotBrain Scripted(Role role) => BrainFactory.Scripted(role, _settings, NewRandom(), _tasks);

    private void LoadModel(Role role, string path)
    {
        try
        {
            if (OnnxPolicy.TryLoad(path, SchemaV2.Hash, out OnnxPolicy? policy, out string reason) && policy != null)
            {
                _policies[role] = policy;
                _log.LogInformation("Bot model for {Role} loaded from {Path}", role, path);
            }
            else
            {
                _log.LogWarning("Bot model for {Role} not used, the scripted brain plays: {Reason}", role, reason);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Bot model for {Role} not used, the scripted brain plays", role);
        }
    }

    private static IRandom NewRandom() => new XorShiftRandom((ulong)Random.Shared.NextInt64());
}
