using Sim.Game.Fouls;
using Sim.Game.Geometry;
using Sim.Game.Scoring;
using static GameTests.TestWorld;

namespace GameTests;

/// <summary>One test (or more) per scoring rule of docs/game_rules.md §8 (spec 00 acceptance).</summary>
public class ScoringTests
{
    private static ScoringEngine Engine() => new(Rules, Field);

    private static int Points(IEnumerable<ScoringEvent> events, Alliance a, ScoreCategory c) =>
        events.Where(e => e.Alliance == a && e.Category == c).Sum(e => e.Points);

    // ---------------------------------------------------------------- LEAVE / PARK (§10.5.4)

    [Fact]
    public void Leave_scores_3_when_the_robot_no_longer_touches_the_wall_at_the_end_of_auto()
    {
        var events = Engine().AssessAutoEnd(Snap(7200, robots:
        [
            Robot(1, Alliance.Red, -1.0, 0.0, touchingWall: false),
            Robot(2, Alliance.Red, -1.5, 0.3, touchingWall: true),
        ]));
        Assert.Equal(3, Points(events, Alliance.Red, ScoreCategory.Leave));
        Assert.Single(events, e => e.Category == ScoreCategory.Leave && e.RobotId == 1);
    }

    [Fact]
    public void Auto_park_needs_part_of_the_robot_in_the_own_loading_zone()
    {
        // Red loading zone: x -1.795..-1.501, z -1.184..-0.607 (config/field_mechanisms.yaml).
        var partly = Robot(1, Alliance.Red, -1.501 + HalfCube - 0.01, -0.9);   // 1 cm overlap
        var outside = Robot(2, Alliance.Red, -1.501 + HalfCube + 0.02, -0.9);  // 2 cm short
        var wrongZone = Robot(3, Alliance.Blue, -1.6, -0.9);                   // blue robot in the red zone
        var events = Engine().AssessAutoEnd(Snap(7200, robots: [partly, outside, wrongZone]));
        Assert.Equal(5, Points(events, Alliance.Red, ScoreCategory.AutoPark));
        Assert.Equal(0, Points(events, Alliance.Blue, ScoreCategory.AutoPark));
    }

    [Fact]
    public void Teleop_park_is_assessed_at_the_end_of_the_match()
    {
        var events = Engine().AssessMatchEnd(Snap(37920, robots:
        [
            Robot(1, Alliance.Blue, 1.6, 0.9),   // blue loading zone x 1.501..1.795, z 0.607..1.184
            Robot(2, Alliance.Blue, 0.5, 0.0),
        ]));
        Assert.Equal(5, Points(events, Alliance.Blue, ScoreCategory.TeleopPark));
    }

    // ---------------------------------------------------------------- HIVE TIP (§10.5.1)

    [Fact]
    public void Hive_tip_scores_when_the_other_stable_state_is_reached()
    {
        var engine = Engine();
        // Red hive: 0° = audience CELL up, 60° = rear CELL up; TIP within 2° of the other state.
        foreach (var angle in new[] { 0.0, 20, 40, 57.9 })
        {
            Assert.Empty(engine.ObserveHives(Snap(9200, hives: [new HiveState(Alliance.Red, angle)]), MatchPeriod.Teleop));
        }
        var tip = Assert.Single(engine.ObserveHives(Snap(9201, hives: [new HiveState(Alliance.Red, 58.5)]), MatchPeriod.Teleop));
        Assert.Equal(ScoreCategory.HiveTip, tip.Category);
        Assert.Equal(20, tip.Points);
        Assert.Equal(CellSide.Rear, engine.StableUpCell(Alliance.Red));
        Assert.Empty(engine.ObserveHives(Snap(9202, hives: [new HiveState(Alliance.Red, 60)]), MatchPeriod.Teleop));
    }

    [Fact]
    public void Hive_that_swings_back_before_reaching_the_dampers_does_not_tip()
    {
        var engine = Engine();
        foreach (var angle in new[] { 10.0, 45, 55, 30, 0 })
        {
            Assert.Empty(engine.ObserveHives(Snap(9200, hives: [new HiveState(Alliance.Red, angle)]), MatchPeriod.Teleop));
        }
        Assert.Equal(0, engine.Tips(Alliance.Red));
    }

