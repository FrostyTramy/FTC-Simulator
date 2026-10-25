using System.Numerics;
using Sim.Core.Randomness;
using Sim.Robots;

namespace Sim.Sensors;

/// <summary>
/// goBILDA Pinpoint odometry computer (spec 09): two spring-loaded dead-wheel pods and an internal gyro, fused by the
/// device firmware into a pose. Register map, status bits, units (mm, radians, ticks per mm) and the meaning of the
/// offsets and directions follow the goBILDA driver (generated/sdk/vendor/GoBildaPinpointDriver_v1.java, "GBDRV"):
/// <list type="bullet">
/// <item>X pod measures forward travel, Y pod measures travel to the left; offsets: X pod sideways from the tracking point
/// (left positive), Y pod forwards (forward positive), mm.</item>
/// <item>Encoder registers hold the raw pod counts; the direction setting only changes how the firmware uses them
/// (ASSUMED: GBDRV calls them "raw").</item>
/// <item>Position and heading are relative to the last reset or setPosition; heading is unnormalized (GBDRV
/// getHeading(UnnormalizedAngleUnit)); velocities are in the same frame (ASSUMED field frame).</item>
/// <item>A calibration (power-on, recalibrateIMU, resetPosAndIMU) lasts calibration_time; the gyro zero is the mean rate
/// seen meanwhile, so moving the robot during calibration leaves a heading drift, as on the real device. Tracking
/// pauses while calibrating (ASSUMED).</item>
/// </list>
/// The pods are physical: their counts come from the motion of their contact points along their axes (robot YAML), so a
/// wrong offset, direction or pod type in team code gives wrong poses exactly like on the real robot.
/// </summary>
public sealed class PinpointDevice : SensorDevice
{
    // GBDRV Register enum values.
    public const int RegDeviceControl = 4, RegXPosition = 8, RegYPosition = 9, RegHOrientation = 10;
    public const int RegMmPerTick = 14, RegXPodOffset = 15, RegYPodOffset = 16, RegYawScalar = 17;

    // GBDRV DeviceStatus bits.
    public const int StatusReady = 1, StatusCalibrating = 1 << 1, StatusNoXPod = 1 << 2, StatusNoYPod = 1 << 3;

    /// <summary>Snapshot layout (the shim's GoBildaPinpointDriver reads these indexes).</summary>
    public const int Count = 22;

    private readonly PinpointModel _model;
    private readonly Pod? _xPod;
    private readonly Pod? _yPod;
    private readonly double _liftTilt;
    private readonly double _velocityFilter;
    private readonly DeterministicRng _rng;
    private double _bias;
    private double _residualBias;
    private double _calibrationEnd;
    private double _calibrationRateSum;
    private int _calibrationSamples;
    private double? _lastHeading;
    private int _lastRawX;
    private int _lastRawY;
    private int _dirX = 1;
    private int _dirY = 1;
    private double _x;
    private double _y;
    private double _h;
    private double _vx;
    private double _vy;
    private double _vh;
    private double _ticksPerMm;
    private double _xOffset;
    private double _yOffset;
    private double _yawScalar;
    private double _pitch;
    private double _roll;

    private sealed class Pod(Vector3 position, Vector3 axis, double countsPerMm)
    {
        public Vector3 Position { get; } = position;
        public Vector3 Axis { get; } = Vector3.Normalize(axis);
        public double CountsPerMm { get; } = countsPerMm;
        public Vector3? LastWorld { get; set; }
        public Vector3 LastAxis { get; set; }
        public double Mm { get; set; }
        public int Counts => (int)Math.Round(Mm * CountsPerMm);
    }

    public PinpointDevice(SensorDef def, SensorModels models, ulong seed) : base(def)
    {
        _model = models.Pinpoints.TryGetValue(def.Model, out var m) ? m : throw new RobotConfigException($"{def.Name}: unknown pinpoint model '{def.Model}'");
        _liftTilt = models.PodLiftTilt;
        _velocityFilter = _model.VelocityFilter;
        _xPod = ReadPod(def, "x_pod", models);
        _yPod = ReadPod(def, "y_pod", models);
        _rng = new DeterministicRng(seed);
        _ticksPerMm = F32(_model.PowerOnTicksPerMm);
        _yawScalar = F32(_model.PowerOnYawScalar);
        StartCalibration(0);
    }

    /// <summary>Raw pod counts (what the encoder registers report).</summary>
    public (int X, int Y) RawCounts => (_xPod?.Counts ?? 0, _yPod?.Counts ?? 0);

    public bool Calibrating => !double.IsNaN(_calibrationEnd);

