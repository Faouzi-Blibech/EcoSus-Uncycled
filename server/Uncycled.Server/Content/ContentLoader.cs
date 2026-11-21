using System.Text.Json;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;

namespace Uncycled.Server.Content;

/// <summary>Everything read from the content directory, ready for rooms.</summary>
public sealed class ContentBundle
{
    public ContentBundle(
        MatchConfig config,
        string tuningJson,
        IReadOnlyList<TaskTypeDef> tasks,
        string tasksJson,
        MapData map,
        PhraseCatalogue? phrases = null,
        ChatFilter? filter = null)
    {
        Config = config;
        TuningJson = tuningJson;
        Tasks = tasks;
        TasksJson = tasksJson;
        Map = map;
        Phrases = phrases;
        Filter = filter ?? ChatFilter.Empty;
    }

    public MatchConfig Config { get; }

    /// <summary>Raw tuning.json text, sent as-is in welcome.</summary>
    public string TuningJson { get; }

    public IReadOnlyList<TaskTypeDef> Tasks { get; }

    /// <summary>The "tasks" array of tasks.json as raw JSON, sent as-is in welcome.</summary>
    public string TasksJson { get; }

    public MapData Map { get; }

    /// <summary>Chat phrase catalogue from phrases.json; every engine validates chat against it (null: any id).</summary>
    public PhraseCatalogue? Phrases { get; }

    /// <summary>Word filter from chat_filter.json; every engine applies it to typed chat (Empty: no words).</summary>
    public ChatFilter Filter { get; }

    public string MapId => Map.Id;

    public string MapVersion => Map.Version;
}

public static class ContentLoader
{
    public const string DefaultMapId = "greybox_01";

