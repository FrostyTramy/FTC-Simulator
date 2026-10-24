using Sim.Core.Events;

namespace Sim.Game.Fouls;

/// <summary>Strict: auto-detectable fouls apply immediately. Assist: everything waits for a referee/user decision. Off: nothing is recorded.</summary>
public enum FoulMode
{
    Strict,
    Assist,
    Off,
}

/// <summary>Penalties of Table 10-4 p92 that change the score or are recorded per instance.</summary>
public enum Penalty
{
    VerbalWarning,
    Minor,
    Major,
}

public enum Card
{
    None,
    Yellow,
    Red,
}

public enum FoulStatus
{
    Applied,
    Pending,
    Rejected,
}

/// <summary>One rule violation (spec 03 "Fouls and penalties"): rule, robot, time, evidence, severity, source.</summary>
/// <param name="Alliance">Offending ALLIANCE; MINOR/MAJOR points are credited to the opponent (Table 10-4).</param>
/// <param name="Count">Number of fouls of <see cref="Penalty"/> in this record (e.g. "per SCORING ELEMENT").</param>
/// <param name="Strategic">Escalated because the action is (likely) STRATEGIC (§10.6 p92).</param>
public sealed record FoulRecord(
    int Id,
    long Tick,
    string Rule,
    Alliance Alliance,
    int? RobotId,
    Penalty Penalty,
    int Count,
    Card Card,
    bool Strategic,
    string Evidence,
    string Source,
    FoulStatus Status) : ISimEvent
{
    public string Type => "Foul";

    public int PointsToOpponent(GameRules rules) => Penalty switch
    {
        Penalty.Minor => Count * rules.MinorFoulPoints,
        Penalty.Major => Count * rules.MajorFoulPoints,
        _ => 0,
    };

    public void AddTo(StableHasher hasher) => hasher
        .Add(Id).Add(Rule).Add((int)Alliance).Add(RobotId ?? -1).Add((int)Penalty).Add(Count).Add((int)Card)
        .Add(Strategic).Add(Evidence).Add((int)Status);
}

/// <summary>Collects fouls for one MATCH and applies the foul mode (Strict / Assist / Off).</summary>
public sealed class FoulBook
{
    private readonly GameRules _rules;
    private readonly List<FoulRecord> _records = [];

    public FoulBook(GameRules rules, FoulMode mode = FoulMode.Strict)
    {
        _rules = rules;
        Mode = mode;
    }

    public FoulMode Mode { get; }
    public IReadOnlyList<FoulRecord> Records => _records;
    public IEnumerable<FoulRecord> Applied => _records.Where(r => r.Status == FoulStatus.Applied);
    public IEnumerable<FoulRecord> PendingReview => _records.Where(r => r.Status == FoulStatus.Pending);

    /// <summary>
    /// Records a violation. Rules marked "review" in config/rules_biobuzz.yaml always wait for confirmation;
    /// "auto" rules apply in Strict mode. Returns null in Off mode.
    /// </summary>
    public FoulRecord? Report(long tick, string rule, Alliance alliance, int? robotId, Penalty penalty, string evidence,
        int count = 1, Card card = Card.None, bool strategic = false)
    {
        if (Mode == FoulMode.Off)
        {
            return null;
        }
        var status = Mode == FoulMode.Strict && _rules.SimHandling(rule) == "auto" ? FoulStatus.Applied : FoulStatus.Pending;
        var record = new FoulRecord(_records.Count + 1, tick, rule, alliance, robotId, penalty, count, card, strategic,
            evidence, _rules.SourceOf(rule), status);
        _records.Add(record);
        return record;
    }

    public bool Confirm(int id) => SetStatus(id, FoulStatus.Applied);

    public bool Reject(int id) => SetStatus(id, FoulStatus.Rejected);

    /// <summary>MINOR/MAJOR points credited to <paramref name="beneficiary"/> by applied opponent fouls.</summary>
    public int PenaltyPointsFor(Alliance beneficiary) =>
        Applied.Where(r => r.Alliance == beneficiary.Opponent()).Sum(r => r.PointsToOpponent(_rules));

    private bool SetStatus(int id, FoulStatus status)
    {
        var index = _records.FindIndex(r => r.Id == id);
        if (index < 0)
        {
            return false;
        }
        _records[index] = _records[index] with { Status = status };
        return true;
    }
}
