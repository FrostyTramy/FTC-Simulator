using System.IO.Compression;
using System.Text;
using Sim.Runner;

namespace Sim.Match.Replay;

/// <summary>Things that happen to the MATCH or the FIELD at a tick, outside the robots' drivers.</summary>
public enum ReplayEventKind : byte
{
    MatchStart = 1,
    MatchAbort = 2,
    Practice = 3,

    /// <summary>A human player entered a NECTAR: Station = the entering ALLIANCE's station 1, Values = element index.</summary>
    NectarEntry = 4,
    FieldReset = 5,

    /// <summary>Practice spawn: Text = element group, Values = x, y, z.</summary>
    SpawnElement = 6,

    /// <summary>A new runner JVM session for the station's robot (RunnerBridge.BeginSession).</summary>
    RunnerSessionBegin = 7,

    /// <summary>Driver Station button (Text = "init OpMode name", "play", "stop"): shown in the replay, not re-simulated.</summary>
    DsAction = 8,

    /// <summary>Assist-mode referee decision: Values = foul id, 1 confirm / 0 reject.</summary>
    FoulDecision = 9,

    /// <summary>An element moved by hand (practice drag): Values = element index, x, y, z, vx, vy, vz.</summary>
    ElementMoved = 10,
}

public sealed record ReplayEvent(long Tick, ReplayEventKind Kind, Station? Station = null, string? Text = null, double[]? Values = null);

/// <summary>What drove a station's robot in one tick.</summary>
public enum DriveKind : byte
{
    /// <summary>Nothing new: no runner answer (the SDK's Stop Robot behaviour) or no driver (placeholder).</summary>
    None = 0,

    /// <summary>The runner's STATE (raw motor/servo/sensor commands), applied through the RunnerBridge.</summary>
    Runner = 1,

    /// <summary>The gamepads a C# driver read (direct drive, AI drivers); re-running the driver reproduces its commands.</summary>
    Gamepads = 2,
}

public sealed record StationTick(Station Station, DriveKind Kind, TickResult? Result = null, GamepadState Gamepad1 = default, GamepadState Gamepad2 = default);

/// <summary>Position and rotation (quaternion x, y, z, w) of a body, world frame.</summary>
public readonly record struct BodyPose(float X, float Y, float Z, float Qx, float Qy, float Qz, float Qw);

/// <summary>One frame of the pose track (viewer): robots in station order, elements in field order, moving field parts
/// (hives) in name order, scoreboard.</summary>
public sealed record PoseFrame(long Tick, IReadOnlyList<BodyPose> Robots, IReadOnlyList<BodyPose> Elements, IReadOnlyList<BodyPose> FieldParts,
    int RedScore, int BlueScore, string Timer);

public sealed record ReplayHeader(int Version, string SimBuild, int TickRate, string Layout, MatchSetup Setup, long StartTick,
    IReadOnlyList<Station> RobotOrder, IReadOnlyList<string> ElementGroups);

public sealed record ReplayFooter(long EndTick, ulong StateHash, string Outcome);

/// <summary>A determinism checkpoint: hash of every body's pose and velocity at the tick (spec 01, once per second).</summary>
public readonly record struct Checkpoint(long Tick, ulong Hash);

/// <summary>
/// Replay file (spec 01 "Record: initial state + seed + all inputs ... per tick → a replay file", spec 10 "Replays"):
/// gzip-compressed, versioned blocks after a JSON header. Tick blocks carry every station's drive input and the events
/// of that tick, so re-simulating from the setup reproduces the MATCH (checked against the checkpoints); pose frames
/// let the viewer scrub without simulating.
/// </summary>
public static class ReplayFormat
{
    public const int Version = 1;
    public static readonly byte[] Magic = "FTCREPLAY"u8.ToArray();

    internal const byte TickBlock = 1;
    internal const byte PoseBlock = 2;
    internal const byte CheckpointBlock = 3;
    internal const byte FooterBlock = 0xFF;
}

/// <summary>Writes a replay while the MATCH runs.</summary>
public sealed class ReplayWriter : IDisposable
{
    private readonly BinaryWriter _w;
    private readonly Dictionary<Station, (IReadOnlyList<string> Telemetry, IReadOnlyList<string> Log)> _lastText = new();
    private bool _closed;

