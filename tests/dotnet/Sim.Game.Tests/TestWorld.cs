using Sim.Core.Numerics;
using Sim.Game.Field;
using Sim.Game.Geometry;

namespace GameTests;

/// <summary>Shared fixtures: the real rules and field geometry (config/*.yaml) and snapshot builders.
/// Frame: X towards blue, Y up, Z towards the audience (docs/game_rules.md §1).</summary>
public static class TestWorld
{
    public static readonly GameRules Rules = GameRules.Load();
    public static readonly FieldGeometry Field = FieldGeometry.LoadDefault();

    public const double PollenRadius = 0.03556;
    public const double NectarRadius = 0.04572;

    /// <summary>Half of the 18 in starting cube (R102).</summary>
    public const double HalfCube = 0.2286;

    public static ElementState Pollen(int id, double x, double y, double z, int? controlledBy = null) =>
        new(id, ElementKind.Pollen, new Vec3(x, y, z), PollenRadius, default, controlledBy, controlledBy);

    public static ElementState Nectar(int id, Alliance colour, double x, double y, double z, int? controlledBy = null) =>
        new(id, colour == Alliance.Red ? ElementKind.NectarRed : ElementKind.NectarBlue, new Vec3(x, y, z), NectarRadius,
            default, controlledBy, controlledBy);

    public static RobotState Robot(int id, Alliance alliance, double x, double z, bool touchingWall = false,
        double half = HalfCube, double topY = 0.4, IReadOnlyList<int>? controlled = null,
        IReadOnlyList<int>? contacting = null, bool powered = false, double speed = 0) =>
        new(id, alliance, $"team{id}", Polygon2.OrientedRect(new Vec2(x, z), half, half, 0), topY,
            new Vec3(speed, 0, 0), 0, touchingWall, controlled ?? [], contacting ?? [], powered);

    public static WorldSnapshot Snap(long tick, IEnumerable<ElementState>? elements = null, IEnumerable<RobotState>? robots = null,
        IEnumerable<HiveState>? hives = null, IEnumerable<PinContact>? pins = null) =>
        new(tick, (elements ?? []).ToList(), (robots ?? []).ToList(),
            (hives ?? [new HiveState(Alliance.Red, 0), new HiveState(Alliance.Blue, 0)]).ToList(), (pins ?? []).ToList());

    /// <summary>Centre of a CELL volume at the CAD pose (hive angle 0).</summary>
    public static Vec3 CellCentre(Alliance a, CellSide side) => Field.CellBox(a, side, 0).Centre;

    public static FlowerGeometry Flower(string id) => Field.Flowers.Single(f => f.Id == id);
}
