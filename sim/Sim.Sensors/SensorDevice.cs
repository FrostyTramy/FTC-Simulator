using Sim.Robots;

namespace Sim.Sensors;

/// <summary>
/// A simulated sensor (spec 09): steps with the physics from the ground truth, exposes what the robot code can read
/// (one value vector per device, docs/ipc_protocol.md TICK) and applies the robot code's writes (STATE sensor commands).
/// The device's own firmware behaviour lives here, like the hub firmware in Sim.Hardware; the SDK shim mirrors the
/// driver on top of these values.
/// </summary>
public abstract class SensorDevice(SensorDef def)
{
    public SensorDef Def { get; } = def;

    public string Name => Def.Name;

    /// <summary>Static values the shim needs to build the device (protocol CONFIGURE parameters).</summary>
    public virtual IReadOnlyDictionary<string, double> Parameters { get; } = new Dictionary<string, double>();

    /// <summary>Advances the device to <paramref name="time"/> (seconds, end of this step of length <paramref name="dt"/>).</summary>
    public abstract void Step(ISensorWorld world, double time, double dt);

    /// <summary>Everything the robot code can read from the device now, in the order the shim's driver expects.</summary>
    public abstract double[] Snapshot();

    /// <summary>A write from the robot code; it takes effect in the next step, like a real bus transaction.</summary>
    public abstract void Apply(string command, IReadOnlyList<double> args);

    /// <summary>Round to what a float32 register holds.</summary>
    protected static double F32(double value) => (float)value;

    /// <summary>Seed for a device's noise: the match seed mixed with the device name (FNV-1a), the same on every machine.</summary>
    public static ulong Seed(ulong matchSeed, string name)
    {
        var h = 14695981039346656037UL ^ matchSeed;
        foreach (var c in name)
        {
            h = (h ^ c) * 1099511628211UL;
        }
        return h;
    }
}
