using Sim.Core.Config;
using Sim.Game.Field;
using Sim.Game.Geometry;
using Sim.Game.Setup;
using Sim.Robots;

namespace MatchTests;

/// <summary>config/match.yaml start positions are legal (G304 p104) for every robot of the library at every station.</summary>
public class StartPositionTests
{
    private static readonly GameRules Rules = GameRules.Load(RepoPaths.RulesFile());
    private static readonly FieldGeometry Field = FieldGeometry.Load(RepoPaths.Find(Path.Combine("config", "field_mechanisms.yaml")));
    private static readonly MatchConfig Config = MatchConfig.Load();

    public static IEnumerable<object[]> Cases() =>
        from robot in Directory.GetFiles(RepoPaths.Find(Path.Combine("config", "robots")), "*.yaml").Select(Path.GetFileNameWithoutExtension)
        from station in Enum.GetValues<Station>()
        select new object[] { robot!, station };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_robot_starts_legally_at_every_station(string robotName, Station station)
    {
        var robot = RobotDefinition.Load(RobotDefinition.FindConfig(robotName));
        // Outline: the YAML bounding box around the robot origin (the CAD footprint is inside it).
        var (w, _, d) = (robot.BboxSize.X, robot.BboxSize.Y, robot.BboxSize.Z);
        var local = new List<Vec2> { new(-w / 2, -d / 2), new(w / 2, -d / 2), new(w / 2, d / 2), new(-w / 2, d / 2) };
        var pose = Config.StartPose(station, local, Field.WallXMin, Field.WallXMax, Field.WallZMin, Field.WallZMax);
        var (sin, cos) = Math.SinCos(pose.YawRad);
        var footprint = new Polygon2(local.Select(p => new Vec2(pose.X + p.X * cos + p.Z * sin, pose.Z - p.X * sin + p.Z * cos)));
        var touching = footprint.Vertices.Any(v => v.X <= Field.WallXMin + 0.005 || v.X >= Field.WallXMax - 0.005
                                                   || v.Z <= Field.WallZMin + 0.005 || v.Z >= Field.WallZMax - 0.005);
        var state = new RobotState(station.RobotId(), station.Alliance(), station.Label(), footprint, robot.BboxSize.Y,
            new Vec3(0, 0, 0), 0, touching, [], []);
        var problems = SetupValidator.Validate(state, Field, Rules, preloadsTouching: Rules.PollenPreloadPerRobot, atRest: true);
        Assert.True(problems.Count == 0, string.Join("; ", problems.Select(p => $"{p.Rule}: {p.Message}")));
        Assert.True(footprint.Vertices.All(v => Field.Interior.Contains(v)), "robot outside the perimeter");
    }

    [Fact]
    public void Robots_face_into_the_field_with_their_backs_to_the_wall()
    {
        var square = new List<Vec2> { new(-0.2, -0.2), new(0.2, -0.2), new(0.2, 0.2), new(-0.2, 0.2) };
        var red1 = Config.StartPose(Station.Red1, square, -1.8, 1.8, -1.8, 1.8);
        Assert.Equal(-1.8 + 0.2 + Config.WallGap, red1.X, 6);
        Assert.Equal(-Math.PI / 2, red1.YawRad, 6);   // facing +X, towards blue
        var blue2 = Config.StartPose(Station.Blue2, square, -1.8, 1.8, -1.8, 1.8);
        Assert.Equal(-1.8 + 0.2 + Config.WallGap, blue2.Z, 6);
        Assert.Equal(Math.PI, blue2.YawRad, 6);       // facing +Z, towards the audience
    }
}

public class MatchSetupBallPathTests
{
    [Fact]
    public void The_ball_path_choice_survives_the_replay_header()
    {
        var setup = Sim.Match.MatchSetup.AllStations(Sim.Match.StationKind.Gamepad) with { BallPath = "physical" };
        Assert.Equal("physical", Sim.Match.MatchSetup.FromJson(setup.ToJson()).BallPath);
        Assert.Null(Sim.Match.MatchSetup.FromJson(Sim.Match.MatchSetup.AllStations(Sim.Match.StationKind.Gamepad).ToJson()).BallPath);
    }
}
