using System.Text.Json;
using System.Text.Json.Serialization;
using Sim.Core.Config;
using Sim.Core.Numerics;
using Sim.Game;
using Sim.Game.Fouls;
using static Sim.Core.Config.YamlObjects;

namespace Sim.Match;

/// <summary>The four driver stations of a BIOBUZZ MATCH (two per ALLIANCE).</summary>
public enum Station
{
    Red1,
    Red2,
    Blue1,
    Blue2,
}

/// <summary>Who drives a station's robot (spec 08 "Match setup": player, AI or empty).</summary>
public enum StationKind
{
    /// <summary>No robot: its 4 pre-load POLLEN go to its LOADING ZONE (§10.3.1 p83).</summary>
    Empty,

    /// <summary>A robot that stays where it was placed (no code, outputs off): a stand-in partner or opponent.</summary>
    Placeholder,

    /// <summary>Driven by a gamepad without robot code (the example TeleOp's controls in C#, practice and AI drivers).</summary>
    Gamepad,

    /// <summary>The team's OpModes in a sandboxed runner JVM, driven from its Driver Station (spec 06).</summary>
    Code,
}

public static class StationExtensions
{
    public static Alliance Alliance(this Station s) => s is Station.Red1 or Station.Red2 ? Game.Alliance.Red : Game.Alliance.Blue;

    /// <summary>Config key: "red_1", "blue_2" (config/match.yaml).</summary>
    public static string Key(this Station s) => s switch
    {
        Station.Red1 => "red_1",
        Station.Red2 => "red_2",
        Station.Blue1 => "blue_1",
        _ => "blue_2",
    };

    /// <summary>Display name as on the field: "Red 1".</summary>
    public static string Label(this Station s) => s switch
    {
        Station.Red1 => "Red 1",
        Station.Red2 => "Red 2",
        Station.Blue1 => "Blue 1",
        _ => "Blue 2",
    };

    /// <summary>Stable robot id used by the rules engine (1-4 in placement order: red 1, blue 1, red 2, blue 2, §10.3.4).</summary>
    public static int RobotId(this Station s) => s switch
    {
        Station.Red1 => 1,
        Station.Blue1 => 2,
        Station.Red2 => 3,
        _ => 4,
    };
}

/// <summary>
/// One station of the match setup. <paramref name="Robot"/> is a robot YAML name (config/robots); <paramref name="Team"/>
/// the team label on the scoreboard. For <see cref="StationKind.Code"/>: the TeamCode jar and the OpModes the drive
/// team selects (display names as the Driver Station lists them; null = none).
/// </summary>
public sealed record StationSetup(
    Station Station,
    StationKind Kind,
    string Robot = "gobilda_starter",
    string Team = "",
    string? TeamCodeJar = null,
    string? AutoOpMode = null,
    string? TeleOpMode = null,
    double BatteryCharge = 1.0)
{
    public string TeamLabel => string.IsNullOrEmpty(Team) ? Station.Label() : Team;
}

/// <summary>
/// Everything that defines a MATCH before it starts (spec 08 "Match setup"); stored in match records and replays so a
/// replay can rebuild the same field.
/// </summary>
public sealed record MatchSetup(
    IReadOnlyList<StationSetup> Stations,
    FoulMode FoulMode = FoulMode.Strict,
    bool AutoHumanPlayer = true,
    ulong Seed = 1,
    string? EventType = null,
    string Label = "Practice match")
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public StationSetup? Of(Station s) => Stations.FirstOrDefault(x => x.Station == s);

    /// <summary>Stations with a robot on the FIELD, in placement order (§10.3.4: red 1, blue 1, red 2, blue 2).</summary>
    public IEnumerable<StationSetup> Robots => Stations.Where(s => s.Kind != StationKind.Empty).OrderBy(s => s.Station.RobotId());

    /// <summary>A 2v2 setup with the same robot and kind at every station.</summary>
    public static MatchSetup AllStations(StationKind kind, string robot = "gobilda_starter", string? jar = null) =>
        new(Enum.GetValues<Station>().Select(s => new StationSetup(s, kind, robot, TeamCodeJar: jar)).ToList());

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static MatchSetup FromJson(string json) =>
        JsonSerializer.Deserialize<MatchSetup>(json, Json) ?? throw new InvalidDataException("empty match setup");

    /// <summary>Problems that keep this setup from starting (duplicate stations, unknown robots).</summary>
    public IReadOnlyList<string> Problems(Func<string, bool> robotExists)
    {
        var problems = new List<string>();
        foreach (var dup in Stations.GroupBy(s => s.Station).Where(g => g.Count() > 1))
        {
            problems.Add($"{dup.Key.Label()} is set up {dup.Count()} times");
        }
        foreach (var s in Robots.Where(s => !robotExists(s.Robot)))
        {
            problems.Add($"{s.Station.Label()}: unknown robot '{s.Robot}'");
        }
        foreach (var s in Robots.Where(s => s.Kind == StationKind.Code && string.IsNullOrEmpty(s.TeamCodeJar)))
        {
            problems.Add($"{s.Station.Label()}: robot code needs a built TeamCode jar");
        }
        foreach (var s in Robots.Where(s => s.BatteryCharge is < 0 or > 1))
        {
            problems.Add($"{s.Station.Label()}: battery charge must be 0..1");
        }
        return problems;
    }
}

