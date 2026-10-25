using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Sim.Runner;

/// <summary>The runner failed: it did not start, broke the protocol, reported an error, or stopped answering.</summary>
public sealed class RunnerException(string message) : Exception(message);

/// <summary>What to start: the Java 17 executable, the shim + runner jars and the team's compiled code.</summary>
public sealed record RunnerLaunch(string JavaExe, string ShimJar, string RunnerJar, string TeamCodeJar, string WorkDir)
{
    /// <summary>JVM start-up plus connection (generous: antivirus scanners can hold a new process for a while).</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Longest wait for any reply. Longer than the runner's own 5 s stuck-code detection.</summary>
    public TimeSpan ReplyTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Development layout: Java 17 from the machine, jars from the Gradle builds under java/.</summary>
    public static RunnerLaunch ForTeamCode(string teamCodeJar, string? workDir = null)
    {
        var (shim, runner) = JavaLocator.FindRunnerJars();
        return new RunnerLaunch(JavaLocator.FindJava17(), shim, runner, teamCodeJar,
            workDir ?? Path.Combine(Path.GetTempPath(), "ftc-sim-runner"));
    }
}

/// <summary>
/// One runner JVM and its socket (spec 01, docs/ipc_protocol.md). The simulator listens on a loopback port, the
/// runner connects and proves it was started by us with a one-time token. Calls are synchronous request/reply.
/// The JVM's own console output (team code System.out) is kept in memory for the UI, never written to logs.
/// </summary>
public sealed class RunnerSession : IDisposable
{
    public const int ProtocolVersion = 1;
    private const int ConsoleLimit = 1000;

    private readonly Process _process;
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Queue<string> _console;
    private bool _disposed;

    private RunnerSession(Process process, TcpClient client, string sdkVersion, Queue<string> console)
    {
        _process = process;
        _client = client;
        _stream = client.GetStream();
        _console = console;
        SdkVersion = sdkVersion;
    }

    public string SdkVersion { get; }

    /// <summary>The JVM has exited (crash, System.exit blocked or not, killed).</summary>
    public bool HasExited => _process.HasExited;

    /// <summary>Recent console lines of the runner JVM (team code prints, JVM errors).</summary>
    public IReadOnlyList<string> ConsoleLines
    {
        get
        {
            lock (_console)
            {
                return _console.ToList();
            }
        }
    }

