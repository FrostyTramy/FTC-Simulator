using System.Numerics;
using Sim.Core.Randomness;
using Sim.Robots;

namespace Sim.Sensors;

/// <summary>
/// SparkFun Optical Tracking Odometry Sensor (spec 09): an optical flow sensor looking at the floor plus a gyro. The
/// firmware integrates the sensor's own motion, scaled by the linear and angular scalars, and reports the pose of the
/// robot centre given the mounting offset the robot code set. Registers hold int16 values with the SDK's scale factors
/// (SparkFunOTOS INT16_TO_METER etc., javap constants); positions are relative to the last resetTracking/setPosition.
/// Tracking stops while the sensor is tilted (warnTiltAngle) or moves faster than it can follow (warnOpticalTracking).
/// Frame as in the SDK sample SensorSparkFunOTOS ("5 inches to the left (negative X) and 10 inches forward (positive Y)"):
/// +Y = the sensor's forward (pose forward in the robot YAML), +X to its right, heading counter-clockwise seen from above;
/// the offset is the sensor's pose in that frame relative to the robot centre.
/// </summary>
public sealed class OtosDevice : SensorDevice
{
    // SparkFunOTOS register addresses (SDK constants).
    public const int RegScalarLinear = 4, RegScalarAngular = 5, RegImuCalib = 6, RegReset = 7, RegSignalProcess = 14;
    public const int RegSelfTest = 15, RegOffset = 16, RegPosition = 32;

    // SDK SparkFunOTOS scale constants.
    public const double MeterToInt16 = 3276.8, RadToInt16 = 10430.378350470453, MpsToInt16 = 6553.6;
    public const double RpsToInt16 = 938.7340515423407, MpssToInt16 = 208.83788041787972, RpssToInt16 = 10.430378350470454;

    /// <summary>Snapshot layout (the shim's SparkFunOTOS reads these indexes).</summary>
    public const int Count = 30;

    private readonly OtosModel _model;
    private readonly DeterministicRng _rng;
    private readonly Vector3 _right;
    private Vector3? _lastPoint;
    private double? _lastHeading;
    private double _now;
    private int _scalarLinear;     // raw signed byte, 0.1 % steps
    private int _scalarAngular;
    private int _imuSamples;       // remaining calibration samples
    private double _nextSample;
    private double _calRateSum;
    private int _calCount;
    private double _bias;
    private int _signalProcess = 0x0F;
    private int _selfTest;
    private double _selfTestEnd = double.NaN;
    private (double X, double Y, double H) _offset;
    private (double X, double Y, double H) _sensor;   // sensor pose in the tracking frame
    private (double X, double Y, double H) _velocity;
    private (double X, double Y, double H) _accel;
    private (double X, double Y, double H) _lastRobot;
    private (double X, double Y) _jitter;
    private bool _tilted;
    private bool _tooFast;

    public OtosDevice(SensorDef def, SensorModels models, ulong seed) : base(def)
    {
        _model = models.Otos.TryGetValue(def.Model, out var m) ? m : throw new RobotConfigException($"{def.Name}: unknown otos model '{def.Model}'");
        _rng = new DeterministicRng(seed);
        _right = Vector3.Normalize(Vector3.Cross(Def.Forward, Def.Up));
        StartImuCalibration(_model.PowerOnSamples);
        _sensor = _offset;
    }

    public override IReadOnlyDictionary<string, double> Parameters => new Dictionary<string, double> { ["productId"] = _model.ProductId };

    public override void Step(ISensorWorld world, double time, double dt)
    {
        _now = time;
        if (_imuSamples > 0)
        {
            while (_imuSamples > 0 && time >= _nextSample)
            {
                _imuSamples--;
                _nextSample += _model.ImuSampleTime;
            }
        }
        if (!double.IsNaN(_selfTestEnd) && time >= _selfTestEnd)
        {
            _selfTest = 1 << 2;   // pass
            _selfTestEnd = double.NaN;
        }

        var point = world.ToWorld(Def.Position);
        var heading = Poses.Heading(world.RobotRotation) + Math.Atan2(-Def.Forward.X, -Def.Forward.Z);
        var up = world.DirectionToWorld(Def.Up);
        _tilted = Math.Acos(Math.Clamp(up.Y / up.Length(), -1f, 1f)) > _model.MaxTilt;
        if (_lastPoint is not { } last || _lastHeading is not { } lastHeading)
        {
            _lastPoint = point;
            _lastHeading = heading;
            return;
        }
        var delta = point - last;
        _tooFast = delta.Length() / dt > _model.MaxSpeed;
        var dTrue = Poses.Wrap(heading - lastHeading);
        _lastPoint = point;
        _lastHeading = heading;

        var calibrating = _imuSamples > 0;
        if (calibrating)
        {
            _calRateSum += dTrue / dt;
            _calCount++;
            if (_imuSamples == 0)
            {
                _bias = -_calRateSum / Math.Max(1, _calCount);
            }
        }
        else if (_calCount > 0)
        {
            _bias = -_calRateSum / _calCount;
            _calCount = 0;
            _calRateSum = 0;
        }

        if (!_tilted && !_tooFast)
        {
            // Optical displacement along the sensor's axes in the floor plane, with the sensor's scale error.
            var xAxis = Horizontal(world.DirectionToWorld(_right));
            var yAxis = Horizontal(world.DirectionToWorld(Def.Forward));
            var scale = (1 + _model.LinearError) * (1 + _scalarLinear * 0.001);
            var dx = Vector3.Dot(delta, xAxis) * scale;
            var dy = Vector3.Dot(delta, yAxis) * scale;
            var dh = 0.0;
            if ((_signalProcess & 1 << 2) != 0 && !calibrating)
            {
                var gyro = dTrue * (1 + _model.AngularError) + (_bias + _model.GyroNoiseDensity / Math.Sqrt(dt) * _rng.NextGaussian()) * dt;
                dh = gyro * (1 + _scalarAngular * 0.001);
            }
            var mid = _sensor.H + dh / 2;
            _sensor = (_sensor.X + dx * Math.Cos(mid) - dy * Math.Sin(mid), _sensor.Y + dx * Math.Sin(mid) + dy * Math.Cos(mid), _sensor.H + dh);
        }

        _jitter = (_model.PositionNoise * _rng.NextGaussian(), _model.PositionNoise * _rng.NextGaussian());
        var robot = RobotPose();
        var v = ((robot.X - _lastRobot.X) / dt, (robot.Y - _lastRobot.Y) / dt, Poses.Wrap(robot.H - _lastRobot.H) / dt);
        var a = ((v.Item1 - _velocity.X) / dt, (v.Item2 - _velocity.Y) / dt, (v.Item3 - _velocity.H) / dt);
        _velocity = v;
        _accel = a;
        _lastRobot = robot;
    }

