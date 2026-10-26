using Sim.Core.Config;
using Sim.Hardware;
using Sim.Robots;
using Sim.Runner;

// Runs the calibration OpModes (tools/calibration/TeamCode, docs/calibration.md) unchanged on the simulated robot: the
// flat-floor PlanarRobotSim (the reference the Godot robot is checked against, D-021) in lockstep with the runner JVM,
// exactly as the C# runner tests do. The OpModes write the same CSV files as on a real Robot Controller; they are copied
// to OUT/<robot>/ for tools/calibration/fit.py.
//
// Usage: dotnet run --project sim/Sim.Bench -- [--robot NAME]... [--out DIR]
const int TickRate = 240;
var robots = new List<string>();
var outDir = RepoPaths.Find("reports") is var r && Directory.Exists(r) ? Path.Combine(r, "calibration", "sim") : "calibration-sim";
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--robot" && i + 1 < args.Length)
    {
        robots.Add(args[++i]);
    }
    else if (args[i] == "--out" && i + 1 < args.Length)
    {
        outDir = Path.GetFullPath(args[++i]);
    }
}
if (robots.Count == 0)
{
    robots.Add("gobilda_starter");
}

var sources = RepoPaths.Find(Path.Combine("tools", "calibration", "TeamCode", "src", "main", "java"));
var buildDir = Path.Combine(Path.GetDirectoryName(RepoPaths.RulesFile())!, "..", "generated", "build", "calibration");
var build = TeamCodeBuilder.Build(sources, Path.GetFullPath(buildDir));
if (!build.Success)
{
    Console.Error.WriteLine("calibration OpModes do not build:\n" + build.Output);
    return 1;
}

// (OpMode, test selected with the dpad during INIT, seconds to run)
var tests = new (string OpMode, GamepadButtons Select, double Seconds)[]
{
    ("Cal: Drive", GamepadButtons.DpadUp, 3.5),
    ("Cal: Drive", GamepadButtons.DpadRight, 3.5),
    ("Cal: Drive", GamepadButtons.DpadDown, 3.5),
    ("Cal: Launcher", GamepadButtons.None, 6.5),
    ("Cal: Loop time", GamepadButtons.None, 9.5),
};
var hardware = HardwareConfig.Load();
foreach (var name in robots)
{
    var robot = RobotDefinition.Load(RobotDefinition.FindConfig(name));
    var target = Path.Combine(outDir, name);
    Directory.CreateDirectory(target);
    foreach (var old in Directory.GetFiles(target, "cal_*.csv"))
    {
        File.Delete(old);
    }
    var measured = new List<string> { $"# Ground truth of the simulated tests (what a team measures with a tape measure, docs/calibration.md)", $"robot: {name}" };
    foreach (var (opMode, select, seconds) in tests)
    {
        var work = Path.Combine(Path.GetTempPath(), "ftcsim-bench-" + Guid.NewGuid().ToString("N")[..8]);
        var sim = new PlanarRobotSim(robot, hardware, DriveContact.LoadDefault(), TickRate);
        var bridge = new RunnerBridge(sim.Hardware, hardware.HubController);
        using (var session = RunnerSession.Start(RunnerLaunch.ForTeamCode(build.JarPath!, work)))
        {
            var info = session.ListOpModes().Single(o => o.Name == opMode);
            bridge.BeginSession();
            session.Configure(HardwareCosts.Load(), sim.Hardware.HubCount, bridge.Devices());
            session.Init(info.ClassName);
            long tick = 0;
            TickResult Step(GamepadState pad)
            {
                var (orientation, rate) = bridge.Imu(Quat.FromYaw(sim.Yaw), new Vec3d(0, 0, sim.YawRate));
                var endNanos = (tick + 1) * 1_000_000_000L / TickRate;
                var result = session.Tick(new TickInput(tick, endNanos, pad, GamepadState.Neutral, sim.Battery.BusVolts, orientation, rate, bridge.Inputs()));
                sim.Step();
                bridge.Apply(result, endNanos);
                tick++;
                return result;
            }
            for (var i = 0; i < TickRate / 2; i++)
            {
                Step(new GamepadState(Buttons: select, Id: 1));   // INIT: the dpad picks the test
            }
            session.Start();
            TickResult? last = null;
            for (var i = 0; i < (int)(seconds * TickRate); i++)
            {
                last = Step(GamepadState.Neutral with { Id = 1 });
            }
            session.Stop();
            Step(GamepadState.Neutral);
            if (opMode == "Cal: Drive")
            {
                var test = select == GamepadButtons.DpadUp ? "forward" : select == GamepadButtons.DpadRight ? "strafe" : "turn";
                var p = sim.Position;
                measured.Add(test == "turn"
                    ? $"turn_deg: {Math.Abs(sim.Yaw) * 180 / Math.PI:0.0}"
                    : $"{test}_distance_m: {Math.Sqrt(p.X * p.X + p.Z * p.Z):0.000}");
            }
            if (last?.Error is { } error)
            {
                Console.Error.WriteLine($"{name} {opMode}: {error}");
            }
            foreach (var line in last?.Telemetry ?? [])
            {
                Console.WriteLine($"  {name} {opMode}: {line.Replace('\n', ' ')}");
            }
        }
        var csvDir = Path.Combine(work, "calibration");
        foreach (var csv in Directory.Exists(csvDir) ? Directory.GetFiles(csvDir, "cal_*.csv") : [])
        {
            var dest = Path.Combine(target, Path.GetFileName(csv));
            for (var n = 2; File.Exists(dest); n++)
            {
                dest = Path.Combine(target, Path.GetFileNameWithoutExtension(csv) + $"_{n}.csv");
            }
            File.Copy(csv, dest);
        }
        Directory.Delete(work, recursive: true);
    }
    File.WriteAllLines(Path.Combine(target, "measurements.yaml"), measured);
    Console.WriteLine($"{name}: {Directory.GetFiles(target, "cal_*.csv").Length} calibration logs in {target}");
}
return 0;
