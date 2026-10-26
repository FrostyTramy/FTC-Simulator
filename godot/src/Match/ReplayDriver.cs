using Sim.Match.Replay;
using Sim.Robot;
using Sim.Runner;

namespace Sim.Match;

/// <summary>
/// Re-simulates a station from its replay: the recorded runner STATE of every tick goes through a RunnerBridge exactly
/// as <see cref="RobotProgram"/> applied it (no JVM needed), and the SDK's Stop Robot runs on ticks without one.
/// </summary>
public sealed class ReplayDriver : IRobotDriver
{
    private readonly RunnerBridge _bridge;

    public ReplayDriver(RobotBody robot)
    {
        _bridge = new RunnerBridge(robot.Powertrain.Hardware, robot.HardwareConfig.HubController, robot.Sensors);
    }

    /// <summary>This tick's recorded input (set by the session before the robots step).</summary>
    public StationTick? Next { get; set; }

    public void BeginSession() => _bridge.BeginSession();

    public void BeforeStep(RobotBody robot, long tick)
    {
    }

    public void AfterStep(RobotBody robot, long tick)
    {
        var now = RobotProgram.EndNanos(tick);
        if (Next is { Kind: DriveKind.Runner, Result: { } result })
        {
            _bridge.Apply(result, now);
        }
        else
        {
            _bridge.StopRobot(now);
        }
    }
}

/// <summary>Gamepads replayed from a recording (a direct-drive or AI station).</summary>
public sealed class RecordedGamepads : IGamepadSource
{
    public GamepadState Gamepad1 { get; set; } = GamepadState.Neutral;
    public GamepadState Gamepad2 { get; set; } = GamepadState.Neutral;
}