    [Fact]
    public void Each_tip_back_and_forth_counts_and_auto_tips_score_in_auto()
    {
        var engine = Engine();
        var auto = engine.ObserveHives(Snap(100, hives: [new HiveState(Alliance.Blue, -59)]), MatchPeriod.Auto);
        var teleop = engine.ObserveHives(Snap(10000, hives: [new HiveState(Alliance.Blue, -1)]), MatchPeriod.Teleop);
        Assert.Equal(MatchPeriod.Auto, Assert.Single(auto).Period);
        Assert.Equal(MatchPeriod.Teleop, Assert.Single(teleop).Period);
        Assert.Equal(2, engine.Tips(Alliance.Blue));
    }

    // ---------------------------------------------------------------- CELL (§10.5.1)

    [Fact]
    public void Elements_left_in_the_upward_cell_score_2_each_for_the_hive_owner()
    {
        var up = CellCentre(Alliance.Red, CellSide.Audience);   // red: audience CELL up at 0°
        var down = CellCentre(Alliance.Red, CellSide.Rear);
        var events = Engine().EvaluateEndState(Snap(38000, elements:
        [
            Pollen(1, up.X, up.Y, up.Z),
            Nectar(2, Alliance.Blue, up.X + 0.05, up.Y, up.Z),     // any element counts
            Pollen(3, down.X, down.Y, down.Z),                       // downward CELL: no points
        ]));
        Assert.Equal(4, Points(events, Alliance.Red, ScoreCategory.CellElement));
        Assert.Equal(0, Points(events, Alliance.Blue, ScoreCategory.CellElement));
    }

    [Fact]
    public void After_a_tip_the_other_cell_is_the_scoring_cell()
    {
        var rearAt60 = Field.CellBox(Alliance.Red, CellSide.Rear, 60).Centre;
        var events = Engine().EvaluateEndState(Snap(38000, elements: [Pollen(1, rearAt60.X, rearAt60.Y, rearAt60.Z)],
            hives: [new HiveState(Alliance.Red, 60), new HiveState(Alliance.Blue, 0)]));
        Assert.Equal(2, Points(events, Alliance.Red, ScoreCategory.CellElement));
    }

    // ---------------------------------------------------------------- FLOWER (§10.5.2)

    [Fact]
    public void Top_most_nectar_owns_the_flower_and_bottom_most_gets_the_bonus()
    {
        var f = Flower("flower_xmin").Volume;
        var (x, z) = (f.AxisXZ.X, f.AxisXZ.Z);
        var events = Engine().EvaluateEndState(Snap(38000, elements:
        [
            Nectar(1, Alliance.Red, x, 0.45, z),    // top-most NECTAR → red owns
            Pollen(2, x, 0.35, z),
            Nectar(3, Alliance.Blue, x, 0.25, z),   // bottom-most NECTAR → blue bonus
        ]));
        Assert.Equal(3 * 2, Points(events, Alliance.Red, ScoreCategory.FlowerOwnedElement));
        Assert.Equal(0, Points(events, Alliance.Blue, ScoreCategory.FlowerOwnedElement));
        Assert.Equal(5, Points(events, Alliance.Blue, ScoreCategory.BottomNectarBonus));
        Assert.Equal(0, Points(events, Alliance.Red, ScoreCategory.BottomNectarBonus));
    }

    [Fact]
    public void Flower_without_nectar_scores_nothing()
    {
        var f = Flower("flower_zmax").Volume;
        var events = Engine().EvaluateEndState(Snap(38000, elements: [Pollen(1, f.AxisXZ.X, 0.3, f.AxisXZ.Z)]));
        Assert.DoesNotContain(events, e => e.Category is ScoreCategory.FlowerOwnedElement or ScoreCategory.BottomNectarBonus);
    }

    [Fact]
    public void Single_nectar_gives_its_alliance_ownership_and_the_bottom_bonus()
    {
        var f = Flower("flower_xmax").Volume;
        var events = Engine().EvaluateEndState(Snap(38000, elements: [Nectar(1, Alliance.Blue, f.AxisXZ.X, 0.3, f.AxisXZ.Z)]));
        Assert.Equal(2, Points(events, Alliance.Blue, ScoreCategory.FlowerOwnedElement));
        Assert.Equal(5, Points(events, Alliance.Blue, ScoreCategory.BottomNectarBonus));
    }

