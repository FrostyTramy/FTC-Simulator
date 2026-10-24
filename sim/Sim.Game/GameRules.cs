using Sim.Core.Config;

namespace Sim.Game;

/// <summary>Audio cue of Table 9-1 p79 (event key, field timer value, spoken/played cue).</summary>
public sealed record AudioCueRule(string Event, string Timer, string Cue);

/// <summary>
/// Typed view of config/rules_biobuzz.yaml for the game logic. Every number comes from the config
/// (the manual is law); <see cref="Config"/> keeps the § source of each value.
/// </summary>
public sealed class GameRules
{
    private GameRules(RulesConfig c)
    {
        Config = c;

        // Timing (§10.4 p85, Table 9-1 p79, G410, G426)
        AutoSeconds = c.Get("timing.auto").Seconds();
        TransitionSeconds = c.Get("timing.transition").Seconds();
        TeleopSeconds = c.Get("timing.teleop").Seconds();
        FlowerUnlockRemainingSeconds = c.Get("timing.flower_scoring_unlock_remaining").Seconds();
        NectarAllEntryRemainingSeconds = c.Get("timing.nectar_all_entry_remaining").Seconds();
        FinalWarningRemainingSeconds = c.Get("timing.final_warning_remaining").Seconds();
        AudioCues = c.Get("timing.audio_cues").AsList()
            .Select((o, i) =>
            {
                var m = YamlObjects.Map(o, $"timing.audio_cues[{i}]");
                return new AudioCueRule((string)m["event"]!, (string)m["timer"]!, (string)m["cue"]!);
            })
            .ToList();

        // Points (Table 10-2 p91)
        LeaveAuto = c.Get("scoring.leave_auto").AsInt();
        ParkAuto = c.Get("scoring.park_auto").AsInt();
        ParkTeleop = c.Get("scoring.park_teleop").AsInt();
        HiveTipAuto = c.Get("scoring.hive_tip_auto").AsInt();
        HiveTipTeleop = c.Get("scoring.hive_tip_teleop").AsInt();
        CellElementEnd = c.Get("scoring.cell_element_end").AsInt();
        BottomNectarBonus = c.Get("scoring.bottom_nectar_bonus").AsInt();
        OwnedFlowerElement = c.Get("scoring.owned_flower_element").AsInt();
        GardenElement = c.Get("scoring.garden_element").AsInt();
        NectarUnlockedPerTip = c.Get("scoring.nectar_unlocked_per_tip").AsInt();

        // Ranking points (Table 10-2/10-3 p91)
        DefaultEventType = c.Get("ranking.event_type_default").AsString();
        SwarmRp = c.Get("ranking.swarm_rp").AsInt();
        Pollinator1Rp = c.Get("ranking.pollinator1_rp").AsInt();
        Pollinator2Rp = c.Get("ranking.pollinator2_rp").AsInt();
        WinRp = c.Get("ranking.win_rp").AsInt();
        TieRp = c.Get("ranking.tie_rp").AsInt();

        // Penalties (Table 10-4 p92, §10.6 p92, Table 10-6 p95)
        MinorFoulPoints = c.Get("penalties.minor_foul").AsInt();
        MajorFoulPoints = c.Get("penalties.major_foul").AsInt();
        MomentaryMaxSeconds = c.Get("penalties.momentary_max").Seconds();
        ContinuousMinSeconds = c.Get("penalties.continuous_min").Seconds();
        RepeatIntervalMajorSeconds = c.Get("penalties.repeat_interval_major").Seconds();

        // Setup (§10.3.1 p83-84)
        NectarInAllianceArea = c.Get("setup.nectar_in_alliance_area").AsInt();
        NectarInUpCell = c.Get("setup.nectar_in_up_cell").AsInt();
        PollenPreloadPerRobot = c.Get("setup.pollen_preload_per_robot").AsInt();
        RobotsPerAlliance = c.Get("setup.robots_per_alliance").AsInt();
        var initialUp = c.Get("hive.initial_up_cell").AsMap();
        RedInitialUpCell = ParseCell((string)initialUp["red"]!);
        BlueInitialUpCell = ParseCell((string)initialUp["blue"]!);

        // Robot limits (R102, R105.A)
        StartCubeMetres = c.Get("robot_limits.start_cube").MetresArray();
        ExpansionMetres = c.Get("robot_limits.expansion").MetresArray();

        // Foul parameters (G407 p108, G421 p114)
        var g407 = c.Foul("G407").Parameters;
        G407MaxControlled = g407["max_controlled"].AsInt();
        G407LikelyStrategicCount = g407["likely_strategic_count"].AsInt();
        var g421 = c.Foul("G421").Parameters;
        G421MaxPinSeconds = g421["max_pin"].Seconds();
        G421SeparationMetres = g421["separation"].Metres();
        G421SeparationSeconds = g421["separation_time"].Seconds();
    }