    public ReplayWriter(string path, ReplayHeader header)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var gzip = new GZipStream(File.Create(path), CompressionLevel.Optimal);
        _w = new BinaryWriter(gzip, Encoding.UTF8);
        _w.Write(ReplayFormat.Magic);
        _w.Write(ReplayFormat.Version);
        _w.Write(System.Text.Json.JsonSerializer.Serialize(header, MatchSetup.Json));
        FilePath = path;
    }

    public string FilePath { get; }

    public void Tick(long tick, IReadOnlyList<StationTick> stations, IReadOnlyList<ReplayEvent> events)
    {
        _w.Write(ReplayFormat.TickBlock);
        _w.Write(tick);
        _w.Write((byte)events.Count);
        foreach (var e in events)
        {
            _w.Write((byte)e.Kind);
            _w.Write(e.Station is { } s ? (sbyte)s : (sbyte)-1);
            WriteOptional(e.Text);
            var values = e.Values ?? [];
            _w.Write((byte)values.Length);
            foreach (var v in values)
            {
                _w.Write(v);
            }
        }
        _w.Write((byte)stations.Count);
        foreach (var st in stations)
        {
            _w.Write((byte)st.Station);
            _w.Write((byte)st.Kind);
            switch (st.Kind)
            {
                case DriveKind.Runner:
                    WriteResult(st.Station, st.Result!);
                    break;
                case DriveKind.Gamepads:
                    WriteGamepad(st.Gamepad1);
                    WriteGamepad(st.Gamepad2);
                    break;
            }
        }
    }

    public void Pose(PoseFrame f)
    {
        _w.Write(ReplayFormat.PoseBlock);
        _w.Write(f.Tick);
        WritePoses(f.Robots);
        WritePoses(f.Elements);
        WritePoses(f.FieldParts);
        _w.Write(f.RedScore);
        _w.Write(f.BlueScore);
        _w.Write(f.Timer);
    }

    public void Checkpoint(Checkpoint c)
    {
        _w.Write(ReplayFormat.CheckpointBlock);
        _w.Write(c.Tick);
        _w.Write(c.Hash);
    }

    public void Close(ReplayFooter footer)
    {
        if (_closed)
        {
            return;
        }
        _w.Write(ReplayFormat.FooterBlock);
        _w.Write(footer.EndTick);
        _w.Write(footer.StateHash);
        _w.Write(footer.Outcome);
        _w.Dispose();
        _closed = true;
    }

    public void Dispose()
    {
        if (!_closed)
        {
            _w.Dispose();   // unfinished replay (no footer): readers report it as truncated
            _closed = true;
        }
    }

    private void WriteOptional(string? s)
    {
        _w.Write(s != null);
        if (s != null)
        {
            _w.Write(s);
        }
    }

    private void WritePoses(IReadOnlyList<BodyPose> poses)
    {
        _w.Write(poses.Count);
        foreach (var p in poses)
        {
            _w.Write(p.X);
            _w.Write(p.Y);
            _w.Write(p.Z);
            _w.Write(p.Qx);
            _w.Write(p.Qy);
            _w.Write(p.Qz);
            _w.Write(p.Qw);
        }
    }

    private void WriteGamepad(GamepadState g)
    {
        _w.Write(g.LeftStickX);
        _w.Write(g.LeftStickY);
        _w.Write(g.RightStickX);
        _w.Write(g.RightStickY);
        _w.Write(g.LeftTrigger);
        _w.Write(g.RightTrigger);
        _w.Write((int)g.Buttons);
        _w.Write(g.Id);
    }

    private void WriteResult(Station station, TickResult r)
    {
        _w.Write(r.NowNanos);
        _w.Write((byte)r.State);
        WriteOptional(r.Error);
        _w.Write((byte)r.Motors.Count);
        foreach (var m in r.Motors)
        {
            _w.Write(m.Key);
            _w.Write(m.Mode);
            _w.Write(m.Power);
            _w.Write(m.VelocitySet);
            _w.Write(m.Velocity);
            _w.Write(m.TargetPosition);
            _w.Write(m.ZeroPower);
            _w.Write(m.Tolerance);
            _w.Write(m.Enabled);
            _w.Write(m.ResetCount);
            _w.Write((sbyte)(m.VelocityPidf?.Length ?? -1));
            foreach (var k in m.VelocityPidf ?? [])
            {
                _w.Write(k);
            }
            _w.Write(m.PositionP);
        }
        _w.Write((byte)r.Servos.Count);
        foreach (var s in r.Servos)
        {
            _w.Write(s.Key);
            _w.Write(s.PulseUs);
            _w.Write(s.PwmEnabled);
        }
        _w.Write(r.TelemetryTransmissions);
        // Telemetry and log lines are written only when they change (they repeat every tick).
        var same = _lastText.TryGetValue(station, out var last) && last.Telemetry.SequenceEqual(r.Telemetry) && last.Log.SequenceEqual(r.Log);
        _w.Write(!same);
        if (!same)
        {
            WriteStrings(r.Telemetry);
            WriteStrings(r.Log);
            _lastText[station] = (r.Telemetry, r.Log);
        }
        _w.Write((byte)r.SensorCommands.Count);
        foreach (var c in r.SensorCommands)
        {
            _w.Write(c.Name);
            _w.Write(c.Command);
            _w.Write((byte)c.Args.Length);
            foreach (var a in c.Args)
            {
                _w.Write(a);
            }
        }
    }

    private void WriteStrings(IReadOnlyList<string> lines)
    {
        _w.Write((ushort)lines.Count);
        foreach (var l in lines)
        {
            _w.Write(l);
        }
    }
}