/// <summary>Where a station's robot starts (config/match.yaml): back against <see cref="Wall"/>, facing into the FIELD.</summary>
public sealed record StartSpot(Station Station, string Wall, double Along);

/// <summary>A robot pose on the floor: origin (x, z) and yaw about +Y (0 = facing −Z, counter-clockwise from above).</summary>
public readonly record struct FloorPose(double X, double Z, double YawRad);

/// <summary>Match defaults from config/match.yaml.</summary>
public sealed class MatchConfig
{
    private MatchConfig(IReadOnlyDictionary<Station, StartSpot> spots, double wallGap, double autoDelay, double dropHeight,
        int poseEvery, int checkpointEvery)
    {
        Spots = spots;
        WallGap = wallGap;
        HumanPlayerAutoDelaySeconds = autoDelay;
        NectarDropHeight = dropHeight;
        PoseEveryTicks = poseEvery;
        CheckpointEveryTicks = checkpointEvery;
    }

    public IReadOnlyDictionary<Station, StartSpot> Spots { get; }
    public double WallGap { get; }
    public double HumanPlayerAutoDelaySeconds { get; }
    public double NectarDropHeight { get; }
    public int PoseEveryTicks { get; }
    public int CheckpointEveryTicks { get; }

    public static MatchConfig Load(string? path = null)
    {
        const string File = "match.yaml";
        var root = YamlObjects.Load(path ?? RepoPaths.Find(Path.Combine("config", File)));
        var stations = Child(root, "stations", File);
        var spots = new Dictionary<Station, StartSpot>();
        foreach (var s in Enum.GetValues<Station>())
        {
            var entry = Child(stations, s.Key(), $"{File} stations");
            spots[s] = new StartSpot(s, String(Child(entry, "wall", s.Key()), $"{s.Key()}.wall"), Value(entry, "along", s.Key()));
        }
        var hp = Child(root, "human_player", File);
        var replay = Child(root, "replay", File);
        return new MatchConfig(spots, Value(root, "wall_gap", File), Value(hp, "auto_delay", "human_player"),
            Value(hp, "drop_height", "human_player"), (int)Value(replay, "pose_every_ticks", "replay"),
            (int)Value(replay, "checkpoint_every_ticks", "replay"));
    }

    /// <summary>
    /// The pose that puts a robot with outline <paramref name="footprintLocal"/> (robot frame x right, z back) with its
    /// back against the spot's wall, facing into the FIELD, <see cref="WallGap"/> from the wall face.
    /// </summary>
    public FloorPose StartPose(Station station, IReadOnlyList<Vec2> footprintLocal, double wallXMin, double wallXMax, double wallZMin, double wallZMax)
    {
        var spot = Spots[station];
        var yaw = spot.Wall switch
        {
            "x_min" => -Math.PI / 2,   // facing +X
            "x_max" => Math.PI / 2,    // facing −X
            "z_max" => 0.0,            // facing −Z
            "z_min" => Math.PI,        // facing +Z
            _ => throw new InvalidDataException($"match.yaml {station.Key()}: unknown wall '{spot.Wall}'"),
        };
        var (sin, cos) = Math.SinCos(yaw);
        // World offset of each outline point at the origin (Godot Basis(Up, yaw): x' = x cos + z sin, z' = −x sin + z cos).
        var world = footprintLocal.Select(p => new Vec2(p.X * cos + p.Z * sin, -p.X * sin + p.Z * cos)).ToList();
        return spot.Wall switch
        {
            "x_min" => new FloorPose(wallXMin + WallGap - world.Min(p => p.X), spot.Along, yaw),
            "x_max" => new FloorPose(wallXMax - WallGap - world.Max(p => p.X), spot.Along, yaw),
            "z_max" => new FloorPose(spot.Along, wallZMax - WallGap - world.Max(p => p.Z), yaw),
            _ => new FloorPose(spot.Along, wallZMin + WallGap - world.Min(p => p.Z), yaw),
        };
    }

    private static double Value(object? node, string key, string path) =>
        Double(QuantityValue(Child(node, key, path), $"{path}.{key}"), $"{path}.{key}");
}