    [Fact]
    public void Element_only_partially_in_the_flower_volume_still_scores()
    {
        var f = Flower("flower_zmin").Volume;
        // Centre just below the volume, sphere reaches 1 cm into it (§10.5.2 "at least partially within").
        var events = Engine().EvaluateEndState(Snap(38000, elements:
            [Nectar(1, Alliance.Red, f.AxisXZ.X, f.YMin - NectarRadius + 0.01, f.AxisXZ.Z)]));
        Assert.Equal(2, Points(events, Alliance.Red, ScoreCategory.FlowerOwnedElement));
        var below = Engine().EvaluateEndState(Snap(38000, elements:
            [Nectar(1, Alliance.Red, f.AxisXZ.X, f.YMin - NectarRadius - 0.01, f.AxisXZ.Z)]));
        Assert.DoesNotContain(below, e => e.Category == ScoreCategory.FlowerOwnedElement);
    }

    // ---------------------------------------------------------------- GARDEN (§10.5.3)

    [Fact]
    public void Garden_scores_1_per_element_at_least_partially_inside_for_the_garden_colour()
    {
        // Red garden: x -1.795..-1.204, z 1.730..1.795 (along the audience wall).
        var events = Engine().EvaluateEndState(Snap(38000, elements:
        [
            Pollen(1, -1.5, PollenRadius, 1.76),                          // inside
            Nectar(2, Alliance.Blue, -1.4, NectarRadius, 1.75),           // opponent NECTAR counts for red
            Pollen(3, -1.5, PollenRadius, 1.7298 - PollenRadius + 0.005), // edge overlaps 5 mm
            Pollen(4, -1.5, PollenRadius, 1.7298 - PollenRadius - 0.005), // 5 mm short
        ]));
        Assert.Equal(3, Points(events, Alliance.Red, ScoreCategory.GardenElement));
        Assert.Equal(0, Points(events, Alliance.Blue, ScoreCategory.GardenElement));
    }

    [Fact]
    public void Off_field_elements_score_nothing()
    {
        var up = CellCentre(Alliance.Red, CellSide.Audience);
        var e = Pollen(1, up.X, up.Y, up.Z) with { OnField = false };
        Assert.Empty(Engine().EvaluateEndState(Snap(38000, elements: [e])));
    }

    // ---------------------------------------------------------------- totals, RP, penalties (Table 10-2/10-3/10-4)

    private static ScoringEvent Ev(Alliance a, ScoreCategory c, MatchPeriod p, int points) =>
        new(0, a, c, p, points, null, null, "test", null, "test");

    [Fact]
    public void Score_sheet_adds_auto_teleop_and_penalties()
    {
        var events = new[]
        {
            Ev(Alliance.Red, ScoreCategory.Leave, MatchPeriod.Auto, 3),
            Ev(Alliance.Red, ScoreCategory.HiveTip, MatchPeriod.Auto, 20),
            Ev(Alliance.Red, ScoreCategory.GardenElement, MatchPeriod.Teleop, 1),
        };
        var book = new FoulBook(Rules);
        book.Report(1, "G410", Alliance.Blue, 3, Penalty.Major, "test");   // auto rule: applied in Strict mode
        book.Report(2, "G426", Alliance.Blue, null, Penalty.Minor, "test");
        var sheet = ScoreSheet.Compute(events, book.Records, Rules);
        Assert.Equal(23, sheet.Red.AutoPoints);
        Assert.Equal(1, sheet.Red.TeleopPoints);
        Assert.Equal(25, sheet.Red.PenaltyPoints);
        Assert.Equal(49, sheet.Red.Total);
        Assert.Equal(Alliance.Red, sheet.Winner);
    }

