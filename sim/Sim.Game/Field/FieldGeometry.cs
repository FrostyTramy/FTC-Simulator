using Sim.Core.Config;
using Sim.Core.Numerics;
using Sim.Game.Geometry;
using static Sim.Core.Config.YamlObjects;

namespace Sim.Game.Field;

/// <summary>HIVE hinge (config/field_mechanisms.yaml joints): angle 0 = CAD pose, right-hand rule about <see cref="Axis"/>.</summary>
public sealed record HingeGeometry(
    string Id,
    Alliance Alliance,
    Vec3 Anchor,
    Vec3 Axis,
    double MinDeg,
    double MaxDeg,
    double AudienceUpDeg,
    double RearUpDeg)
{
    public double StableAngle(CellSide up) => up == CellSide.Audience ? AudienceUpDeg : RearUpDeg;

    /// <summary>The stable state nearest to an angle: which CELL is facing up.</summary>
    public CellSide NearestStable(double angleDeg) =>
        Math.Abs(angleDeg - AudienceUpDeg) <= Math.Abs(angleDeg - RearUpDeg) ? CellSide.Audience : CellSide.Rear;
}

/// <summary>CELL trigger volume in the CAD pose of its HIVE; it moves with the hinge angle.</summary>
public sealed record CellGeometry(string Id, Alliance Alliance, CellSide Side, OrientedBox CadBox, IReadOnlyList<int> AprilTagIds);

/// <summary>FLOWER scoring volume (§10.5.2): between the top ring and the middle ring.</summary>
public sealed record FlowerGeometry(string Id, string Wall, CylinderVolume Volume);

/// <summary>
/// Field geometry used by scoring and foul logic, loaded from config/field_mechanisms.yaml (derived from the
/// official field CAD, §9.1). Frame: X towards blue, Y up, Z towards the audience, metres.
/// </summary>
public sealed class FieldGeometry
{
    private readonly IReadOnlyDictionary<string, Polygon2> _zones;

    private FieldGeometry(
        double xMin, double xMax, double zMin, double zMax,
        IReadOnlyDictionary<string, Polygon2> zones,
        IReadOnlyList<FlowerGeometry> flowers,
        IReadOnlyList<CellGeometry> cells,
        IReadOnlyList<HingeGeometry> hinges,
        double damperEngageDeg,
        string source)
    {
        (WallXMin, WallXMax, WallZMin, WallZMax) = (xMin, xMax, zMin, zMax);
        _zones = zones;
        Flowers = flowers;
        Cells = cells;
        Hinges = hinges;
        DamperEngageDeg = damperEngageDeg;
        Source = source;
        Interior = Polygon2.Rect(xMin, xMax, zMin, zMax);
    }

    public double WallXMin { get; }
    public double WallXMax { get; }
    public double WallZMin { get; }
    public double WallZMax { get; }

    /// <summary>FIELD floor inside the perimeter walls.</summary>
    public Polygon2 Interior { get; }

    public IReadOnlyList<FlowerGeometry> Flowers { get; }
    public IReadOnlyList<CellGeometry> Cells { get; }
    public IReadOnlyList<HingeGeometry> Hinges { get; }

    /// <summary>TIP completes when the hinge comes within this angle of the opposite stable state (field_mechanisms tip_detection).</summary>
    public double DamperEngageDeg { get; }

    public string Source { get; }

    public IEnumerable<string> ZoneNames => _zones.Keys.OrderBy(k => k, StringComparer.Ordinal);

    public Polygon2 Zone(string name) =>
        _zones.TryGetValue(name, out var z) ? z : throw new ConfigException($"{Source}: zone '{name}' not defined");

    public Polygon2 LoadingZone(Alliance a) => Zone($"loading_zone_{a.Key()}");
    public Polygon2 Garden(Alliance a) => Zone($"garden_{a.Key()}");
    public Polygon2 AllianceArea(Alliance a) => Zone($"alliance_area_{a.Key()}");

    /// <summary>Own side of the FIELD: columns A–C red, D–F blue (G304.A, G402).</summary>
    public Polygon2 Side(Alliance a) => Zone($"side_{a.Key()}");

    public HingeGeometry Hinge(Alliance a) => Hinges.Single(h => h.Alliance == a);

    public CellGeometry Cell(Alliance a, CellSide side) => Cells.Single(c => c.Alliance == a && c.Side == side);

    /// <summary>CELL volume in the world for a hinge angle: the CAD box rotated about the hinge axis through its anchor.</summary>
    public OrientedBox CellBox(Alliance a, CellSide side, double angleDeg)
    {
        var hinge = Hinge(a);
        var rotation = Rotation3.AxisAngle(hinge.Axis, angleDeg * Math.PI / 180);
        return Cell(a, side).CadBox.RotatedAbout(hinge.Anchor, rotation);
    }

    public static FieldGeometry LoadDefault() => Load(RepoPaths.Find(Path.Combine("config", "field_mechanisms.yaml")));

