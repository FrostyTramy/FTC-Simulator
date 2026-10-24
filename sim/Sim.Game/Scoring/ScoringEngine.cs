using Sim.Game.Field;
using Sim.Game.Geometry;

namespace Sim.Game.Scoring;

/// <summary>
/// Event-sourced scoring (spec 03): live HIVE TIPs, LEAVE/AUTO PARK at the end of AUTO, TELEOP PARK at the
/// end of the MATCH, CELL/FLOWER/GARDEN once everything is at rest. Results never depend on the order in
/// which physics lists elements or robots (everything is evaluated in id order).
/// </summary>
public sealed class ScoringEngine
{
    private readonly GameRules _rules;
    private readonly FieldGeometry _field;
    private readonly List<ScoringEvent> _events = [];
    private readonly Dictionary<Alliance, HiveTracker> _hives;

    public ScoringEngine(GameRules rules, FieldGeometry field)
    {
        _rules = rules;
        _field = field;
        _hives = new Dictionary<Alliance, HiveTracker>
        {
            [Alliance.Red] = new(field.Hinge(Alliance.Red), field.DamperEngageDeg, rules.InitialUpCell(Alliance.Red)),
            [Alliance.Blue] = new(field.Hinge(Alliance.Blue), field.DamperEngageDeg, rules.InitialUpCell(Alliance.Blue)),
        };
    }

    public IReadOnlyList<ScoringEvent> Events => _events;

    public int Tips(Alliance a) => _hives[a].Tips;

    public CellSide StableUpCell(Alliance a) => _hives[a].StableUp;

    /// <summary>HIVE TIPs (§10.5.1, assessed throughout the MATCH and until everything is at rest, §10.5 A).</summary>
    public IReadOnlyList<ScoringEvent> ObserveHives(WorldSnapshot snap, MatchPeriod period)
    {
        var added = new List<ScoringEvent>();
        foreach (var hive in snap.Hives.OrderBy(h => h.Alliance))
        {
            if (_hives[hive.Alliance].Observe(hive.AngleDeg))
            {
                var points = period == MatchPeriod.Auto ? _rules.HiveTipAuto : _rules.HiveTipTeleop;
                added.Add(new ScoringEvent(snap.Tick, hive.Alliance, ScoreCategory.HiveTip, period, points,
                    null, null, $"hive_{hive.Alliance.Key()}", null, "§10.5.1 p87, Table 10-2"));
            }
        }
        _events.AddRange(added);
        return added;
    }

    /// <summary>LEAVE and AUTO PARK, assessed at the end of AUTO (§10.5 F, §10.5.4 p90).</summary>
    public IReadOnlyList<ScoringEvent> AssessAutoEnd(WorldSnapshot snap)
    {
        var added = new List<ScoringEvent>();
        foreach (var robot in snap.RobotsById)
        {
            if (!robot.TouchingPerimeterWall)
            {
                added.Add(new ScoringEvent(snap.Tick, robot.Alliance, ScoreCategory.Leave, MatchPeriod.Auto, _rules.LeaveAuto,
                    null, robot.Id, "perimeter", null, "§10.5.4 p90 LEAVE"));
            }
            if (ZonePredicates.Intersects(_field.LoadingZone(robot.Alliance), robot))
            {
                added.Add(new ScoringEvent(snap.Tick, robot.Alliance, ScoreCategory.AutoPark, MatchPeriod.Auto, _rules.ParkAuto,
                    null, robot.Id, $"loading_zone_{robot.Alliance.Key()}", null, "§10.5.4 p90 PARK"));
            }
        }
        _events.AddRange(added);
        return added;
    }

    /// <summary>TELEOP PARK, assessed at the end of the MATCH (§10.5 G).</summary>
    public IReadOnlyList<ScoringEvent> AssessMatchEnd(WorldSnapshot snap)
    {
        var added = snap.RobotsById
            .Where(r => ZonePredicates.Intersects(_field.LoadingZone(r.Alliance), r))
            .Select(r => new ScoringEvent(snap.Tick, r.Alliance, ScoreCategory.TeleopPark, MatchPeriod.Teleop, _rules.ParkTeleop,
                null, r.Id, $"loading_zone_{r.Alliance.Key()}", null, "§10.5.4 p90 PARK"))
            .ToList();
        _events.AddRange(added);
        return added;
    }

