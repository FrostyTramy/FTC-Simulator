using Sim.Core.Events;
using Sim.Game.Fouls;

namespace Sim.Game.Scoring;

/// <summary>One alliance's line of the score sheet (Table 10-2 p91 structure).</summary>
public sealed record AllianceScore(
    Alliance Alliance,
    int LeavePoints,
    int AutoParkPoints,
    int AutoTips,
    int AutoTipPoints,
    int TeleopParkPoints,
    int TeleopTips,
    int TeleopTipPoints,
    int CellPoints,
    int FlowerOwnedPoints,
    int BottomNectarPoints,
    int GardenPoints,
    int MinorFoulsByOpponent,
    int MajorFoulsByOpponent,
    int PenaltyPoints,
    bool SwarmRp,
    bool Pollinator1Rp,
    bool Pollinator2Rp,
    int WinTieRp,
    int RankingPoints)
{
    public int AutoPoints => LeavePoints + AutoParkPoints + AutoTipPoints;
    public int TeleopPoints => TeleopParkPoints + TeleopTipPoints + CellPoints + FlowerOwnedPoints + BottomNectarPoints + GardenPoints;
    public int Tips => AutoTips + TeleopTips;

    /// <summary>LEAVE + PARK points (SWARM RP condition, Table 10-2).</summary>
    public int LeaveParkPoints => LeavePoints + AutoParkPoints + TeleopParkPoints;

    public int Total => AutoPoints + TeleopPoints + PenaltyPoints;
}

/// <summary>Final or live score of both alliances, computed purely from scoring events and fouls.</summary>
public sealed record ScoreSheet(AllianceScore Red, AllianceScore Blue, string EventType)
{
    public AllianceScore For(Alliance a) => a == Alliance.Red ? Red : Blue;

    public Alliance? Winner => Red.Total > Blue.Total ? Alliance.Red : Blue.Total > Red.Total ? Alliance.Blue : null;

    public static ScoreSheet Compute(IEnumerable<ScoringEvent> events, IEnumerable<FoulRecord> fouls, GameRules rules, string? eventType = null)
    {
        var type = eventType ?? rules.DefaultEventType;
        var ev = events.ToList();
        var applied = fouls.Where(f => f.Status == FoulStatus.Applied).ToList();

        (int minor, int major, int points) PenaltiesFor(Alliance beneficiary)
        {
            var committed = applied.Where(f => f.Alliance == beneficiary.Opponent()).ToList();
            var minor = committed.Where(f => f.Penalty == Penalty.Minor).Sum(f => f.Count);
            var major = committed.Where(f => f.Penalty == Penalty.Major).Sum(f => f.Count);
            return (minor, major, minor * rules.MinorFoulPoints + major * rules.MajorFoulPoints);
        }

        AllianceScore Base(Alliance a)
        {
            int Sum(ScoreCategory c, MatchPeriod? p = null) =>
                ev.Where(e => e.Alliance == a && e.Category == c && (p is null || e.Period == p)).Sum(e => e.Points);
            int Count(ScoreCategory c, MatchPeriod p) => ev.Count(e => e.Alliance == a && e.Category == c && e.Period == p);
            var (minor, major, penaltyPoints) = PenaltiesFor(a);
            return new AllianceScore(a,
                Sum(ScoreCategory.Leave), Sum(ScoreCategory.AutoPark),
                Count(ScoreCategory.HiveTip, MatchPeriod.Auto), Sum(ScoreCategory.HiveTip, MatchPeriod.Auto),
                Sum(ScoreCategory.TeleopPark),
                Count(ScoreCategory.HiveTip, MatchPeriod.Teleop), Sum(ScoreCategory.HiveTip, MatchPeriod.Teleop),
                Sum(ScoreCategory.CellElement), Sum(ScoreCategory.FlowerOwnedElement), Sum(ScoreCategory.BottomNectarBonus),
                Sum(ScoreCategory.GardenElement),
                minor, major, penaltyPoints,
                false, false, false, 0, 0);
        }

        var red = Base(Alliance.Red);
        var blue = Base(Alliance.Blue);
        return new ScoreSheet(WithRankingPoints(red, blue, rules, type), WithRankingPoints(blue, red, rules, type), type);
    }

    /// <summary>RP (Table 10-2/10-3 p91): SWARM, POLLINATOR 1 and 2, WIN 3 / TIE 1. "TBA" thresholds award nothing.</summary>
    private static AllianceScore WithRankingPoints(AllianceScore own, AllianceScore opponent, GameRules rules, string eventType)
    {
        var swarm = rules.SwarmThreshold(eventType) is { } s && own.LeaveParkPoints >= s;
        var p1 = rules.Pollinator1Threshold(eventType) is { } t1 && own.Tips >= t1;
        var p2 = rules.Pollinator2Threshold(eventType) is { } t2 && own.Tips >= t2;
        var winTie = own.Total > opponent.Total ? rules.WinRp : own.Total == opponent.Total ? rules.TieRp : 0;
        var rp = (swarm ? rules.SwarmRp : 0) + (p1 ? rules.Pollinator1Rp : 0) + (p2 ? rules.Pollinator2Rp : 0) + winTie;
        return own with { SwarmRp = swarm, Pollinator1Rp = p1, Pollinator2Rp = p2, WinTieRp = winTie, RankingPoints = rp };
    }

    /// <summary>Determinism hash of every number on the sheet.</summary>
    public ulong Hash()
    {
        var h = new StableHasher().Add(EventType);
        foreach (var a in new[] { Red, Blue })
        {
            h.Add((int)a.Alliance).Add(a.LeavePoints).Add(a.AutoParkPoints).Add(a.AutoTips).Add(a.AutoTipPoints)
                .Add(a.TeleopParkPoints).Add(a.TeleopTips).Add(a.TeleopTipPoints).Add(a.CellPoints).Add(a.FlowerOwnedPoints)
                .Add(a.BottomNectarPoints).Add(a.GardenPoints).Add(a.MinorFoulsByOpponent).Add(a.MajorFoulsByOpponent)
                .Add(a.PenaltyPoints).Add(a.SwarmRp).Add(a.Pollinator1Rp).Add(a.Pollinator2Rp).Add(a.WinTieRp).Add(a.RankingPoints)
                .Add(a.Total);
        }
        return h.Value;
    }
}
