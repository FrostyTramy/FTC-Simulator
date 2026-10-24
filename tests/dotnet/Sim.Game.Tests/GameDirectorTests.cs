using Sim.Game.Fouls;
using Sim.Game.Match;
using static GameTests.TestWorld;

namespace GameTests;

/// <summary>Foul detectors and a whole MATCH through <see cref="GameDirector"/> (tick-driven, deterministic).</summary>
public class GameDirectorTests
{
    private const long TeleopStart = 9120;
    private const long FlowerUnlock = 37920 - 60 * 240;
    private const long MatchEnd = 37920;

    private static GameDirector Started(FoulMode mode = FoulMode.Strict)
    {
        var g = new GameDirector(Rules, Field, new GameOptions(mode));
        g.StartMatch(0);
        return g;
    }

    private static void StepTo(GameDirector g, long from, long to, Func<long, WorldSnapshot> world)
    {
        for (var t = from; t <= to; t++)
        {
            g.Step(world(t));
        }
    }

    [Fact]
    public void G410_nectar_into_a_flower_before_60_seconds_is_one_major_per_nectar()
    {
        var g = Started();
        var f = Flower("flower_xmin").Volume;
        var red = Robot(1, Alliance.Red, -1.4, 0.6);
        StepTo(g, 1, TeleopStart + 10, t => Snap(t, robots: [red]));
        // NECTAR enters the volume and stays there for a while: still a single foul.
        StepTo(g, TeleopStart + 11, TeleopStart + 500, t => Snap(t,
            [Nectar(10, Alliance.Red, f.AxisXZ.X, 0.3, f.AxisXZ.Z, controlledBy: 1)], [red]));
        var foul = Assert.Single(g.Fouls.Records, r => r.Rule == "G410");
        Assert.Equal(Penalty.Major, foul.Penalty);
        Assert.Equal(Alliance.Red, foul.Alliance);
        Assert.Equal(FoulStatus.Applied, foul.Status);
    }

    [Fact]
    public void G410_does_not_apply_in_the_last_minute()
    {
        var g = Started();
        var f = Flower("flower_zmax").Volume;
        var blue = Robot(2, Alliance.Blue, 0.6, 1.4);
        StepTo(g, 1, FlowerUnlock, t => Snap(t, robots: [blue]));
        StepTo(g, FlowerUnlock + 1, FlowerUnlock + 50, t => Snap(t,
            [Nectar(11, Alliance.Blue, f.AxisXZ.X, 0.3, f.AxisXZ.Z, controlledBy: 2)], [blue]));
        Assert.DoesNotContain(g.Fouls.Records, r => r.Rule == "G410");
    }

    [Fact]
    public void G421_pin_longer_than_3_seconds_is_a_major_plus_one_every_3_seconds()
    {
        var g = Started();
        var pinner = Robot(1, Alliance.Red, 0.3, 0.0);
        var pinned = Robot(2, Alliance.Blue, 0.75, 0.0);
        StepTo(g, 1, TeleopStart, t => Snap(t, robots: [pinner, pinned]));
        var start = TeleopStart + 1;
        StepTo(g, start, start + 3 * 240 - 1, t => Snap(t, robots: [pinner, pinned], pins: [new PinContact(1, 2)]));
        Assert.DoesNotContain(g.Fouls.Records, r => r.Rule == "G421");   // 720 ticks = exactly 3 s is allowed
        StepTo(g, start + 3 * 240, start + 3 * 240, t => Snap(t, robots: [pinner, pinned], pins: [new PinContact(1, 2)]));
        Assert.Single(g.Fouls.Records, r => r.Rule == "G421");          // first tick over 3 s
        StepTo(g, start + 3 * 240 + 1, start + 6 * 240, t => Snap(t, robots: [pinner, pinned], pins: [new PinContact(1, 2)]));
        var majors = g.Fouls.Records.Where(r => r.Rule == "G421").ToList();
        Assert.Equal(2, majors.Count);
        Assert.All(majors, m => Assert.Equal(Alliance.Red, m.Alliance));
    }

    [Fact]
    public void G407_controlling_too_many_elements_beyond_momentary_is_reported()
    {
        var g = Started();
        var greedy = Robot(1, Alliance.Red, -0.8, 0.0, controlled: [1, 2, 3, 4, 5, 6]);
        StepTo(g, 1, TeleopStart, t => Snap(t));
        StepTo(g, TeleopStart + 1, TeleopStart + 4 * 240, t => Snap(t, robots: [greedy]));
        var records = g.Fouls.Records.Where(r => r.Rule == "G407").ToList();
        Assert.Contains(records, r => r.Penalty == Penalty.VerbalWarning);
        Assert.Contains(records, r => r is { Penalty: Penalty.Major, Card: Card.Yellow, Strategic: true });   // 6 elements
    }

