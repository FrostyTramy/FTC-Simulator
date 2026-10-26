using System.Globalization;
using System.Text;
using System.Text.Json;
using Sim.Game;
using Sim.Game.Fouls;
using Sim.Game.Scoring;

namespace Sim.Match;

/// <summary>
/// Per-robot statistics of a MATCH (spec 08 post-match screen: distance driven, cycles, elements scored, shot accuracy,
/// battery used, max current).
/// </summary>
/// <param name="Launches">Elements the robot launched (guided path).</param>
/// <param name="Intakes">Elements the robot took in (intake captures; pre-loads not counted).</param>
/// <param name="Scored">Scoring achievements credited to elements this robot last controlled.</param>
public sealed record RobotStats(
    Station Station,
    string Team,
    string Robot,
    double DistanceM,
    int Intakes,
    int Launches,
    int Scored,
    double BatteryStartV,
    double BatteryMinV,
    double BatteryEndV,
    double PeakCurrentA,
    int Fouls)
{
    /// <summary>Scored achievements per launch, or null before the first launch.</summary>
    public double? ShotAccuracy => Launches == 0 ? null : (double)Scored / Launches;
}

/// <summary>Accumulates <see cref="RobotStats"/> tick by tick (driven by the simulator).</summary>
public sealed class RobotStatsTracker
{
    private double _distance;
    private (double X, double Z)? _last;
    private double _startV = double.NaN;
    private double _minV = double.PositiveInfinity;
    private double _endV;
    private double _peakA;

    public RobotStatsTracker(StationSetup station, string robot)
    {
        Station = station;
        Robot = robot;
    }

    public StationSetup Station { get; }
    public string Robot { get; }
    public int Intakes { get; private set; }
    public int Launches { get; private set; }

    /// <summary>One tick: robot origin on the floor, battery bus voltage and total current.</summary>
    public void Update(double x, double z, double busVolts, double totalCurrentA)
    {
        if (_last is { } p)
        {
            _distance += Math.Sqrt((x - p.X) * (x - p.X) + (z - p.Z) * (z - p.Z));
        }
        _last = (x, z);
        if (double.IsNaN(_startV))
        {
            _startV = busVolts;
        }
        _minV = Math.Min(_minV, busVolts);
        _endV = busVolts;
        _peakA = Math.Max(_peakA, totalCurrentA);
    }

    public void Intake() => Intakes++;

    public void Launch() => Launches++;

    public RobotStats Finish(IEnumerable<ScoringEvent> scoring, IEnumerable<FoulRecord> fouls)
    {
        var id = Station.Station.RobotId();
        return new RobotStats(Station.Station, Station.TeamLabel, Robot, Math.Round(_distance, 3), Intakes, Launches,
            scoring.Count(e => e.LastControlledBy == id || e.RobotId == id),
            Math.Round(double.IsNaN(_startV) ? 0 : _startV, 3), Math.Round(double.IsInfinity(_minV) ? 0 : _minV, 3),
            Math.Round(_endV, 3), Math.Round(_peakA, 2), fouls.Count(f => f.RobotId == id));
    }
}