    [Fact]
    public void Ranking_points_follow_the_thresholds_for_other_events()
    {
        var events = new List<ScoringEvent>
        {
            Ev(Alliance.Blue, ScoreCategory.Leave, MatchPeriod.Auto, 3),
            Ev(Alliance.Blue, ScoreCategory.Leave, MatchPeriod.Auto, 3),
            Ev(Alliance.Blue, ScoreCategory.AutoPark, MatchPeriod.Auto, 5),
            Ev(Alliance.Blue, ScoreCategory.TeleopPark, MatchPeriod.Teleop, 5),   // 16 → SWARM
        };
        events.AddRange(Enumerable.Range(0, 7).Select(_ => Ev(Alliance.Blue, ScoreCategory.HiveTip, MatchPeriod.Teleop, 20)));
        events.AddRange(Enumerable.Range(0, 4).Select(_ => Ev(Alliance.Red, ScoreCategory.HiveTip, MatchPeriod.Teleop, 20)));
        var sheet = ScoreSheet.Compute(events, [], Rules, "other");
        Assert.True(sheet.Blue.SwarmRp);
        Assert.True(sheet.Blue.Pollinator1Rp);
        Assert.True(sheet.Blue.Pollinator2Rp);
        Assert.Equal(1 + 1 + 1 + 3, sheet.Blue.RankingPoints);
        Assert.False(sheet.Red.SwarmRp);
        Assert.True(sheet.Red.Pollinator1Rp);
        Assert.False(sheet.Red.Pollinator2Rp);
        Assert.Equal(1, sheet.Red.RankingPoints);
    }

    [Fact]
    public void Swarm_needs_16_points_not_15()
    {
        var events = new[]
        {
            Ev(Alliance.Red, ScoreCategory.AutoPark, MatchPeriod.Auto, 5),
            Ev(Alliance.Red, ScoreCategory.AutoPark, MatchPeriod.Auto, 5),
            Ev(Alliance.Red, ScoreCategory.TeleopPark, MatchPeriod.Teleop, 5),
        };
        Assert.False(ScoreSheet.Compute(events, [], Rules, "other").Red.SwarmRp);
    }

    [Fact]
    public void Tie_gives_both_alliances_one_ranking_point()
    {
        var sheet = ScoreSheet.Compute([], [], Rules, "other");
        Assert.Null(sheet.Winner);
        Assert.Equal(1, sheet.Red.WinTieRp);
        Assert.Equal(1, sheet.Blue.WinTieRp);
    }

    [Fact]
    public void Championship_thresholds_are_tba_so_no_threshold_rp_is_awarded()
    {
        var events = Enumerable.Range(0, 9).Select(_ => Ev(Alliance.Red, ScoreCategory.HiveTip, MatchPeriod.Teleop, 20));
        var sheet = ScoreSheet.Compute(events, [], Rules, "championship");
        Assert.False(sheet.Red.Pollinator1Rp);
        Assert.Equal(3, sheet.Red.RankingPoints);   // WIN only
    }

    [Fact]
    public void Assist_mode_fouls_count_only_after_confirmation()
    {
        var book = new FoulBook(Rules, FoulMode.Assist);
        var foul = book.Report(1, "G410", Alliance.Red, 1, Penalty.Major, "test")!;
        Assert.Equal(FoulStatus.Pending, foul.Status);
        Assert.Equal(0, ScoreSheet.Compute([], book.Records, Rules).Blue.PenaltyPoints);
        book.Confirm(foul.Id);
        Assert.Equal(20, ScoreSheet.Compute([], book.Records, Rules).Blue.PenaltyPoints);
    }

    [Fact]
    public void Review_rules_wait_for_the_referee_even_in_strict_mode()
    {
        var book = new FoulBook(Rules, FoulMode.Strict);
        Assert.Equal(FoulStatus.Pending, book.Report(1, "G417", Alliance.Red, 1, Penalty.Major, "hive contact")!.Status);
    }

    [Fact]
    public void Zone_predicates_distinguish_inside_centroid_and_partial()
    {
        var zone = Polygon2.Rect(0, 1, 0, 1);
        var ball = Pollen(1, 1.02, PollenRadius, 0.5);   // centre 2 cm outside, edge 1.6 cm inside
        Assert.True(ZonePredicates.Intersects(zone, ball));
        Assert.False(ZonePredicates.CentroidInside(zone, ball));
        Assert.False(ZonePredicates.FullyInside(zone, ball));
        Assert.True(ZonePredicates.FullyInside(zone, Pollen(2, 0.5, PollenRadius, 0.5)));
    }
}