    /// <summary>
    /// UNCYCLED_CONTENT_DIR when set, else the first "content" folder holding tuning.json found in
    /// ./content, next to the binaries, or in any parent of the working directory or binaries.
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
                string candidate = Path.Combine(dir, "content");
                if (File.Exists(Path.Combine(candidate, "tuning.json")))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "content");
    }

    public static ContentBundle Load(string contentDir, string mapId = DefaultMapId)
    {
        string tuningText = File.ReadAllText(Path.Combine(contentDir, "tuning.json"));
        string tasksText = File.ReadAllText(Path.Combine(contentDir, "tasks.json"));
        string mapText = File.ReadAllText(Path.Combine(contentDir, "maps", mapId + ".json"));

        using JsonDocument tuningDoc = JsonDocument.Parse(tuningText);
        using JsonDocument tasksDoc = JsonDocument.Parse(tasksText);
        using JsonDocument mapDoc = JsonDocument.Parse(mapText);

        MatchConfig config = ReadConfig(tuningDoc.RootElement);
        JsonElement tasksArray = tasksDoc.RootElement.GetProperty("tasks");
        IReadOnlyList<TaskTypeDef> tasks = ReadTasks(tasksArray);
        MapData map = ReadMap(mapDoc.RootElement);
        PhraseCatalogue phrases = PhraseCatalogue.Parse(File.ReadAllText(Path.Combine(contentDir, "phrases.json")));
        ChatFilter filter = ChatFilter.Parse(File.ReadAllText(Path.Combine(contentDir, "chat_filter.json")));
        return new ContentBundle(config, tuningText, tasks, tasksArray.GetRawText(), map, phrases, filter);
    }

    // ---------- tuning ----------

    private static JsonElement? Child(JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement c))
        {
            return c;
        }

        return null;
    }

    private static float Num(JsonElement root, string a, string? b, float fallback)
    {
        JsonElement? e = Child(root, a);
        if (e.HasValue && b != null)
        {
            e = Child(e.Value, b);
        }

        if (e.HasValue && e.Value.ValueKind == JsonValueKind.Number && e.Value.TryGetSingle(out float v))
        {
            return v;
        }

        return fallback;
    }

    private static bool Flag(JsonElement root, string a, string b, bool fallback)
    {
        JsonElement? g = Child(root, a);
        JsonElement? e = g.HasValue ? Child(g.Value, b) : null;
        if (e.HasValue && e.Value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (e.HasValue && e.Value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return fallback;
    }

    public static MatchConfig ReadConfig(JsonElement t)
    {
        var d = new MatchConfig();
        var c = new MatchConfig
        {
            Seats = (int)Num(t, "seats", null, d.Seats),
            Polluters = (int)Num(t, "polluters", null, d.Polluters),
            SpeedTilesPerS = Num(t, "speed_tiles_per_s", null, d.SpeedTilesPerS),
            ReportRange = Num(t, "report_range", null, d.ReportRange),
            VisionCitizen = Num(t, "vision", "citizen", d.VisionCitizen),
            VisionPolluter = Num(t, "vision", "polluter", d.VisionPolluter),
            SmogMin = Num(t, "vision", "smog_min", d.SmogMin),
            DarkMult = Num(t, "vision", "dark_mult", d.DarkMult),
            PoisonPerHit = (int)Num(t, "poison", "per_hit", d.PoisonPerHit),
            HitRange = Num(t, "poison", "hit_range", d.HitRange),
            HitCooldownS = Num(t, "poison", "hit_cooldown_s", d.HitCooldownS),
            StartCooldownS = Num(t, "poison", "start_cooldown_s", d.StartCooldownS),
            VictimImmunityS = Num(t, "poison", "victim_immunity_s", d.VictimImmunityS),
            DumpCooldownS = Num(t, "dump", "cooldown_s", d.DumpCooldownS),
            DumpMeterAdd = Num(t, "dump", "meter_add", d.DumpMeterAdd),
            CleanMeterSub = Num(t, "dump", "clean_meter_sub", d.CleanMeterSub),
            CleanS = Num(t, "dump", "clean_s", d.CleanS),
            CleanRange = Num(t, "dump", "clean_range", d.CleanRange),
            MeterPassivePerS = Num(t, "meter", "passive_per_s", d.MeterPassivePerS),
            FireExpiredAdd = Num(t, "meter", "fire_expired_add", d.FireExpiredAdd),
            DeadlineS = Num(t, "match", "deadline_s", d.DeadlineS),
            ReturnToLobbyS = Num(t, "match", "return_to_lobby_s", d.ReturnToLobbyS),
            DiscussionS = Num(t, "meeting", "discussion_s", d.DiscussionS),
            VotingS = Num(t, "meeting", "voting_s", d.VotingS),
            ResultsS = Num(t, "meeting", "results_s", d.ResultsS),
            EmergenciesPerMatch = (int)Num(t, "meeting", "emergencies_per_match", d.EmergenciesPerMatch),
            EmergencyMinSinceMeetingS = Num(t, "meeting", "emergency_min_since_meeting_s", d.EmergencyMinSinceMeetingS),
            MissedVoteIsSkip = Flag(t, "meeting", "missed_vote_is_skip", d.MissedVoteIsSkip),
            PhrasesPerMeeting = (int)Num(t, "meeting", "phrases_per_meeting", d.PhrasesPerMeeting),
            TextMaxChars = (int)Num(t, "meeting", "text_max_chars", d.TextMaxChars),
            TextPerMeeting = (int)Num(t, "meeting", "text_per_meeting", d.TextPerMeeting),
            ChatMinGapS = Num(t, "meeting", "chat_min_gap_s", d.ChatMinGapS),
            GhostChatKeep = (int)Num(t, "meeting", "ghost_chat_keep", d.GhostChatKeep),
            GhostWelcomeMaxLines = (int)Num(t, "meeting", "ghost_welcome_max_lines", d.GhostWelcomeMaxLines),
            GhostWelcomeMaxBytes = (int)Num(t, "meeting", "ghost_welcome_max_bytes", d.GhostWelcomeMaxBytes),
            LostSightGraceS = Num(t, "visibility", "lost_sight_grace_s", d.LostSightGraceS),
            SabotageSharedCooldownS = Num(t, "sabotage", "shared_cooldown_s", d.SabotageSharedCooldownS),
            SmogFixS = Num(t, "sabotage", "smog_fix_s", d.SmogFixS),
            FireCountdownS = Num(t, "sabotage", "fire_countdown_s", d.FireCountdownS),
            BagCap = (int)Num(t, "bags", "cap", d.BagCap),
            BackpackCap = (int)Num(t, "bags", "backpack_cap", d.BackpackCap),
            ToolTimeMult = Num(t, "spots", "tool_time_mult", d.ToolTimeMult),
            BagMeterAdd = Num(t, "spots", "bag_meter_add", d.BagMeterAdd),
            RepolluteMeterSub = Num(t, "spots", "repollute_meter_sub", d.RepolluteMeterSub),
            RepolluteCooldownS = Num(t, "trasher", "repollute_cooldown_s", d.RepolluteCooldownS),
            UnlockAfter = (int)Num(t, "trasher", "unlock_after", d.UnlockAfter),
            TrasherStartCooldownS = Num(t, "trasher", "start_cooldown_s", d.TrasherStartCooldownS),
            VirusCooldownS = Num(t, "trasher", "virus_cooldown_s", d.VirusCooldownS),
            GasCooldownS = Num(t, "trasher", "gas_cooldown_s", d.GasCooldownS),
            GasWarningS = Num(t, "trasher", "gas_warning_s", d.GasWarningS),
            MachineFixS = Num(t, "machine", "fix_s", d.MachineFixS),
            TasksCommon = (int)Num(t, "tasks", "common", d.TasksCommon),
            TasksShort = (int)Num(t, "tasks", "short", d.TasksShort),
            TasksLong = (int)Num(t, "tasks", "long", d.TasksLong),
        };
        ReadRooms(t, c);
        return c;
    }

    /// <summary>rooms.sizes, rooms.default_size and rooms.trashers; anything missing or invalid keeps the MatchConfig default.</summary>
    private static void ReadRooms(JsonElement t, MatchConfig c)
    {
        JsonElement? rooms = Child(t, "rooms");
        if (!rooms.HasValue)
        {
            return;
        }

        JsonElement? sizes = Child(rooms.Value, "sizes");
        if (sizes.HasValue && sizes.Value.ValueKind == JsonValueKind.Array)
        {
            var list = new List<int>();
            foreach (JsonElement e in sizes.Value.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int n) && n > 1 && !list.Contains(n))
                {
                    list.Add(n);
                }
            }

            if (list.Count > 0)
            {
                c.RoomSizes = list.ToArray();
                c.DefaultSize = list.Contains(c.DefaultSize) ? c.DefaultSize : list[0];
            }
        }

        JsonElement? def = Child(rooms.Value, "default_size");
        if (def.HasValue && def.Value.ValueKind == JsonValueKind.Number && def.Value.TryGetInt32(out int d) && System.Array.IndexOf(c.RoomSizes, d) >= 0)
        {
            c.DefaultSize = d;
        }

        JsonElement? trashers = Child(rooms.Value, "trashers");
        if (trashers.HasValue && trashers.Value.ValueKind == JsonValueKind.Object)
        {
            var map = new Dictionary<int, int>();
            foreach (JsonProperty p in trashers.Value.EnumerateObject())
            {
                if (int.TryParse(p.Name, out int size) && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int n) && n >= 0)
                {
                    map[size] = n;
                }
            }

            c.RoomTrashers = map;
        }
    }

    // ---------- tasks ----------

    private static IReadOnlyList<TaskTypeDef> ReadTasks(JsonElement array)
    {
        var list = new List<TaskTypeDef>();
        foreach (JsonElement e in array.EnumerateArray())
        {
            string id = e.GetProperty("type").GetString() ?? "";
            string cat = e.GetProperty("category").GetString() ?? "short";
            if (!Enum.TryParse(cat, true, out TaskCategory category))
            {
                throw new InvalidDataException("Unknown task category: " + cat);
            }

            int steps = e.GetProperty("steps").GetInt32();
            float minS = e.TryGetProperty("min_s", out JsonElement m) ? m.GetSingle() : 0f;
            var consoles = new List<string>();
            foreach (JsonElement c in e.GetProperty("consoles").EnumerateArray())
            {
                consoles.Add(c.GetString() ?? "");
            }

            list.Add(new TaskTypeDef(id, category, steps, minS, consoles));
        }

        return list;
    }

    // ---------- map ----------

    private static bool[] ReadGrid(JsonElement rows, int width, int height, string what)
    {
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != height)
        {
            throw new InvalidDataException("Map " + what + " must have " + height + " rows.");
        }

        var cells = new bool[width * height];
        int y = 0;
        foreach (JsonElement row in rows.EnumerateArray())
        {
            string s = row.GetString() ?? "";
            if (s.Length != width)
            {
                throw new InvalidDataException("Map " + what + " row " + y + " must be " + width + " chars.");
            }

            for (int x = 0; x < width; x++)
            {
                cells[y * width + x] = s[x] == '#';
            }

            y++;
        }

        return cells; // true = wall
    }

    private static Vec2 ReadVec(JsonElement e)
    {
        return new Vec2(e[0].GetSingle(), e[1].GetSingle());
    }

    private static ConsoleKind ReadKind(string? kind)
    {
        switch (kind)
        {
            case "task": return ConsoleKind.Task;
            case "emergency": return ConsoleKind.Emergency;
            case "fire_fix": return ConsoleKind.FireFix;
            case "smog_fix": return ConsoleKind.SmogFix;
            case "machine": return ConsoleKind.Machine;
            default: throw new InvalidDataException("Unknown console kind: " + kind);
        }
    }

    public static MapData ReadMap(JsonElement m)
    {
        int width = m.GetProperty("width").GetInt32();
        int height = m.GetProperty("height").GetInt32();
        bool[] walls = ReadGrid(m.GetProperty("walk"), width, height, "walk");
        bool[] sightWalls = m.TryGetProperty("sight", out JsonElement sight)
            ? ReadGrid(sight, width, height, "sight")
            : walls;

        var walkable = new bool[walls.Length];
        for (int i = 0; i < walls.Length; i++)
        {
            walkable[i] = !walls[i];
        }

        var zones = new List<ZoneDef>();
        if (m.TryGetProperty("zones", out JsonElement zs))
        {
            foreach (JsonElement z in zs.EnumerateArray())
            {
                JsonElement rect = z.GetProperty("rect");
                zones.Add(new ZoneDef(
                    z.GetProperty("id").GetString() ?? "",
                    rect[0].GetSingle(),
                    rect[1].GetSingle(),
                    rect[2].GetSingle(),
                    rect[3].GetSingle()));
            }
        }

        var dark = new List<ZoneDef>();
        if (m.TryGetProperty("dark", out JsonElement ds))
        {
            foreach (JsonElement d in ds.EnumerateArray())
            {
                JsonElement rect = d.GetProperty("rect");
                dark.Add(new ZoneDef(
                    d.GetProperty("id").GetString() ?? "",
                    rect[0].GetSingle(),
                    rect[1].GetSingle(),
                    rect[2].GetSingle(),
                    rect[3].GetSingle()));
            }
        }

        var gasRooms = new List<ZoneDef>();
        if (m.TryGetProperty("gas_rooms", out JsonElement gs))
        {
            foreach (JsonElement g in gs.EnumerateArray())
            {
                JsonElement rect = g.GetProperty("rect");
                gasRooms.Add(new ZoneDef(
                    g.GetProperty("id").GetString() ?? "",
                    rect[0].GetSingle(),
                    rect[1].GetSingle(),
                    rect[2].GetSingle(),
                    rect[3].GetSingle()));
            }
        }

        var consoles = new List<ConsoleDef>();
        foreach (string listName in new[] { "consoles", "special" })
        {
            if (!m.TryGetProperty(listName, out JsonElement list))
            {
                continue;
            }

            foreach (JsonElement c in list.EnumerateArray())
            {
                float range = c.TryGetProperty("range", out JsonElement r) ? r.GetSingle() : 1.2f;
                consoles.Add(new ConsoleDef(
                    c.GetProperty("id").GetString() ?? "",
                    ReadKind(c.GetProperty("kind").GetString()),
                    ReadVec(c.GetProperty("pos")),
                    range));
            }
        }

        var spawns = new List<Vec2>();
        foreach (JsonElement s in m.GetProperty("spawns").EnumerateArray())
        {
            spawns.Add(ReadVec(s));
        }

        string id = m.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() ?? "" : "";
        string version = m.TryGetProperty("version", out JsonElement vEl) ? vEl.GetString() ?? "" : "";
        return new MapData(width, height, walkable, sightWalls, zones, consoles, spawns, id, version, dark, gasRooms);
    }
}