    public override void Step(ISensorWorld world, double time, double dt)
    {
        _now = time;
        var lifted = Poses.Tilt(world.RobotRotation) > _liftTilt;
        foreach (var pod in new[] { _xPod, _yPod })
        {
            if (pod is null)
            {
                continue;
            }
            var p = world.ToWorld(pod.Position);
            var a = world.DirectionToWorld(pod.Axis);
            if (pod.LastWorld is { } last && !lifted)
            {
                pod.Mm += Vector3.Dot(p - last, Vector3.Normalize(a + pod.LastAxis)) * 1000;
            }
            pod.LastWorld = p;
            pod.LastAxis = a;
        }
        ReadAttitude(world.RobotRotation);

        var heading = Poses.Heading(world.RobotRotation);
        var dTrue = _lastHeading is { } lh ? Poses.Wrap(heading - lh) : 0;
        _lastHeading = heading;
        var dGyro = dTrue + (_bias + _model.GyroNoiseDensity / Math.Sqrt(dt) * _rng.NextGaussian()) * dt;

        var (rawX, rawY) = RawCounts;
        var dEx = rawX - _lastRawX;
        var dEy = rawY - _lastRawY;
        _lastRawX = rawX;
        _lastRawY = rawY;

        if (Calibrating)
        {
            _calibrationRateSum += dTrue / dt;
            _calibrationSamples++;
            _vx = _vy = _vh = 0;
            if (time >= _calibrationEnd)
            {
                _bias = _residualBias - (_calibrationSamples > 0 ? _calibrationRateSum / _calibrationSamples : 0);
                _calibrationEnd = double.NaN;
            }
            return;
        }

        // Firmware odometry: pod travel in mm, corrected for the rotation seen at the pods, turned into the pose frame.
        var dx = _ticksPerMm != 0 ? _dirX * dEx / _ticksPerMm : 0;
        var dy = _ticksPerMm != 0 ? _dirY * dEy / _ticksPerMm : 0;
        var dh = dGyro * _yawScalar;
        var forward = dx + dh * _xOffset;
        var left = dy - dh * _yOffset;
        var mid = _h + dh / 2;
        var gx = forward * Math.Cos(mid) - left * Math.Sin(mid);
        var gy = forward * Math.Sin(mid) + left * Math.Cos(mid);
        _x += gx;
        _y += gy;
        _h += dh;
        var k = dt / (_velocityFilter + dt);
        _vx += k * (gx / dt - _vx);
        _vy += k * (gy / dt - _vy);
        _vh += k * (dh / dt - _vh);
    }

    public override double[] Snapshot()
    {
        var status = Calibrating ? StatusCalibrating : StatusReady;
        status |= _xPod is null ? StatusNoXPod : 0;
        status |= _yPod is null ? StatusNoYPod : 0;
        var (rawX, rawY) = RawCounts;
        var q = Orientation();
        return
        [
            status, _model.LoopTimeUs, rawX, rawY,
            F32(_x), F32(_y), F32(_h), F32(_vx), F32(_vy), F32(_vh),
            _model.DeviceId, _model.DeviceVersion, F32(_yawScalar), F32(_ticksPerMm), F32(_xOffset), F32(_yOffset),
            F32(q.W), F32(q.X), F32(q.Y), F32(q.Z), F32(_pitch), F32(_roll),
        ];
    }

    /// <summary>"write" (register, value): the register writes of the GBDRV driver methods.</summary>
    public override void Apply(string command, IReadOnlyList<double> args)
    {
        if (command != "write" || args.Count != 2)
        {
            throw new ArgumentException($"{Name}: unknown pinpoint command '{command}'");
        }
        var value = F32(args[1]);
        switch ((int)args[0])
        {
            case RegDeviceControl:
                var bits = (int)args[1];
                if ((bits & 1 << 1) != 0)
                {
                    _x = _y = _h = 0;
                }
                if ((bits & (1 << 0 | 1 << 1)) != 0)
                {
                    StartCalibration(double.NaN);
                }
                _dirY = (bits & 1 << 2) != 0 ? -1 : (bits & 1 << 3) != 0 ? 1 : _dirY;
                _dirX = (bits & 1 << 4) != 0 ? -1 : (bits & 1 << 5) != 0 ? 1 : _dirX;
                break;
            case RegXPosition:
                _x = value;
                break;
            case RegYPosition:
                _y = value;
                break;
            case RegHOrientation:
                _h = value;
                break;
            case RegMmPerTick:
                _ticksPerMm = value;
                break;
            case RegXPodOffset:
                _xOffset = value;
                break;
            case RegYPodOffset:
                _yOffset = value;
                break;
            case RegYawScalar:
                _yawScalar = value;
                break;
            default:
                break;   // read-only or unknown registers: the device ignores the write
        }
    }

    private double _now;

    /// <summary>Starts a gyro calibration of calibration_time; NaN = from the current step.</summary>
    private void StartCalibration(double start)
    {
        _calibrationEnd = (double.IsNaN(start) ? _now : start) + _model.CalibrationTime;
        _calibrationRateSum = 0;
        _calibrationSamples = 0;
        _residualBias = _model.GyroBiasResidual * _rng.NextGaussian();
    }

    private void ReadAttitude(Quaternion rotation)
    {
        var f = Vector3.Transform(Vector3.Normalize(Def.Forward), rotation);
        var u = Vector3.Transform(Vector3.Normalize(Def.Up), rotation);
        var r = Vector3.Cross(f, u);
        _pitch = Math.Asin(Math.Clamp(f.Y, -1f, 1f));
        var cosPitch = Math.Cos(_pitch);
        _roll = cosPitch > 1e-6 ? -Math.Asin(Math.Clamp(r.Y / cosPitch, -1, 1)) : 0;
    }

    /// <summary>Device orientation estimate in the FTC convention (Z up): heading from the firmware, pitch/roll from gravity.</summary>
    private Quaternion Orientation()
    {
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)_h);
        var pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)_pitch);
        var roll = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)_roll);
        return yaw * pitch * roll;
    }

    private static Pod? ReadPod(SensorDef def, string key, SensorModels models)
    {
        if (!def.Params.ContainsKey(key))
        {
            return null;   // not connected: the device reports the pod as missing
        }
        var pod = RobotDefinition.SensorParam(def, key);
        var model = pod.TryGetValue("model", out var m) ? m.ToString()! : throw new RobotConfigException($"{def.Name}.{key}: missing model");
        if (!models.PodTicksPerMm.TryGetValue(model, out var countsPerMm))
        {
            throw new RobotConfigException($"{def.Name}.{key}: unknown pod model '{model}'");
        }
        return new Pod(RobotDefinition.VectorValue(pod["position"]), RobotDefinition.VectorValue(pod["axis"]), countsPerMm);
    }
}
