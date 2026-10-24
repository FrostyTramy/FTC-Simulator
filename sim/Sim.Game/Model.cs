using Sim.Core.Numerics;
using Sim.Game.Geometry;

namespace Sim.Game;

public enum Alliance
{
    Red,
    Blue,
}

public enum ElementKind
{
    Pollen,
    NectarRed,
    NectarBlue,
}

/// <summary>The two CELLS of a HIVE (§9.6.2): the one on the audience side (+Z) and the one on the rear side (-Z).</summary>
public enum CellSide
{
    Audience,
    Rear,
}

/// <summary>Scoring period of an achievement (Table 10-2 columns).</summary>
public enum MatchPeriod
{
    Auto,
    Teleop,
}

public static class GameEnumExtensions
{
    public static Alliance Opponent(this Alliance a) => a == Alliance.Red ? Alliance.Blue : Alliance.Red;

    /// <summary>Lower-case key used in the config files ("red", "blue").</summary>
    public static string Key(this Alliance a) => a == Alliance.Red ? "red" : "blue";

    public static Alliance ParseAlliance(string s) => s.Trim().ToLowerInvariant() switch
    {
        "red" => Alliance.Red,
        "blue" => Alliance.Blue,
        _ => throw new ArgumentException($"unknown alliance '{s}'"),
    };

    public static bool IsNectar(this ElementKind k) => k != ElementKind.Pollen;

    /// <summary>Alliance that owns a NECTAR (§9.8: red and blue NECTAR); null for POLLEN.</summary>
    public static Alliance? NectarAlliance(this ElementKind k) => k switch
    {
        ElementKind.NectarRed => Alliance.Red,
        ElementKind.NectarBlue => Alliance.Blue,
        _ => null,
    };

    public static CellSide Other(this CellSide s) => s == CellSide.Audience ? CellSide.Rear : CellSide.Audience;

    public static string Key(this CellSide s) => s == CellSide.Audience ? "audience" : "rear";
}

/// <summary>One SCORING ELEMENT as reported by the physics side for one tick.</summary>
/// <param name="Position">Sphere centre, world frame (X towards blue, Y up, Z towards the audience), metres.</param>
/// <param name="ControlledBy">Robot currently CONTROLLING the element (Glossary §16), if any.</param>
/// <param name="LastControlledBy">Robot that last CONTROLLED or LAUNCHED it (attribution for fouls and scoring logs).</param>
/// <param name="OnField">False once the element has left the FIELD (§10.8.2) or is still in a reserve tray.</param>
public sealed record ElementState(
    int Id,
    ElementKind Kind,
    Vec3 Position,
    double Radius,
    Vec3 Velocity,
    int? ControlledBy = null,
    int? LastControlledBy = null,
    bool OnField = true);

/// <summary>One ROBOT as reported by the physics/robot side for one tick.</summary>
/// <param name="Footprint">Convex outline of the robot projected on the floor (world X/Z), including mechanisms.</param>
/// <param name="TopY">Highest point of the robot above the TILES (m).</param>
/// <param name="TouchingPerimeterWall">Robot is in contact with the FIELD perimeter wall (LEAVE §10.5.4, G304.C).</param>
/// <param name="ControlledElements">Element ids the robot CONTROLS this tick (Glossary §16).</param>
/// <param name="ContactingRobots">Robot ids in contact with this robot.</param>
/// <param name="PoweredMotion">Any actuator is powered and moving (G403/G404).</param>
public sealed record RobotState(
    int Id,
    Alliance Alliance,
    string Team,
    Polygon2 Footprint,
    double TopY,
    Vec3 Velocity,
    double YawRate,
    bool TouchingPerimeterWall,
    IReadOnlyList<int> ControlledElements,
    IReadOnlyList<int> ContactingRobots,
    bool PoweredMotion = false)
{
    public Vec2 Centre => Footprint.Centroid;
}

/// <summary>HIVE pose as reported by the physics side: hinge angle relative to the CAD pose (config/field_mechanisms.yaml).</summary>
public sealed record HiveState(Alliance Alliance, double AngleDeg, double AngularSpeedDegPerS = 0);

/// <summary>Robot <see cref="PinnerId"/> is preventing the movement of robot <see cref="PinnedId"/> by contact (G421).</summary>
public readonly record struct PinContact(int PinnerId, int PinnedId);

/// <summary>Everything the game logic needs from one simulation tick.</summary>
public sealed record WorldSnapshot(
    long Tick,
    IReadOnlyList<ElementState> Elements,
    IReadOnlyList<RobotState> Robots,
    IReadOnlyList<HiveState> Hives,
    IReadOnlyList<PinContact> Pins)
{
    public static WorldSnapshot Empty(long tick) => new(tick, [], [], [], []);

    public RobotState? Robot(int id) => Robots.FirstOrDefault(r => r.Id == id);

    public ElementState? Element(int id) => Elements.FirstOrDefault(e => e.Id == id);

    public HiveState? Hive(Alliance a) => Hives.FirstOrDefault(h => h.Alliance == a);

    /// <summary>Elements sorted by id, so results never depend on the physics engine's ordering.</summary>
    public IEnumerable<ElementState> ElementsById => Elements.OrderBy(e => e.Id);

    public IEnumerable<RobotState> RobotsById => Robots.OrderBy(r => r.Id);
}