/// <summary>
/// Everything kept about a finished (or aborted) MATCH (spec 08 "Match history saved locally, with replay files";
/// "Export results as CSV/JSON"): setup, the official-style score breakdown, every foul with its status, the scoring
/// events with their times, robot statistics, the replay file and the determinism hash.
/// </summary>
public sealed record MatchRecord(
    string Id,
    string Label,
    DateTime RecordedUtc,
    MatchSetup Setup,
    string Outcome,
    ScoreSheet Score,
    IReadOnlyList<FoulRecord> Fouls,
    IReadOnlyList<ScoringEvent> Scoring,
    IReadOnlyList<RobotStats> Robots,
    int TickRate,
    long MatchStartTick,
    long DurationTicks,
    string? ReplayFile,
    ulong StateHash,
    string SimBuild)
{
    /// <summary>"Red wins 54-32", "Tie 20-20" or "Aborted".</summary>
    public string Summary => Outcome != "completed" ? Outcome
        : Score.Winner switch
        {
            Alliance.Red => $"Red wins {Score.Red.Total}-{Score.Blue.Total}",
            Alliance.Blue => $"Blue wins {Score.Blue.Total}-{Score.Red.Total}",
            _ => $"Tie {Score.Red.Total}-{Score.Blue.Total}",
        };

    /// <summary>MATCH time of a tick as the field timer shows it ("2:13"), for event lists.</summary>
    public static string Clock(long tick, long matchStartTick, int tickRate, double autoS, double transitionS, double teleopS)
    {
        var t = (tick - matchStartTick) / (double)tickRate;
        double remaining = t < autoS ? autoS + teleopS - t : t < autoS + transitionS ? teleopS : autoS + transitionS + teleopS - t;
        remaining = Math.Max(0, remaining);
        var s = (int)Math.Ceiling(remaining - 1e-9);
        return $"{s / 60}:{s % 60:00}";
    }

    public string ToJson() => JsonSerializer.Serialize(this, MatchSetup.Json);

    public static MatchRecord FromJson(string json) =>
        JsonSerializer.Deserialize<MatchRecord>(json, MatchSetup.Json) ?? throw new InvalidDataException("empty match record");

    /// <summary>
    /// One row per ALLIANCE with the score sheet columns (Table 10-2 structure), then one row per robot, for spreadsheets.
    /// </summary>
    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("match,alliance,total,auto,teleop,leave,auto_park,auto_tips,auto_tip_points,teleop_park,teleop_tips,teleop_tip_points," +
                      "cell,flower_owned,bottom_nectar,garden,minor_fouls_by_opponent,major_fouls_by_opponent,penalty_points,ranking_points");
        foreach (var a in new[] { Score.Red, Score.Blue })
        {
            sb.AppendLine(string.Join(',', Csv(Id), a.Alliance.Key(), a.Total, a.AutoPoints, a.TeleopPoints, a.LeavePoints, a.AutoParkPoints,
                a.AutoTips, a.AutoTipPoints, a.TeleopParkPoints, a.TeleopTips, a.TeleopTipPoints, a.CellPoints, a.FlowerOwnedPoints,
                a.BottomNectarPoints, a.GardenPoints, a.MinorFoulsByOpponent, a.MajorFoulsByOpponent, a.PenaltyPoints, a.RankingPoints));
        }
        sb.AppendLine();
        sb.AppendLine("match,station,team,robot,distance_m,intakes,launches,scored,shot_accuracy,battery_start_v,battery_min_v,battery_end_v,peak_current_a,fouls");
        foreach (var r in Robots)
        {
            sb.AppendLine(string.Join(',', Csv(Id), r.Station.Key(), Csv(r.Team), Csv(r.Robot), F(r.DistanceM), r.Intakes, r.Launches, r.Scored,
                r.ShotAccuracy is { } acc ? F(acc) : "", F(r.BatteryStartV), F(r.BatteryMinV), F(r.BatteryEndV), F(r.PeakCurrentA), r.Fouls));
        }
        return sb.ToString();
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Csv(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}

/// <summary>One line of the match history list.</summary>
public sealed record MatchSummary(string Id, string Label, DateTime RecordedUtc, string Summary, string Path, string? ReplayPath);

/// <summary>
/// Match records saved locally (spec 08 "Match history"), one JSON file per MATCH in <see cref="Directory"/> with the
/// replay next to it. Ids sort by time.
/// </summary>
public sealed class MatchHistory
{
    public MatchHistory(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    /// <summary>A new id from the wall-clock time of recording (only a file name; never used in the simulation).</summary>
    public static string NewId(DateTime utc) => utc.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);

    public string ReplayPathFor(string id) => Path.Combine(Directory, id + ".ftcreplay");

    public string Save(MatchRecord record)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, record.Id + ".json");
        File.WriteAllText(path, record.ToJson());
        return path;
    }

    public MatchRecord Load(string id) => MatchRecord.FromJson(File.ReadAllText(Path.Combine(Directory, id + ".json")));

    /// <summary>Saved matches, newest first. Files that do not parse are skipped.</summary>
    public IReadOnlyList<MatchSummary> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }
        var list = new List<MatchSummary>();
        foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json"))
        {
            try
            {
                var r = MatchRecord.FromJson(File.ReadAllText(file));
                var replay = r.ReplayFile is { } f && File.Exists(Path.Combine(Directory, f)) ? Path.Combine(Directory, f) : null;
                list.Add(new MatchSummary(r.Id, r.Label, r.RecordedUtc, r.Summary, file, replay));
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or NotSupportedException)
            {
                // not a match record
            }
        }
        return list.OrderByDescending(s => s.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Writes the record as CSV next to its JSON; returns the path.</summary>
    public string ExportCsv(MatchRecord record)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, record.Id + ".csv");
        File.WriteAllText(path, record.ToCsv());
        return path;
    }
}