    public static FieldGeometry Load(string path)
    {
        var root = YamlObjects.Load(path);

        var walls = Child(root, "walls", "walls");
        double Wall(string key) => Double(QuantityValue(Child(walls, key, "walls"), $"walls.{key}", "m"), $"walls.{key}");

        var hinges = new List<HingeGeometry>();
        var joints = List(Child(root, "joints", ""), "joints");
        for (var i = 0; i < joints.Count; i++)
        {
            var p = $"joints[{i}]";
            var j = joints[i];
            if (String(Child(j, "type", p), $"{p}.type") != "hinge")
            {
                continue;
            }
            var body = String(Child(j, "body_b", p), $"{p}.body_b");
            var range = Doubles(QuantityValue(Child(j, "angle_range", p), $"{p}.angle_range", "deg"), $"{p}.angle_range");
            var stable = Map(QuantityValue(Child(j, "stable_states", p), $"{p}.stable_states", "deg"), $"{p}.stable_states");
            hinges.Add(new HingeGeometry(
                String(Child(j, "id", p), $"{p}.id"),
                AllianceOfBody(body, p),
                ToVec3(Doubles(QuantityValue(Child(j, "anchor", p), $"{p}.anchor", "m"), $"{p}.anchor"), $"{p}.anchor"),
                ToVec3(Doubles(QuantityValue(Child(j, "axis", p), $"{p}.axis"), $"{p}.axis"), $"{p}.axis"),
                range[0],
                range[1],
                Double(stable["audience_up"], $"{p}.stable_states.audience_up"),
                Double(stable["rear_up"], $"{p}.stable_states.rear_up")));
        }

        var engage = Double(QuantityValue(Child(Child(root, "tip_detection", ""), "damper_engage", "tip_detection"),
            "tip_detection.damper_engage", "deg"), "tip_detection.damper_engage");

        var triggers = Child(root, "triggers", "");
        var cells = new List<CellGeometry>();
        var cellList = List(Child(triggers, "cells", "triggers"), "triggers.cells");
        for (var i = 0; i < cellList.Count; i++)
        {
            var p = $"triggers.cells[{i}]";
            var c = cellList[i];
            var id = String(Child(c, "id", p), $"{p}.id");
            var side = id.EndsWith("_audience", StringComparison.Ordinal) ? CellSide.Audience
                : id.EndsWith("_rear", StringComparison.Ordinal) ? CellSide.Rear
                : throw new ConfigException($"{p}: cell id must end with _audience or _rear");
            var box = OrientedBox.FromRowMajor(
                Doubles(QuantityValue(Child(c, "obb_transform", p), $"{p}.obb_transform", "m"), $"{p}.obb_transform"),
                Doubles(QuantityValue(Child(c, "obb_size", p), $"{p}.obb_size", "m"), $"{p}.obb_size"));
            var tags = Doubles(QuantityValue(Child(c, "apriltag_ids", p), $"{p}.apriltag_ids"), $"{p}.apriltag_ids")
                .Select(d => (int)d).ToList();
            cells.Add(new CellGeometry(id, GameEnumExtensions.ParseAlliance(String(Child(c, "alliance", p), $"{p}.alliance")), side, box, tags));
        }

        var fv = Child(triggers, "flower_volume", "triggers");
        double Fv(string key) => Double(QuantityValue(Child(fv, key, "triggers.flower_volume"), $"triggers.flower_volume.{key}", "m"), key);
        var radius = Fv("radius");
        var yMin = Fv("y_min");
        var yMax = Fv("y_max");
        var flowers = new List<FlowerGeometry>();
        var flowerList = List(Child(triggers, "flowers", "triggers"), "triggers.flowers");
        for (var i = 0; i < flowerList.Count; i++)
        {
            var p = $"triggers.flowers[{i}]";
            var f = flowerList[i];
            var axis = Doubles(QuantityValue(Child(f, "axis_xz", p), $"{p}.axis_xz", "m"), $"{p}.axis_xz");
            flowers.Add(new FlowerGeometry(
                String(Child(f, "id", p), $"{p}.id"),
                String(Child(f, "wall", p), $"{p}.wall"),
                new CylinderVolume(new Vec2(axis[0], axis[1]), radius, yMin, yMax)));
        }

        var zones = new Dictionary<string, Polygon2>(StringComparer.Ordinal);
        foreach (var (name, zone) in Map(Child(triggers, "zones", "triggers"), "triggers.zones"))
        {
            var p = $"triggers.zones.{name}";
            if (String(Child(zone, "unit", p), $"{p}.unit") != "m")
            {
                throw new ConfigException($"{p}: expected unit 'm'");
            }
            _ = String(Child(zone, "source", p), $"{p}.source");
            var x = Doubles(Child(zone, "x", p), $"{p}.x");
            var z = Doubles(Child(zone, "z", p), $"{p}.z");
            zones[name] = Polygon2.Rect(x[0], x[1], z[0], z[1]);
        }

        return new FieldGeometry(Wall("x_min"), Wall("x_max"), Wall("z_min"), Wall("z_max"),
            zones, flowers, cells, hinges, engage, path);
    }

    private static Alliance AllianceOfBody(string body, string path) =>
        body.EndsWith("_red", StringComparison.Ordinal) ? Alliance.Red
        : body.EndsWith("_blue", StringComparison.Ordinal) ? Alliance.Blue
        : throw new ConfigException($"{path}: cannot tell the alliance of body '{body}'");

    private static Vec3 ToVec3(double[] v, string path) =>
        v.Length == 3 ? new Vec3(v[0], v[1], v[2]) : throw new ConfigException($"{path}: expected 3 values");
}