    public static RunnerSession Start(RunnerLaunch launch)
    {
        foreach (var file in new[] { launch.JavaExe, launch.ShimJar, launch.RunnerJar, launch.TeamCodeJar })
        {
            if (!File.Exists(file))
            {
                throw new RunnerException($"missing file: {file}");
            }
        }
        Directory.CreateDirectory(launch.WorkDir);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        Process? process = null;
        var console = new Queue<string>();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var info = new ProcessStartInfo(launch.JavaExe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = launch.WorkDir,
            };
            foreach (var arg in new[]
            {
                // Heap limit like a Robot Controller app (spec 07 sandbox "memory limit"; 512 MB ASSUMED).
                "-Xmx512m", "-Xss4m", "-Dfile.encoding=UTF-8", "-cp", launch.ShimJar + Path.PathSeparator + launch.RunnerJar,
                "org.firstinspires.ftc.sim.runner.RunnerMain",
                "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--token", token,
                "--teamcode", launch.TeamCodeJar, "--workdir", launch.WorkDir,
            })
            {
                info.ArgumentList.Add(arg);
            }
            process = Process.Start(info) ?? throw new RunnerException("could not start java");
            process.OutputDataReceived += (_, e) => Remember(console, e.Data);
            process.ErrorDataReceived += (_, e) => Remember(console, e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var accept = listener.AcceptTcpClientAsync();
            if (!accept.Wait(launch.ConnectTimeout) || process.HasExited)
            {
                throw new RunnerException($"the runner did not connect within {launch.ConnectTimeout.TotalSeconds:0} s. {Tail(console)}");
            }
            var client = accept.Result;
            client.NoDelay = true;
            client.ReceiveTimeout = (int)launch.ReplyTimeout.TotalMilliseconds;
            client.SendTimeout = (int)launch.ReplyTimeout.TotalMilliseconds;

            new FrameWriter(MessageType.Hello).I32(ProtocolVersion).Send(client.GetStream());
            var ack = Receive(client.GetStream(), MessageType.HelloAck, console);
            var version = ack.I32();
            var sdk = ack.Text();
            var echoed = ack.Text();
            if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(echoed), System.Text.Encoding.ASCII.GetBytes(token)))
            {
                client.Dispose();
                throw new RunnerException("a process that is not our runner connected to the simulator port");
            }
            if (version != ProtocolVersion)
            {
                client.Dispose();
                throw new RunnerException($"runner protocol {version}, simulator protocol {ProtocolVersion}");
            }
            return new RunnerSession(process, client, sdk, console);
        }
        catch
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }
        finally
        {
            listener.Stop();
        }
    }

    public IReadOnlyList<OpModeInfo> ListOpModes()
    {
        Send(new FrameWriter(MessageType.ListOpModes));
        var r = Receive(MessageType.OpModes);
        var n = r.I32();
        var list = new List<OpModeInfo>(n);
        for (var i = 0; i < n; i++)
        {
            list.Add(new OpModeInfo(r.Text(), r.Text(), r.Text(), r.U8() == 1, r.Text()));
        }
        return list;
    }

    /// <summary>Builds the runner's hardware map. Also resets its runtime (call once per OpMode session).</summary>
    public void Configure(HardwareCosts costs, int hubCount, IReadOnlyList<RunnerDevice> devices)
    {
        var w = new FrameWriter(MessageType.Configure)
            .I64(costs.ControlHubCommand).I64(costs.ExpansionHubCommand).I64(costs.ControlHubI2cRead)
            .I64(costs.LoopOverhead).I64(costs.TrivialCall).I64(costs.GamepadPeriod)
            .I32(hubCount).I32(devices.Count);
        foreach (var d in devices)
        {
            w.Str(d.Name).Str(d.Type).I32(d.Hub).I32(d.Port).Str(d.MotorTypeName)
                .F64(d.TicksPerRev).F64(d.Gearing).F64(d.MaxRpm).F64(d.AchievableFraction).Str(d.Orientation);
        }
        Send(w);
        Receive(MessageType.ConfigureAck);
    }

    /// <summary>INIT pressed: loads and starts the OpMode. Throws <see cref="RunnerException"/> if it cannot be loaded.</summary>
    public void Init(string className)
    {
        Send(new FrameWriter(MessageType.Init).Str(className));
        Receive(MessageType.InitAck);
    }

    public void Start()
    {
        Send(new FrameWriter(MessageType.Start));
        Receive(MessageType.StartAck);
    }

    public void Stop()
    {
        Send(new FrameWriter(MessageType.Stop));
        Receive(MessageType.StopAck);
    }

    /// <summary>Runs the OpMode for one simulation step and returns its commands.</summary>
    public TickResult Tick(TickInput input)
    {
        var w = new FrameWriter(MessageType.Tick).I64(input.Tick).I64(input.EndNanos);
        WriteGamepad(w, input.Gamepad1);
        WriteGamepad(w, input.Gamepad2);
        var q = input.ImuOrientation;
        var av = input.ImuAngularVelocity;
        w.F64(input.BatteryVolts).F64(q.W).F64(q.X).F64(q.Y).F64(q.Z).F64(av.X).F64(av.Y).F64(av.Z).I32(input.Motors.Count);
        foreach (var m in input.Motors)
        {
            w.Str(m.Key).I32(m.Position).F64(m.Velocity).F64(m.CurrentAmps).Bool(m.Busy);
        }
        Send(w);

        var r = Receive(MessageType.State);
        var now = r.I64();
        var state = (RunnerState)r.U8();
        var error = r.Str();
        var motors = new MotorCommand[r.I32()];
        for (var i = 0; i < motors.Length; i++)
        {
            var key = r.Text();
            var mode = r.Text();
            var power = r.F64();
            var velocitySet = r.Bool();
            var velocity = r.F64();
            var target = r.I32();
            var zeroPower = r.Text();
            var tolerance = r.I32();
            var enabled = r.Bool();
            var resets = r.I32();
            var hasPidf = r.Bool();
            var pidf = new[] { r.F64(), r.F64(), r.F64(), r.F64() };
            var positionP = r.F64();
            motors[i] = new MotorCommand(key, mode, power, velocitySet, velocity, target, zeroPower, tolerance, enabled, resets,
                hasPidf ? pidf : null, positionP);
        }
        var servos = new ServoCommand[r.I32()];
        for (var i = 0; i < servos.Length; i++)
        {
            servos[i] = new ServoCommand(r.Text(), r.F64(), r.Bool());
        }
        var transmissions = r.I64();
        var telemetry = ReadStrings(r);
        var log = ReadStrings(r);
        return new TickResult(now, state, error, motors, servos, transmissions, telemetry, log);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                Send(new FrameWriter(MessageType.Quit));
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or RunnerException)
        {
            // the runner is already gone
        }
        _client.Dispose();
        if (!_process.WaitForExit(2000))
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(2000);
        }
        _process.Dispose();
    }

    private static void WriteGamepad(FrameWriter w, GamepadState g) =>
        w.F32(g.LeftStickX).F32(g.LeftStickY).F32(g.RightStickX).F32(g.RightStickY).F32(g.LeftTrigger).F32(g.RightTrigger)
            .I32((int)g.Buttons).I32(g.Id);

    private static List<string> ReadStrings(FrameReader r)
    {
        var n = r.I32();
        var list = new List<string>(n);
        for (var i = 0; i < n; i++)
        {
            list.Add(r.Text());
        }
        return list;
    }

    private void Send(FrameWriter frame)
    {
        try
        {
            frame.Send(_stream);
        }
        catch (IOException e)
        {
            throw Dead("could not send to the runner: " + e.Message);
        }
    }

    private FrameReader Receive(byte expected) => Receive(_stream, expected, _console, this);

    private static FrameReader Receive(Stream stream, byte expected, Queue<string> console, RunnerSession? session = null)
    {
        FrameReader? frame;
        try
        {
            frame = FrameReader.Read(stream);
        }
        catch (IOException e)
        {
            throw session?.Dead("the runner stopped answering: " + e.Message) ?? new RunnerException("the runner stopped answering: " + e.Message + " " + Tail(console));
        }
        if (frame is null)
        {
            throw session?.Dead("the runner closed the connection") ?? new RunnerException("the runner closed the connection. " + Tail(console));
        }
        var type = frame.U8();
        if (type == MessageType.Error)
        {
            throw new RunnerException(frame.Text());
        }
        if (type != expected)
        {
            throw new RunnerException($"protocol error: expected message {expected}, got {type}");
        }
        return frame;
    }

    private RunnerException Dead(string message) => new($"{message}. {Tail(_console)}");

    private static void Remember(Queue<string> console, string? line)
    {
        if (line is null)
        {
            return;
        }
        lock (console)
        {
            console.Enqueue(line);
            while (console.Count > ConsoleLimit)
            {
                console.Dequeue();
            }
        }
    }

    private static string Tail(Queue<string> console)
    {
        lock (console)
        {
            var tail = console.Skip(Math.Max(0, console.Count - 10)).ToList();
            return tail.Count == 0 ? "" : "Runner output: " + string.Join(" | ", tail);
        }
    }
}
