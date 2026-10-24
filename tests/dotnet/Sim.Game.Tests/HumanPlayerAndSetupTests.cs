using Sim.Core.Numerics;
using Sim.Game.Fouls;
using Sim.Game.HumanPlayer;
using Sim.Game.Setup;
using static GameTests.TestWorld;

namespace GameTests;

/// <summary>HUMAN PLAYER NECTAR rules (G426 p116, G427 p117, §10.8.2) and the G304 setup check.</summary>
public class HumanPlayerAndSetupTests
{
    [Fact]
    public void Nectar_may_only_be_entered_after_an_own_tip_or_in_the_last_60_seconds()
    {
        var hp = new NectarHumanPlayer(Rules, Field);
        var book = new FoulBook(Rules);
        Assert.Equal(5, hp.Reserve(Alliance.Red));
        Assert.False(hp.CanEnter(Alliance.Red, allEntryOpen: false).Allowed);
        Assert.False(hp.Enter(100, Alliance.Red, false, book).Allowed);
        Assert.Equal(5, hp.Reserve(Alliance.Red));
        Assert.Empty(book.Records);

        hp.OnTip(Alliance.Red);
        Assert.True(hp.Enter(200, Alliance.Red, false, book).Allowed);
        Assert.False(hp.CanEnter(Alliance.Red, false).Allowed);   // one per TIP
        Assert.Equal(4, hp.Reserve(Alliance.Red));
        Assert.Equal(4, hp.AllowedNow(Alliance.Red, allEntryOpen: true));
        Assert.True(hp.CanEnter(Alliance.Red, allEntryOpen: true).Allowed);
    }

    [Fact]
    public void Forcing_an_early_entry_is_a_minor_foul_per_nectar()
    {
        var hp = new NectarHumanPlayer(Rules, Field);
        var book = new FoulBook(Rules);
        hp.Enter(100, Alliance.Blue, false, book, force: true);
        hp.Enter(101, Alliance.Blue, false, book, force: true);
        Assert.Equal(2, book.Records.Count(r => r.Rule == "G426" && r.Penalty == Penalty.Minor && r.Alliance == Alliance.Blue));
        Assert.Equal(3, hp.Reserve(Alliance.Blue));
    }

    [Fact]
    public void Tips_of_the_other_hive_do_not_unlock_nectar()
    {
        var hp = new NectarHumanPlayer(Rules, Field);
        hp.OnTip(Alliance.Blue);
        Assert.False(hp.CanEnter(Alliance.Red, false).Allowed);
        Assert.True(hp.CanEnter(Alliance.Blue, false).Allowed);
    }

    [Fact]
    public void Nectar_that_left_the_field_may_be_re_entered_any_time()
    {
        var hp = new NectarHumanPlayer(Rules, Field);
        var book = new FoulBook(Rules);
        hp.OnNectarReturned(Alliance.Red);
        var decision = hp.Enter(100, Alliance.Red, false, book);
        Assert.True(decision.Allowed);
        Assert.Equal(EntrySource.Returned, decision.Source);
        Assert.Equal(5, hp.Reserve(Alliance.Red));
        Assert.Empty(book.Records);
    }

    [Fact]
    public void Nectar_must_first_touch_the_tile_in_the_own_loading_zone()
    {
        var hp = new NectarHumanPlayer(Rules, Field);
        var book = new FoulBook(Rules);
        var inZone = new Vec2(-1.65, -0.9);   // red loading zone
        Assert.True(hp.RecordLanding(1, Alliance.Red, 7, LandingContact.Tile, inZone, book));
        Assert.False(hp.RecordLanding(2, Alliance.Red, 8, LandingContact.Robot, inZone, book));
        Assert.False(hp.RecordLanding(3, Alliance.Red, 9, LandingContact.Tile, new Vec2(-1.0, 0.0), book));
        Assert.False(hp.RecordLanding(4, Alliance.Blue, 10, LandingContact.Tile, inZone, book));   // wrong zone
        Assert.Equal(3, book.Records.Count(r => r.Rule == "G427" && r.Penalty == Penalty.Minor));
    }

    // ---------------------------------------------------------------- G304 setup

    private static RobotState LegalRed() =>
        Robot(1, Alliance.Red, Field.WallXMin + HalfCube, 0.0, touchingWall: true);

    [Fact]
    public void Legal_start_position_has_no_problems()
    {
        Assert.Empty(SetupValidator.Validate(LegalRed(), Field, Rules, preloadsTouching: 4, atRest: true));
    }

    [Fact]
    public void Each_setup_violation_is_reported_with_its_rule()
    {
        static IEnumerable<string> Rules304(RobotState r, int preloads = 4, bool rest = true) =>
            SetupValidator.Validate(r, Field, Rules, preloads, rest).Select(p => p.Rule);

        Assert.Contains("G304.A", Rules304(LegalRed() with { Alliance = Alliance.Blue }));
        Assert.Contains("G304.C", Rules304(LegalRed() with { TouchingPerimeterWall = false }));
        Assert.Contains("G304.E", Rules304(Robot(1, Alliance.Red, Field.WallXMin + HalfCube, -0.9, touchingWall: true)));
        Assert.Contains("G304.F", Rules304(Robot(1, Alliance.Red, Field.WallXMin + 0.26, 0.0, touchingWall: true, half: 0.26)));
        Assert.Contains("G304.F", Rules304(LegalRed() with { TopY = 0.5 }));
        Assert.Contains("G304.G", Rules304(LegalRed(), preloads: 3));
        Assert.Contains("G304.H", Rules304(LegalRed(), rest: false));
    }

    [Fact]
    public void Robot_in_a_flower_scoring_volume_is_not_a_legal_start()
    {
        var f = Flower("flower_xmin").Volume;   // left (red) wall, z = +0.594
        var robot = Robot(1, Alliance.Red, Field.WallXMin + HalfCube, f.AxisXZ.Z, touchingWall: true);
        Assert.Contains(SetupValidator.Validate(robot, Field, Rules, 4, true), p => p.Rule == "G304.D");
    }
}