    /// <summary>Robot centre pose = sensor pose composed with the inverse of the mounting offset.</summary>
    private (double X, double Y, double H) RobotPose()
    {
        var (ox, oy, oh) = _offset;
        var h = _sensor.H - oh;
        return (_sensor.X - (ox * Math.Cos(h) - oy * Math.Sin(h)), _sensor.Y - (ox * Math.Sin(h) + oy * Math.Cos(h)), h);
    }

    private void SetRobotPose((double X, double Y, double H) robot)
    {
        var (ox, oy, oh) = _offset;
        _sensor = (robot.X + ox * Math.Cos(robot.H) - oy * Math.Sin(robot.H), robot.Y + ox * Math.Sin(robot.H) + oy * Math.Cos(robot.H), robot.H + oh);
        _lastRobot = robot;
        _velocity = _accel = (0, 0, 0);
    }

    public override double[] Snapshot()
    {
        var robot = RobotPose();
        var status = (_tilted ? 1 : 0) | (_tooFast ? 1 << 1 : 0);
        var std = _model.PositionNoise;
        return
        [
            _model.ProductId, _model.HwVersion, _model.FwVersion, _scalarLinear, _scalarAngular, _imuSamples, _signalProcess, _selfTest,
            I16(_offset.X * MeterToInt16), I16(_offset.Y * MeterToInt16), I16(Poses.Wrap(_offset.H) * RadToInt16),
            status,
            I16((robot.X + _jitter.X) * MeterToInt16), I16((robot.Y + _jitter.Y) * MeterToInt16), I16(Poses.Wrap(robot.H) * RadToInt16),
            I16(_velocity.X * MpsToInt16), I16(_velocity.Y * MpsToInt16), I16(_velocity.H * RpsToInt16),
            I16(_accel.X * MpssToInt16), I16(_accel.Y * MpssToInt16), I16(_accel.H * RpssToInt16),
            I16(std * MeterToInt16), I16(std * MeterToInt16), I16(0.001 * RadToInt16),
            I16(std * 10 * MpsToInt16), I16(std * 10 * MpsToInt16), I16(0.01 * RpsToInt16),
            I16(std * 100 * MpssToInt16), I16(std * 100 * MpssToInt16), I16(0.1 * RpssToInt16),
        ];
    }

    /// <summary>"write" (register, values): single-byte registers take one value, the pose registers three int16 values.</summary>
    public override void Apply(string command, IReadOnlyList<double> args)
    {
        if (command != "write" || args.Count < 2)
        {
            throw new ArgumentException($"{Name}: unknown otos command '{command}'");
        }
        var reg = (int)args[0];
        switch (reg)
        {
            case RegScalarLinear:
                _scalarLinear = (sbyte)(int)args[1];
                break;
            case RegScalarAngular:
                _scalarAngular = (sbyte)(int)args[1];
                break;
            case RegImuCalib:
                StartImuCalibration((int)args[1] & 0xFF);
                break;
            case RegReset:
                if (((int)args[1] & 1) != 0)
                {
                    SetRobotPose((0, 0, 0));
                }
                break;
            case RegSignalProcess:
                _signalProcess = (int)args[1] & 0xFF;
                break;
            case RegSelfTest:
                if (((int)args[1] & 1) != 0)
                {
                    _selfTest = 1 << 1;   // in progress
                    _selfTestEnd = _now + 0.01;
                }
                break;
            case RegOffset when args.Count == 4:
                var robot = RobotPose();
                _offset = (args[1] / MeterToInt16, args[2] / MeterToInt16, args[3] / RadToInt16);
                SetRobotPose(robot);
                break;
            case RegPosition when args.Count == 4:
                SetRobotPose((args[1] / MeterToInt16, args[2] / MeterToInt16, args[3] / RadToInt16));
                break;
            default:
                break;
        }
    }

    private void StartImuCalibration(int samples)
    {
        _imuSamples = samples;
        _nextSample = _now + _model.ImuSampleTime;
        _calRateSum = 0;
        _calCount = 0;
    }

    private static Vector3 Horizontal(Vector3 v) => Vector3.Normalize(new Vector3(v.X, 0, v.Z));

    private static double I16(double value) => Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);
}