    [Fact]
    public void G416_expansion_beyond_29_inches_is_flagged_once_per_instance()
    {
        var g = Started();
        var tall = Robot(1, Alliance.Blue, 0.8, 0.0, topY: 0.80);
        StepTo(g, 1, 500, t => Snap(t, robots: [tall]));
        Assert.Single(g.Fouls.Records, r => r.Rule == "G416");
    }

    [Fact]
    public void G403_powered_movement_in_the_transition_is_a_warning()
    {
        var g = new GameDirector(Rules, Field, new GameOptions(FieldControl: FieldControlMode.Realistic));
        g.StartMatch(0);
        var moving = Robot(1, Alliance.Red, -0.8, 0.0, powered: true);
        StepTo(g, 1, 7300, t => Snap(t, robots: [moving]));
        var w = Assert.Single(g.Fouls.Records, r => r.Rule == "G403");
        Assert.Equal(Penalty.VerbalWarning, w.Penalty);
    }

    [Fact]
    public void G402_contact_with_an_opponent_on_its_own_side_during_auto_is_a_major()
    {
        var g = Started();
        var red = Robot(1, Alliance.Red, 0.15, 0.0, contacting: [2]);   // reaches across the centre line
        var blue = Robot(2, Alliance.Blue, 0.6, 0.0, contacting: [1]);
        StepTo(g, 1, 10, t => Snap(t, robots: [red, blue]));
        var foul = Assert.Single(g.Fouls.Records, r => r.Rule == "G402");
        Assert.Equal(Alliance.Red, foul.Alliance);
    }

    /// <summary>A scripted MATCH: one red robot leaves and parks in AUTO, the red hive tips in TELEOP, a
    /// pollen ends in the red garden; the result comes after the post-match settle.</summary>
    private static GameDirector PlayScriptedMatch(double hiveTipAngle = 60)
    {
        var g = Started();
        WorldSnapshot World(long t)
        {
            var x = t < 2000 ? Field.WallXMin + HalfCube : -1.6;   // drives into the red loading zone row
            var z = t < 2000 ? 0.0 : -0.9;
            var robot = Robot(1, Alliance.Red, x, z, touchingWall: t < 2000);
            var angle = t < 15000 ? 0 : Math.Min(hiveTipAngle, (t - 15000) * 0.05);
            var moving = t <= MatchEnd + 100;
            var pollen = Pollen(1, -1.5, PollenRadius, 1.76) with { Velocity = new Sim.Core.Numerics.Vec3(moving ? 0.1 : 0, 0, 0) };
            return Snap(t, [pollen], [robot], [new HiveState(Alliance.Red, angle), new HiveState(Alliance.Blue, 0)]);
        }
        StepTo(g, 1, MatchEnd + 2000, World);
        return g;
    }

    [Fact]
    public void Full_match_produces_the_expected_final_score()
    {
        var g = PlayScriptedMatch();
        Assert.Equal(MatchPhase.Results, g.Match.Phase);
        var red = g.FinalScore!.Red;
        Assert.Equal(3, red.LeavePoints);
        Assert.Equal(5, red.AutoParkPoints);
        Assert.Equal(5, red.TeleopParkPoints);
        Assert.Equal(1, red.TeleopTips);
        Assert.Equal(20, red.TeleopTipPoints);
        Assert.Equal(1, red.GardenPoints);
        Assert.Equal(34, red.Total);
        Assert.Equal(Alliance.Red, g.FinalScore.Winner);
        Assert.Equal(1, g.HumanPlayer.UnlockedByTips(Alliance.Red));
    }

    [Fact]
    public void Same_inputs_give_the_same_state_hash_and_different_inputs_do_not()
    {
        var a = PlayScriptedMatch();
        var b = PlayScriptedMatch();
        var c = PlayScriptedMatch(hiveTipAngle: 50);   // no TIP
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(a.FinalScore!.Hash(), b.FinalScore!.Hash());
        Assert.NotEqual(a.StateHash(), c.StateHash());
    }

    [Fact]
    public void Human_player_entry_is_refused_before_the_match()
    {
        var g = new GameDirector(Rules, Field);
        Assert.False(g.EnterNectar(Alliance.Red).Allowed);
    }
}
