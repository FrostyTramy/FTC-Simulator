using System.Numerics;
using Sim.Core.Randomness;
using Sim.Robots;

namespace Sim.Sensors;

/// <summary>Cone raycasts shared by the range-based sensors.</summary>
internal static class Cone
{
    /// <summary>
    /// Nearest hit of a centre ray and <paramref name="ring"/> rays on a cone of <paramref name="halfAngle"/> around the
    /// sensor's forward direction (robot frame mounting), within <paramref name="maxDistance"/>.
    /// </summary>
    public static SurfaceHit? Nearest(ISensorWorld world, SensorDef def, double halfAngle, float maxDistance, int ring = 6)
    {
        var origin = world.ToWorld(def.Position);
        var forward = Vector3.Normalize(world.DirectionToWorld(def.Forward));
        var up = Vector3.Normalize(world.DirectionToWorld(def.Up));
        var side = Vector3.Cross(forward, up);
        SurfaceHit? best = world.Raycast(origin, forward, maxDistance);
        var (sin, cos) = ((float)Math.Sin(halfAngle), (float)Math.Cos(halfAngle));
        for (var i = 0; i < ring; i++)
        {
            var a = 2 * Math.PI * i / ring;
            var dir = forward * cos + (up * (float)Math.Cos(a) + side * (float)Math.Sin(a)) * sin;
            if (world.Raycast(origin, dir, maxDistance) is { } hit && (best is null || hit.Distance * cos < best.Value.Distance))
            {
                best = hit with { Distance = hit.Distance * cos };   // range along the axis
            }
        }
        return best;
    }
}

/// <summary>
/// REV Color Sensor V3 (Broadcom APDS-9151, spec 09): colour channels from the diffuse colour and distance of the
/// nearest surface in its cone, lit by the sensor's LED (inverse square fall-off), plus ambient light; the SDK's
/// proximity-based distance saturates at max_distance. All scale values are ASSUMED (config/sensors.yaml color).
/// Snapshot: red, green, blue, alpha (raw counts), distance (cm), raw proximity (0..2047), device id, LED on.
/// </summary>
public sealed class ColorSensorDevice : SensorDevice
{
    public const int Count = 8;
    private const double ReferenceDistance = 0.01;   // white_target_counts are defined at 1 cm
    private const double SourceSize = 0.005;         // softens the 1/d² law at contact (ASSUMED)
    private readonly ColorModel _model;
    private readonly DeterministicRng _rng;
    private double[] _rgba = new double[4];
    private double _distanceCm;
    private double _rawOptical;
    private bool _ledOn = true;

    public ColorSensorDevice(SensorDef def, SensorModels models, ulong seed) : base(def)
    {
        _model = models.Colors.TryGetValue(def.Model, out var m) ? m : throw new RobotConfigException($"{def.Name}: unknown color model '{def.Model}'");
        _rng = new DeterministicRng(seed);
        _distanceCm = _model.MaxDistance * 100;
    }

    public override void Step(ISensorWorld world, double time, double dt)
    {
        var hit = Cone.Nearest(world, Def, _model.ConeHalfAngle, 0.25f);
        var light = new double[3];
        if (hit is { } h)
        {
            var falloff = Math.Pow((ReferenceDistance + SourceSize) / (h.Distance + SourceSize), 2) * (_ledOn ? 1 : 0.05);
            light[0] = _model.WhiteTargetCounts * h.Color.X * falloff;
            light[1] = _model.WhiteTargetCounts * h.Color.Y * falloff;
            light[2] = _model.WhiteTargetCounts * h.Color.Z * falloff;
        }
        for (var c = 0; c < 3; c++)
        {
            var counts = (light[c] + _model.AmbientCounts) * (1 + _model.Noise * _rng.NextGaussian());
            _rgba[c] = Math.Round(Math.Clamp(counts, 0, _model.FullScaleCounts));
        }
        _rgba[3] = Math.Round(Math.Clamp((_rgba[0] + _rgba[1] + _rgba[2]) / 3, 0, _model.FullScaleCounts));
        var d = hit is { } near && near.Distance < _model.MaxDistance ? near.Distance : _model.MaxDistance;
        _distanceCm = Math.Min(_model.MaxDistance, d * (1 + _model.Noise * _rng.NextGaussian())) * 100;
        _rawOptical = Math.Round(2047 * Math.Pow(Math.Clamp(1 - d / _model.MaxDistance, 0, 1), 2));
    }

    public override double[] Snapshot() => [_rgba[0], _rgba[1], _rgba[2], _rgba[3], _distanceCm, _rawOptical, _model.DeviceId, _ledOn ? 1 : 0];

    /// <summary>"led" (on 0/1): BroadcomColorSensorImpl.enableLed.</summary>
    public override void Apply(string command, IReadOnlyList<double> args)
    {
        if (command == "led" && args.Count == 1)
        {
            _ledOn = args[0] != 0;
        }
    }
}

/// <summary>
/// REV 2m Distance Sensor (ST VL53L0X time of flight, spec 09): nearest surface in its 25° cone, a new range every
/// timing budget, relative noise, and the out-of-range code beyond 2 m. Snapshot: range (mm), model id, timeout flag.
/// </summary>
public sealed class DistanceSensorDevice : SensorDevice
{
    public const int Count = 3;
    private readonly DistanceModel _model;
    private readonly DeterministicRng _rng;
    private double _rangeMm;
    private double _nextUpdate;

    public DistanceSensorDevice(SensorDef def, SensorModels models, ulong seed) : base(def)
    {
        _model = models.Distances.TryGetValue(def.Model, out var m) ? m : throw new RobotConfigException($"{def.Name}: unknown distance model '{def.Model}'");
        _rng = new DeterministicRng(seed);
        _rangeMm = _model.OutOfRangeMm;
    }

    public override void Step(ISensorWorld world, double time, double dt)
    {
        if (time < _nextUpdate)
        {
            return;
        }
        _nextUpdate = time + _model.UpdatePeriod;
        var hit = Cone.Nearest(world, Def, _model.ConeHalfAngle, (float)_model.MaxRange, 8);
        _rangeMm = hit is { } h
            ? Math.Round(Math.Max(_model.MinRange, h.Distance * (1 + _model.Noise * _rng.NextGaussian())) * 1000)
            : _model.OutOfRangeMm;
    }

    public override double[] Snapshot() => [_rangeMm, _model.ModelId, 0];

    public override void Apply(string command, IReadOnlyList<double> args)
    {
    }
}

/// <summary>REV Touch Sensor (spec 09): pressed while something is within its button travel. Snapshot: pressed 0/1.</summary>
public sealed class TouchSensorDevice : SensorDevice
{
    public const int Count = 1;
    private readonly TouchModel _model;
    private bool _pressed;

    public TouchSensorDevice(SensorDef def, SensorModels models) : base(def)
    {
        _model = models.Touches.TryGetValue(def.Model, out var m) ? m : throw new RobotConfigException($"{def.Name}: unknown touch model '{def.Model}'");
    }

    public override void Step(ISensorWorld world, double time, double dt) =>
        _pressed = world.Raycast(world.ToWorld(Def.Position), Vector3.Normalize(world.DirectionToWorld(Def.Forward)), (float)_model.Travel) is not null;

    public override double[] Snapshot() => [_pressed ? 1 : 0];

    public override void Apply(string command, IReadOnlyList<double> args)
    {
    }
}