    public RulesConfig Config { get; }

    public double AutoSeconds { get; }
    public double TransitionSeconds { get; }
    public double TeleopSeconds { get; }
    public double FlowerUnlockRemainingSeconds { get; }
    public double NectarAllEntryRemainingSeconds { get; }
    public double FinalWarningRemainingSeconds { get; }
    public IReadOnlyList<AudioCueRule> AudioCues { get; }

    public int LeaveAuto { get; }
    public int ParkAuto { get; }
    public int ParkTeleop { get; }
    public int HiveTipAuto { get; }
    public int HiveTipTeleop { get; }
    public int CellElementEnd { get; }
    public int BottomNectarBonus { get; }
    public int OwnedFlowerElement { get; }
    public int GardenElement { get; }
    public int NectarUnlockedPerTip { get; }

    public string DefaultEventType { get; }
    public int SwarmRp { get; }
    public int Pollinator1Rp { get; }
    public int Pollinator2Rp { get; }
    public int WinRp { get; }
    public int TieRp { get; }

    public int MinorFoulPoints { get; }
    public int MajorFoulPoints { get; }
    public double MomentaryMaxSeconds { get; }
    public double ContinuousMinSeconds { get; }
    public double RepeatIntervalMajorSeconds { get; }

    public int NectarInAllianceArea { get; }
    public int NectarInUpCell { get; }
    public int PollenPreloadPerRobot { get; }
    public int RobotsPerAlliance { get; }
    public CellSide RedInitialUpCell { get; }
    public CellSide BlueInitialUpCell { get; }

    /// <summary>R102 starting configuration (width, length, height), metres.</summary>
    public double[] StartCubeMetres { get; }

    /// <summary>R105.A expansion limit (footprint, footprint, height), metres.</summary>
    public double[] ExpansionMetres { get; }

    public int G407MaxControlled { get; }
    public int G407LikelyStrategicCount { get; }
    public double G421MaxPinSeconds { get; }
    public double G421SeparationMetres { get; }
    public double G421SeparationSeconds { get; }

    public CellSide InitialUpCell(Alliance a) => a == Alliance.Red ? RedInitialUpCell : BlueInitialUpCell;

    public string AudioCueText(string eventKey) => AudioCues.FirstOrDefault(a => a.Event == eventKey)?.Cue ?? eventKey;

    /// <summary>SWARM threshold in points for an event type ("other", "regional", "championship"); null when "TBA".</summary>
    public int? SwarmThreshold(string eventType) => Threshold("ranking.swarm_threshold_points", eventType);

    public int? Pollinator1Threshold(string eventType) => Threshold("ranking.pollinator1_threshold_tips", eventType);

    public int? Pollinator2Threshold(string eventType) => Threshold("ranking.pollinator2_threshold_tips", eventType);

    /// <summary>Source reference of a rule (e.g. "G410 p109") or config path.</summary>
    public string SourceOf(string ruleOrPath) => Config.SourceOf(ruleOrPath);

    /// <summary>"auto", "review" or "no" handling of a foul rule in the sim (rules_biobuzz.yaml fouls list).</summary>
    public string SimHandling(string rule) => Config.Fouls.FirstOrDefault(f => f.Rule == rule)?.Sim ?? "review";

    public static GameRules Load(string? path = null) => new(RulesConfig.Load(path ?? RepoPaths.RulesFile()));

    public static GameRules FromConfig(RulesConfig config) => new(config);

    private int? Threshold(string path, string eventType)
    {
        var map = Config.Get(path).AsMap();
        if (!map.TryGetValue(eventType, out var raw))
        {
            throw new ConfigException($"{path}: unknown event type '{eventType}'");
        }
        var d = Quantity.TryParseDouble(raw);
        return d is null ? null : (int)d.Value;
    }

    private static CellSide ParseCell(string s) => s switch
    {
        "audience" => CellSide.Audience,
        "rear" => CellSide.Rear,
        _ => throw new ConfigException($"hive.initial_up_cell: unknown cell '{s}'"),
    };
}
