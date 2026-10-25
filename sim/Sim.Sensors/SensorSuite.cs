using Sim.Robots;

namespace Sim.Sensors;

/// <summary>
/// The sensor devices of one robot (robot YAML sensors, models from config/sensors.yaml). Stepped once per physics step
/// after the robot moved; the runner bridge reads the snapshots for TICK and hands the robot code's writes to
/// <see cref="Apply"/>. Webcams are not stepped here (the renderer captures their frames).
/// </summary>
public sealed class SensorSuite
{
    private readonly Dictionary<string, SensorDevice> _byName;

    public SensorSuite(RobotDefinition robot, SensorModels models, ulong seed)
    {
        var devices = new List<SensorDevice>();
        foreach (var s in robot.Sensors)
        {
            var deviceSeed = SensorDevice.Seed(seed, s.Name);
            SensorDevice? device = s.Type switch
            {
                "pinpoint" => new PinpointDevice(s, models, deviceSeed),
                "otos" => new OtosDevice(s, models, deviceSeed),
                "color" => new ColorSensorDevice(s, models, deviceSeed),
                "distance" => new DistanceSensorDevice(s, models, deviceSeed),
                "touch" => new TouchSensorDevice(s, models),
                "webcam" => null,
                _ => throw new RobotConfigException($"sensor '{s.Name}': unknown type '{s.Type}'"),
            };
            if (device != null)
            {
                devices.Add(device);
            }
        }
        Devices = devices;
        _byName = devices.ToDictionary(d => d.Name);
    }

    public IReadOnlyList<SensorDevice> Devices { get; }

    public SensorDevice? Device(string name) => _byName.GetValueOrDefault(name);

    /// <summary>Advances every device to <paramref name="time"/> (end of the step).</summary>
    public void Step(ISensorWorld world, double time, double dt)
    {
        foreach (var d in Devices)
        {
            d.Step(world, time, dt);
        }
    }

    /// <summary>Applies one write from the robot code; unknown devices are ignored (a renamed config).</summary>
    public void Apply(string name, string command, IReadOnlyList<double> args) => Device(name)?.Apply(command, args);
}
