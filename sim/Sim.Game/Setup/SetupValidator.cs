using Sim.Game.Field;
using Sim.Game.Geometry;

namespace Sim.Game.Setup;

/// <summary>One reason why a robot is not set up legally (the MATCH does not start, G304 p104).</summary>
public sealed record SetupProblem(int RobotId, string Rule, string Message);

/// <summary>G304 p104 starting-position checks plus the R102 starting size.</summary>
public static class SetupValidator
{
    /// <param name="preloadsTouching">POLLEN pre-loads in contact with the robot (G304.G: exactly 4).</param>
    /// <param name="atRest">Robot is motionless after OpMode INIT (G304.H).</param>
    public static IReadOnlyList<SetupProblem> Validate(RobotState robot, FieldGeometry field, GameRules rules, int preloadsTouching, bool atRest)
    {
        var problems = new List<SetupProblem>();
        void Add(string rule, string message) => problems.Add(new SetupProblem(robot.Id, rule, message));

        var ownSide = field.Side(robot.Alliance);
        if (!robot.Footprint.Vertices.All(ownSide.Contains))
        {
            Add("G304.A", "robot is not fully on its own side of the FIELD (columns A-C red, D-F blue)");
        }
        if (!robot.TouchingPerimeterWall)
        {
            Add("G304.C", "robot is not touching the FIELD perimeter wall");
        }
        if (field.Flowers.Any(f => robot.Footprint.IntersectsCircle(f.Volume.AxisXZ, f.Volume.Radius)))
        {
            Add("G304.D", "robot is in the scoring volume of a FLOWER");
        }
        if (ZonePredicates.Intersects(field.LoadingZone(robot.Alliance), robot))
        {
            Add("G304.E", "robot is in the LOADING ZONE");
        }
        var cube = rules.StartCubeMetres;
        if (!robot.Footprint.FitsInRectangle(cube[0], cube[1], 1e-6) || robot.TopY > cube[2] + 1e-6)
        {
            Add("G304.F", "robot exceeds the 18 in STARTING CONFIGURATION (R102)");
        }
        if (preloadsTouching != rules.PollenPreloadPerRobot)
        {
            Add("G304.G", $"robot touches {preloadsTouching} pre-load POLLEN, must be exactly {rules.PollenPreloadPerRobot}");
        }
        if (!atRest)
        {
            Add("G304.H", "robot is moving after OpMode INIT");
        }
        return problems;
    }
}