    /// <summary>Final CELL, FLOWER and GARDEN assessment once everything is at rest (§10.5 C, D, E).</summary>
    public IReadOnlyList<ScoringEvent> AssessFinal(WorldSnapshot snap)
    {
        var added = EvaluateEndState(snap);
        _events.AddRange(added);
        return added;
    }

    /// <summary>
    /// End-of-MATCH achievements for a world state, without recording them (also used for the live
    /// scoreboard): elements in the upward CELL, FLOWER ownership and bottom NECTAR bonus, GARDEN.
    /// </summary>
    public IReadOnlyList<ScoringEvent> EvaluateEndState(WorldSnapshot snap)
    {
        var result = new List<ScoringEvent>();
        var elements = snap.ElementsById.Where(e => e.OnField).ToList();

        // §10.5.1 p87: POLLEN/NECTAR left in an upward-facing CELL score for that ALLIANCE (the HIVE owner, I-2).
        foreach (var hive in snap.Hives.OrderBy(h => h.Alliance))
        {
            var up = _field.Hinge(hive.Alliance).NearestStable(hive.AngleDeg);
            var box = _field.CellBox(hive.Alliance, up, hive.AngleDeg);
            foreach (var e in elements.Where(e => ZonePredicates.CentroidInside(box, e)))
            {
                result.Add(new ScoringEvent(snap.Tick, hive.Alliance, ScoreCategory.CellElement, MatchPeriod.Teleop,
                    _rules.CellElementEnd, e.Id, null, _field.Cell(hive.Alliance, up).Id, e.LastControlledBy, "§10.5.1 p87"));
            }
        }

        // §10.5.2 p87-88: FLOWER owner = top-most scoring NECTAR (I-3), bottom NECTAR bonus = bottom-most one.
        foreach (var flower in _field.Flowers)
        {
            var inside = elements.Where(e => ZonePredicates.Intersects(flower.Volume, e)).ToList();
            var nectar = inside.Where(e => e.Kind.IsNectar()).ToList();
            if (nectar.Count == 0)
            {
                continue;
            }
            var top = nectar.OrderByDescending(e => e.Position.Y).ThenBy(e => e.Id).First();
            var bottom = nectar.OrderBy(e => e.Position.Y).ThenBy(e => e.Id).First();
            var owner = top.Kind.NectarAlliance()!.Value;
            var bottomAlliance = bottom.Kind.NectarAlliance()!.Value;
            result.Add(new ScoringEvent(snap.Tick, bottomAlliance, ScoreCategory.BottomNectarBonus, MatchPeriod.Teleop,
                _rules.BottomNectarBonus, bottom.Id, null, flower.Id, bottom.LastControlledBy, "§10.5.2 p88 Bottom NECTAR Bonus"));
            foreach (var e in inside)
            {
                result.Add(new ScoringEvent(snap.Tick, owner, ScoreCategory.FlowerOwnedElement, MatchPeriod.Teleop,
                    _rules.OwnedFlowerElement, e.Id, null, flower.Id, e.LastControlledBy, "§10.5.2 p88 FLOWER Owner"));
            }
        }

        // §10.5.3 p89: any POLLEN or NECTAR at least partially in a GARDEN scores for the GARDEN's colour.
        foreach (var alliance in new[] { Alliance.Red, Alliance.Blue })
        {
            var garden = _field.Garden(alliance);
            foreach (var e in elements.Where(e => ZonePredicates.Intersects(garden, e)))
            {
                result.Add(new ScoringEvent(snap.Tick, alliance, ScoreCategory.GardenElement, MatchPeriod.Teleop,
                    _rules.GardenElement, e.Id, null, $"garden_{alliance.Key()}", e.LastControlledBy, "§10.5.3 p89"));
            }
        }
        return result;
    }
}
