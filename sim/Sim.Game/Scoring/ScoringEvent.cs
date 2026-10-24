using Sim.Core.Events;

namespace Sim.Game.Scoring;

/// <summary>Achievement categories of Table 10-2 p91.</summary>
public enum ScoreCategory
{
    Leave,
    AutoPark,
    TeleopPark,
    HiveTip,
    CellElement,
    FlowerOwnedElement,
    BottomNectarBonus,
    GardenElement,
}

/// <summary>
/// One scored achievement (spec 03: time, alliance, element, zone, last controlling robot, rule reference).
/// The score is a pure function of the list of these events plus the foul list (<see cref="ScoreSheet.Compute"/>).
/// </summary>
public sealed record ScoringEvent(
    long Tick,
    Alliance Alliance,
    ScoreCategory Category,
    MatchPeriod Period,
    int Points,
    int? ElementId,
    int? RobotId,
    string Zone,
    int? LastControlledBy,
    string Rule) : ISimEvent
{
    public string Type => "Score";

    public void AddTo(StableHasher hasher) => hasher
        .Add((int)Alliance).Add((int)Category).Add((int)Period).Add(Points)
        .Add(ElementId ?? -1).Add(RobotId ?? -1).Add(Zone).Add(LastControlledBy ?? -1).Add(Rule);
}