/// <summary>One recorded tick: events and the stations' drive inputs.</summary>
public sealed record ReplayTick(long Tick, IReadOnlyList<ReplayEvent> Events, IReadOnlyList<StationTick> Stations);

/// <summary>Reads a replay as a stream of blocks (re-simulation) or all at once without the tick inputs (viewer).</summary>
public sealed class ReplayReader : IDisposable
{
    private readonly BinaryReader _r;
    private readonly Dictionary<Station, (IReadOnlyList<string> Telemetry, IReadOnlyList<string> Log)> _lastText = new();

    public ReplayReader(string path)
    {
        _r = new BinaryReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress), Encoding.UTF8);
        var magic = _r.ReadBytes(ReplayFormat.Magic.Length);
        if (!magic.AsSpan().SequenceEqual(ReplayFormat.Magic))
        {
            throw new InvalidDataException($"{path} is not a replay file");
        }
        var version = _r.ReadInt32();
        if (version != ReplayFormat.Version)
        {
            throw new InvalidDataException($"{path}: replay version {version}, this simulator reads version {ReplayFormat.Version}");
        }
        Header = System.Text.Json.JsonSerializer.Deserialize<ReplayHeader>(_r.ReadString(), MatchSetup.Json)
            ?? throw new InvalidDataException($"{path}: empty replay header");
    }

    public ReplayHeader Header { get; }
    public ReplayFooter? Footer { get; private set; }
    public List<Checkpoint> Checkpoints { get; } = new();

    /// <summary>Tick blocks in order; pose frames go to <paramref name="poses"/> (if given), checkpoints and the footer
    /// to the properties. Ends at the footer, or at the end of a truncated file.</summary>
    public IEnumerable<ReplayTick> Ticks(Action<PoseFrame>? poses = null)
    {
        while (true)
        {
            int tag;
            try
            {
                tag = _r.ReadByte();
            }
            catch (EndOfStreamException)
            {
                yield break;   // no footer: the recording was cut off
            }
            switch (tag)
            {
                case ReplayFormat.TickBlock:
                    yield return ReadTick();
                    break;
                case ReplayFormat.PoseBlock:
                    var frame = ReadPose();
                    poses?.Invoke(frame);
                    break;
                case ReplayFormat.CheckpointBlock:
                    Checkpoints.Add(new Checkpoint(_r.ReadInt64(), _r.ReadUInt64()));
                    break;
                case ReplayFormat.FooterBlock:
                    Footer = new ReplayFooter(_r.ReadInt64(), _r.ReadUInt64(), _r.ReadString());
                    yield break;
                default:
                    throw new InvalidDataException($"unknown replay block {tag}");
            }
        }
    }

    /// <summary>Everything the viewer needs: pose frames and events (tick inputs are skipped after reading).</summary>
    public (List<PoseFrame> Frames, List<ReplayEvent> Events) ReadForViewing()
    {
        var frames = new List<PoseFrame>();
        var events = new List<ReplayEvent>();
        foreach (var t in Ticks(frames.Add))
        {
            events.AddRange(t.Events);
        }
        return (frames, events);
    }

    public void Dispose() => _r.Dispose();

    private string? ReadOptional() => _r.ReadBoolean() ? _r.ReadString() : null;

    private ReplayTick ReadTick()
    {
        var tick = _r.ReadInt64();
        var events = new List<ReplayEvent>();
        for (int i = 0, n = _r.ReadByte(); i < n; i++)
        {
            var kind = (ReplayEventKind)_r.ReadByte();
            var station = _r.ReadSByte();
            var text = ReadOptional();
            var values = new double[_r.ReadByte()];
            for (var k = 0; k < values.Length; k++)
            {
                values[k] = _r.ReadDouble();
            }
            events.Add(new ReplayEvent(tick, kind, station < 0 ? null : (Station)station, text, values.Length == 0 ? null : values));
        }
        var stations = new List<StationTick>();
        for (int i = 0, n = _r.ReadByte(); i < n; i++)
        {
            var station = (Station)_r.ReadByte();
            var kind = (DriveKind)_r.ReadByte();
            stations.Add(kind switch
            {
                DriveKind.Runner => new StationTick(station, kind, ReadResult(station)),
                DriveKind.Gamepads => new StationTick(station, kind, null, ReadGamepad(), ReadGamepad()),
                _ => new StationTick(station, kind),
            });
        }
        return new ReplayTick(tick, events, stations);
    }

    private PoseFrame ReadPose()
    {
        var tick = _r.ReadInt64();
        var robots = ReadPoses();
        var elements = ReadPoses();
        var parts = ReadPoses();
        return new PoseFrame(tick, robots, elements, parts, _r.ReadInt32(), _r.ReadInt32(), _r.ReadString());
    }

    private List<BodyPose> ReadPoses()
    {
        var n = _r.ReadInt32();
        var list = new List<BodyPose>(n);
        for (var i = 0; i < n; i++)
        {
            list.Add(new BodyPose(_r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle()));
        }
        return list;
    }

    private GamepadState ReadGamepad() => new(_r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle(),
        (GamepadButtons)_r.ReadInt32(), _r.ReadInt32());

    private TickResult ReadResult(Station station)
    {
        var now = _r.ReadInt64();
        var state = (RunnerState)_r.ReadByte();
        var error = ReadOptional();
        var motors = new List<MotorCommand>();
        for (int i = 0, n = _r.ReadByte(); i < n; i++)
        {
            var key = _r.ReadString();
            var mode = _r.ReadString();
            var power = _r.ReadDouble();
            var velocitySet = _r.ReadBoolean();
            var velocity = _r.ReadDouble();
            var target = _r.ReadInt32();
            var zeroPower = _r.ReadString();
            var tolerance = _r.ReadInt32();
            var enabled = _r.ReadBoolean();
            var resets = _r.ReadInt32();
            var pidfLength = _r.ReadSByte();
            double[]? pidf = null;
            if (pidfLength >= 0)
            {
                pidf = new double[pidfLength];
                for (var k = 0; k < pidfLength; k++)
                {
                    pidf[k] = _r.ReadDouble();
                }
            }
            motors.Add(new MotorCommand(key, mode, power, velocitySet, velocity, target, zeroPower, tolerance, enabled, resets, pidf, _r.ReadDouble()));
        }
        var servos = new List<ServoCommand>();
        for (int i = 0, n = _r.ReadByte(); i < n; i++)
        {
            servos.Add(new ServoCommand(_r.ReadString(), _r.ReadDouble(), _r.ReadBoolean()));
        }
        var transmissions = _r.ReadInt64();
        if (_r.ReadBoolean())
        {
            _lastText[station] = (ReadStrings(), ReadStrings());
        }
        var text = _lastText.TryGetValue(station, out var t) ? t : (Array.Empty<string>(), Array.Empty<string>());
        var sensors = new List<SensorCommand>();
        for (int i = 0, n = _r.ReadByte(); i < n; i++)
        {
            var name = _r.ReadString();
            var command = _r.ReadString();
            var args = new double[_r.ReadByte()];
            for (var k = 0; k < args.Length; k++)
            {
                args[k] = _r.ReadDouble();
            }
            sensors.Add(new SensorCommand(name, command, args));
        }
        return new TickResult(now, state, error, motors, servos, transmissions, text.Item1, text.Item2, sensors);
    }

    private IReadOnlyList<string> ReadStrings()
    {
        var n = _r.ReadUInt16();
        var lines = new string[n];
        for (var i = 0; i < n; i++)
        {
            lines[i] = _r.ReadString();
        }
        return lines;
    }
}
