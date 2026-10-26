using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Sim.App;
using Sim.Core.Events;
using Sim.DriverStation;
using Sim.Field;
using Sim.Game;
using Sim.Game.HumanPlayer;
using Sim.Game.Match;
using Sim.Game.Setup;
using Sim.Match.Replay;
using Sim.Robot;
using Sim.Runner;

namespace Sim.Match;

/// <summary>Live: drivers, code and players act and everything is recorded. Verify: a replay is re-simulated.</summary>
public enum SessionMode
{
    Live,
    Verify,
}

/// <summary>One driver station's robot and what drives it.</summary>
public sealed class StationRuntime
{
    public StationRuntime(StationSetup setup, RobotBody robot, Transform3D start)
    {
        Setup = setup;
        Robot = robot;
        Start = start;
        Stats = new RobotStatsTracker(setup, robot.Definition.Name);
    }

    public StationSetup Setup { get; }
    public Station Station => Setup.Station;
    public RobotBody Robot { get; }
    public Transform3D Start { get; }
    public RobotStatsTracker Stats { get; }

    /// <summary>The team's code (Code stations, live).</summary>
    public RobotProgram? Program { get; set; }

    /// <summary>Gamepad driving without code (Gamepad stations).</summary>
    public DirectDrive? Direct { get; set; }

    /// <summary>The station's gamepads after field control (neutral during AUTO, G401).</summary>
    public FieldGamepads? Pads { get; set; }

    public ReplayDriver? Replay { get; set; }
    public RecordedGamepads? RecordedPads { get; set; }
    public DriverStationApp? Ds => Program?.Ds;
}

/// <summary>
/// The FIELD is in charge of the gamepads during the MATCH: drivers give no input during AUTO (G401 p106), so the
/// field hands neutral gamepads to every robot until TELEOP (rules sim handling "gamepad input ignored during AUTO").
/// </summary>
public sealed class FieldGamepads : IGamepadSource
{
    private readonly IGamepadSource _source;
    private readonly Func<bool> _blocked;

    public FieldGamepads(IGamepadSource source, Func<bool> blocked)
    {
        _source = source;
        _blocked = blocked;
    }

    public GamepadState Gamepad1 => _blocked() ? GamepadState.Neutral with { Id = _source.Gamepad1.Id } : _source.Gamepad1;
    public GamepadState Gamepad2 => _blocked() ? GamepadState.Neutral with { Id = _source.Gamepad2.Id } : _source.Gamepad2;
}

/// <summary>
/// A MATCH on the FIELD (spec 08 "Match control", Phase 10): spawns the setup's robots at their start positions (G304,
/// config/match.yaml), gives each its 4 pre-load POLLEN, runs the drive teams' Driver Station steps (INIT AUTO before
/// the start, PLAY at the start, the TeleOp INIT in the transition and PLAY at TELEOP), the human players' NECTAR (keys
/// or automatic) and Assist-mode referee decisions, and keeps robot statistics. Every external action is queued and
/// applied at the start of a physics tick, so a replay applies it at the same point; the end of each tick writes the
/// replay (drive inputs, events, determinism checkpoints, and from the start of the MATCH a 60 Hz pose track). The
/// replay starts with the session, because what happens before the start (robots settling, INIT code moving servos)
/// shapes the MATCH; a session that never plays a MATCH deletes its replay. One session plays at most one MATCH.
/// When the results are final the <see cref="MatchRecord"/> is saved to the match history. In
/// <see cref="SessionMode.Verify"/> mode the replay drives everything instead and the checkpoints are compared.
/// </summary>
public partial class MatchSession : Node
{
    private readonly LoadedField _field;
    private readonly PhysicsConfig _physics;
    private readonly GamepadInput? _input;
    private readonly MatchConfig _config;
    private readonly List<StationRuntime> _stations = new();
    private readonly Queue<ReplayEvent> _queue = new();
    private readonly List<ReplayEvent> _tickEvents = new();
    private readonly Dictionary<Alliance, long> _nectarAvailableSince = new();
    private readonly Dictionary<int, Alliance> _airborneNectar = new();
    private readonly HashSet<int> _nectarOffField = new();
    private readonly Dictionary<long, ulong> _checkpoints = new();
    private readonly string _historyDir;
    private FieldReset _reset = null!;
    private ReplayWriter? _writer;
    private ReplayReader? _reader;
    private IEnumerator<ReplayTick>? _replayTicks;
    private ReplayTick? _pendingReplay;
    private int _seenEvents;
    private long _tick;
    private bool _recordingMatch;
    private bool _matchPlayed;
    private string? _recordId;
    private long _matchStartTick = -1;

    public MatchSession(LoadedField field, MatchSetup setup, PhysicsConfig physics, GamepadInput? input, string historyDir,
        SessionMode mode = SessionMode.Live, string? replayPath = null)
    {
        _field = field;
        Setup = setup;
        _physics = physics;
        _input = input;
        _historyDir = historyDir;
        Mode = mode;
        _config = MatchConfig.Load(RepoPaths.Config("match.yaml"));
        if (mode == SessionMode.Verify)
        {
            _reader = new ReplayReader(replayPath ?? throw new ArgumentNullException(nameof(replayPath)));
            Setup = _reader.Header.Setup;
        }
        Name = "MatchSession";
        ProcessPhysicsPriority = -100;   // events are applied before the robots step
    }

    public MatchSetup Setup { get; }
    public SessionMode Mode { get; }

    /// <summary>Gamepads of stations without a person (scripted tests, AI drivers); others use <see cref="GamepadInput"/>.</summary>
    public Dictionary<Station, IGamepadSource> GamepadSources { get; } = new();
    public IReadOnlyList<StationRuntime> Stations => _stations;
    public MatchController Controller { get; private set; } = null!;
    public GameDirector Director => Controller.Director;
    public long Tick => _tick;

    /// <summary>The record of the last finished or aborted MATCH.</summary>
    public MatchRecord? LastRecord { get; private set; }

    /// <summary>Problems that keep the MATCH from starting (G304), refreshed every second before the start.</summary>
    public IReadOnlyList<SetupProblem> SetupProblems { get; private set; } = [];

    /// <summary>Verify mode: checkpoint ticks where the re-simulation differed from the recording.</summary>
    public List<long> Mismatches { get; } = new();

    /// <summary>Verify mode: the replay has been used up.</summary>
    public bool ReplayFinished { get; private set; }

    public event Action<MatchRecord>? MatchFinished;

    public StationRuntime? Of(Station s) => _stations.FirstOrDefault(x => x.Station == s);

    // ------------------------------------------------------------------ actions (queued, applied at the next tick)
    public void StartMatch() => Queue(ReplayEventKind.MatchStart);

    public void Abort() => Queue(ReplayEventKind.MatchAbort);

    public void Practice() => Queue(ReplayEventKind.Practice);

    public void ResetField() => Queue(ReplayEventKind.FieldReset);

    public void EnterNectar(Alliance alliance) =>
        Queue(ReplayEventKind.NectarEntry, alliance == Alliance.Red ? Station.Red1 : Station.Blue1);

    public void DecideFoul(int foulId, bool confirm) =>
        _queue.Enqueue(new ReplayEvent(0, ReplayEventKind.FoulDecision, Values: [foulId, confirm ? 1 : 0]));

    public void SpawnElement(string group, Vector3 at) =>
        _queue.Enqueue(new ReplayEvent(0, ReplayEventKind.SpawnElement, Text: group, Values: [at.X, at.Y, at.Z]));

    private void Queue(ReplayEventKind kind, Station? station = null) => _queue.Enqueue(new ReplayEvent(0, kind, station));

    // ------------------------------------------------------------------ setup
    public override void _Ready()
    {
        var problems = Setup.Problems(n => File.Exists(RepoPaths.Config(Path.Combine("robots", n + ".yaml"))));
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("match setup: " + string.Join("; ", problems));
        }
        foreach (var s in Setup.Robots)
        {
            _stations.Add(SpawnStation(s));
        }
        var options = new GameOptions(Setup.FoulMode, EventType: Setup.EventType, TickRate: Engine.PhysicsTicksPerSecond);
        Controller = new MatchController(_field, _stations.Select(s => new MatchRobot(s.Station.RobotId(), s.Station.Alliance(), s.Setup.TeamLabel, s.Robot)),
            _physics, options) { Name = "Match", ProcessPhysicsPriority = 50 };
        AddChild(Controller);
        AddChild(new Tail(this) { Name = "SessionTail", ProcessPhysicsPriority = 100 });
        _reset = new FieldReset(_field, _stations.Select(s => s.Robot));
        GivePreloads();
        if (Mode == SessionMode.Verify)
        {
            _replayTicks = _reader!.Ticks().GetEnumerator();
        }
        else
        {
            OpenReplay();
        }
        foreach (var s in _stations.Where(s => s.Program != null))
        {
            s.Program!.Connect();
            PrepareAuto(s);
        }
    }

    private StationRuntime SpawnStation(StationSetup setup)
    {
        // Spawn far away, measure the outline, then put the robot against its wall (config/match.yaml).
        var robot = RobotBody.Spawn(this, setup.Robot, new Transform3D(Basis.Identity, new Vector3(0, -50, 0)), _physics,
            nodeName: $"Robot_{setup.Station.Key()}");
        var outline = robot.FootprintLocal.Select(p => new Core.Numerics.Vec2(p.X, p.Y)).ToList();
        var g = Controller?.Geometry ?? Game.Field.FieldGeometry.Load(RepoPaths.Config("field_mechanisms.yaml"));
        var pose = _config.StartPose(setup.Station, outline, g.WallXMin, g.WallXMax, g.WallZMin, g.WallZMax);
        var start = new Transform3D(new Basis(Vector3.Up, (float)pose.YawRad), new Vector3((float)pose.X, _field.TileTopY, (float)pose.Z));
        robot.ResetTo(start);
        robot.Elements = _field.Elements;
        robot.Powertrain.Battery.SetStateOfCharge(setup.BatteryCharge);
        var station = new StationRuntime(setup, robot, start);
        robot.ElementCaptured += _ => station.Stats.Intake();
        robot.ElementLaunched += _ => station.Stats.Launch();
        if (Mode == SessionMode.Verify)
        {
            // Either driver may be recorded on a tick (a code robot switched to direct drive in practice).
            if (setup.Kind is StationKind.Code or StationKind.Gamepad)
            {
                station.Replay = new ReplayDriver(robot);
                station.RecordedPads = new RecordedGamepads();
                station.Direct = new DirectDrive(station.RecordedPads);
                robot.Driver = setup.Kind == StationKind.Code ? station.Replay : station.Direct;
            }
            return station;
        }
        IGamepadSource seat = GamepadSources.TryGetValue(setup.Station, out var source) ? source
            : _input != null ? _input.Seat(setup.Station.Key()) : new RecordedGamepads();
        station.Pads = new FieldGamepads(seat, () => Controller.Director.Match.Phase == MatchPhase.Auto);
        switch (setup.Kind)
        {
            case StationKind.Code:
                station.Program = new RobotProgram(robot, station.Pads, setup.TeamCodeJar!);
                station.Program.SessionBegan += () => _tickEvents.Add(new ReplayEvent(_tick, ReplayEventKind.RunnerSessionBegin, setup.Station));
                robot.Driver = station.Program;
                break;
            case StationKind.Gamepad:
                station.Direct = new DirectDrive(station.Pads);
                robot.Driver = station.Direct;
                break;
        }
        return station;
    }

    /// <summary>
    /// G304.G: every robot touches exactly 4 pre-load POLLEN. The off-field pre-loads of the start layout (beside the
    /// ALLIANCE walls) go into each robot's hopper, nearest first; a robot without a hopper gets them on the tiles in
    /// front of it; an empty station's go to the centre of its LOADING ZONE against the wall (§10.3.1 p83).
    /// </summary>
    private void GivePreloads()
    {
        var per = Controller.Rules.PollenPreloadPerRobot;
        var g = Controller.Geometry;
        var pool = _field.Elements.Where(e => e.Group == "pollen" && (Math.Abs(e.StartPosition.X) > g.WallXMax || Math.Abs(e.StartPosition.Z) > g.WallZMax)).ToList();
        FieldElement? Take(Alliance a, Vector3 near)
        {
            var e = pool.Where(p => a == Alliance.Red ? p.StartPosition.X < 0 : p.StartPosition.X > 0)
                .OrderBy(p => p.StartPosition.DistanceTo(near)).ThenBy(p => _field.Elements.IndexOf(p)).FirstOrDefault();
            if (e != null)
            {
                pool.Remove(e);
            }
            return e;
        }
        foreach (var s in _stations)
        {
            for (var k = 0; k < per && Take(s.Station.Alliance(), s.Start.Origin) is { } e; k++)
            {
                if (!s.Robot.Preload(e))
                {
                    var front = s.Start * new Vector3((k - 1.5f) * 0.075f, 0.04f, s.Robot.FootprintLocal.Min(p => p.Y) - 0.04f);
                    Place(e.Body, front);
                }
            }
        }
        foreach (var empty in Setup.Stations.Where(s => s.Kind == StationKind.Empty))
        {
            var a = empty.Station.Alliance();
            var (minX, maxX, minZ, maxZ) = Bounds(g.LoadingZone(a));
            var wallX = a == Alliance.Red ? minX + 0.04 : maxX - 0.04;
            var centreZ = (minZ + maxZ) / 2;
            for (var k = 0; k < per && Take(a, new Vector3((float)wallX, 0, (float)centreZ)) is { } e; k++)
            {
                Place(e.Body, new Vector3((float)wallX, _field.TileTopY + 0.04f + k * 0.075f, (float)(centreZ + (k - 1.5) * 0.002)));
            }
        }
    }

    /// <summary>
    /// Where a human player would put the next NECTAR: the free spot of a 3 × 5 grid over the LOADING ZONE nearest its
    /// centre (no element or robot within a ball's width), else the centre.
    /// </summary>
    private (double X, double Z) FreeSpot(Game.Geometry.Polygon2 zone, double radius)
    {
        var (minX, maxX, minZ, maxZ) = Bounds(zone);
        var centre = ((minX + maxX) / 2, (minZ + maxZ) / 2);
        var inset = 1.5 * radius;   // clear of the wall and the tape edge: the TILE must be the first thing it touches
        var spots = from i in Enumerable.Range(0, 3)
                    from k in Enumerable.Range(0, 5)
                    let x = minX + inset + (maxX - minX - 2 * inset) * i / 2.0
                    let z = minZ + inset + (maxZ - minZ - 2 * inset) * k / 4.0
                    orderby (x - centre.Item1) * (x - centre.Item1) + (z - centre.Item2) * (z - centre.Item2), i, k
                    select (x, z);
        foreach (var (x, z) in spots)
        {
            var clear = _field.Elements.All(e => new Vector2(e.Body.GlobalPosition.X - (float)x, e.Body.GlobalPosition.Z - (float)z).Length() > 2.2 * radius)
                        && _stations.All(s => !Controller.FootprintOf(s.Robot).IntersectsCircle(new Core.Numerics.Vec2(x, z), radius));
            if (clear)
            {
                return (x, z);
            }
        }
        return centre;
    }

    private static (double MinX, double MaxX, double MinZ, double MaxZ) Bounds(Game.Geometry.Polygon2 zone) =>
        (zone.Vertices.Min(v => v.X), zone.Vertices.Max(v => v.X), zone.Vertices.Min(v => v.Z), zone.Vertices.Max(v => v.Z));

    private static void Place(RigidBody3D body, Vector3 at)
    {
        body.GlobalTransform = new Transform3D(Basis.Identity, at);
        body.LinearVelocity = Vector3.Zero;
        body.AngularVelocity = Vector3.Zero;
        body.Sleeping = false;
    }

    /// <summary>Before the start the drive team selects its AUTO OpMode (or its TeleOp), enables the 30 s timer and INITs.</summary>
    private void PrepareAuto(StationRuntime s)
    {
        var ds = s.Ds!;
        var auto = ds.Autonomous.FirstOrDefault(o => o.Name == s.Setup.AutoOpMode);
        var opMode = auto ?? ds.TeleOps.FirstOrDefault(o => o.Name == s.Setup.TeleOpMode);
        if (opMode == null || !ds.CanSelect)
        {
            return;
        }
        ds.Select(opMode);
        ds.AutonomousTimerEnabled = auto != null;
        ds.Init();
        _tickEvents.Add(new ReplayEvent(_tick, ReplayEventKind.DsAction, s.Station, "init " + opMode.Name));
    }

    // ------------------------------------------------------------------ tick start
    public override void _PhysicsProcess(double delta)
    {
        if (Mode == SessionMode.Verify)
        {
            NextReplayTick();
        }
        else
        {
            while (_queue.Count > 0)
            {
                _tickEvents.Add(_queue.Dequeue() with { Tick = _tick });
            }
            AutoHumanPlayer();
        }
        foreach (var e in _tickEvents.ToList())
        {
            ApplyEvent(e);
        }
    }

    private void NextReplayTick()
    {
        if (_replayTicks == null || ReplayFinished)
        {
            return;
        }
        if (!_replayTicks.MoveNext())
        {
            ReplayFinished = true;
            return;
        }
        var t = _replayTicks.Current;
        if (t.Tick != _tick)
        {
            throw new InvalidDataException($"replay tick {t.Tick} where {_tick} was expected");
        }
        _pendingReplay = t;
        _tickEvents.AddRange(t.Events);
        foreach (var st in t.Stations)
        {
            if (Of(st.Station) is not { } s)
            {
                continue;
            }
            if (s.Replay == null)
            {
                continue;   // a placeholder has no driver
            }
            s.Replay.Next = st;
            if (st.Kind == DriveKind.Gamepads)
            {
                s.RecordedPads!.Gamepad1 = st.Gamepad1;
                s.RecordedPads.Gamepad2 = st.Gamepad2;
                s.Robot.Driver = s.Direct;
            }
            else
            {
                s.Robot.Driver = s.Replay;
            }
        }
    }

    private void ApplyEvent(ReplayEvent e)
    {
        switch (e.Kind)
        {
            case ReplayEventKind.MatchStart:
                if (Director.Match.Phase == MatchPhase.PreMatch && !_matchPlayed)
                {
                    Controller.StartMatch();
                    _matchStartTick = _tick;
                    _matchPlayed = true;
                    _recordingMatch = true;
                    foreach (var s in _stations.Where(s => s.Ds is { CanPlay: true }))
                    {
                        s.Ds!.Play(s.Robot.NowNanos);   // the drive team starts its INIT-ed OpMode with the match
                        _tickEvents.Add(new ReplayEvent(_tick, ReplayEventKind.DsAction, s.Station, "play"));
                    }
                }
                break;
            case ReplayEventKind.MatchAbort:
                Controller.Abort();
                break;
            case ReplayEventKind.Practice:
                Controller.Practice();
                break;
            case ReplayEventKind.FieldReset:
                _reset.Apply();
                GivePreloads();
                break;
            case ReplayEventKind.NectarEntry:
                EnterNectarNow(e.Station == Station.Red1 ? Alliance.Red : Alliance.Blue);
                break;
            case ReplayEventKind.FoulDecision when e.Values is [var id, var confirm]:
                if (confirm > 0)
                {
                    Director.Fouls.Confirm((int)id);
                }
                else
                {
                    Director.Fouls.Reject((int)id);
                }
                break;
            case ReplayEventKind.SpawnElement when e.Text != null && e.Values is [var x, var y, var z]:
                FieldLoader.SpawnElement(_field, e.Text, new Transform3D(Basis.Identity, new Vector3((float)x, (float)y, (float)z)));
                break;
            case ReplayEventKind.ElementMoved when e.Values is [var index, var vx, var vy, var vz] && index < _field.Elements.Count:
                var moved = _field.Elements[(int)index].Body;
                moved.LinearVelocity = new Vector3((float)vx, (float)vy, (float)vz);
                moved.AngularVelocity *= 0.9f;
                moved.Sleeping = false;
                break;
            case ReplayEventKind.RunnerSessionBegin when e.Station is { } st && Mode == SessionMode.Verify:
                Of(st)?.Replay?.BeginSession();
                break;
        }
    }

    /// <summary>
    /// G426/G427: the human player puts one NECTAR (reserve first, or one that left the FIELD) into the own LOADING ZONE,
    /// released <c>drop_height</c> above the tiles; its first contact decides whether G427 is met.
    /// </summary>
    private void EnterNectarNow(Alliance alliance)
    {
        var decision = Director.EnterNectar(alliance);
        if (!decision.Allowed)
        {
            return;
        }
        var g = Controller.Geometry;
        var group = alliance == Alliance.Red ? "nectar_red" : "nectar_blue";
        var candidate = _field.Elements.Select((e, i) => (e, i)).Where(x => x.e.Group == group && !x.e.Body.Freeze
            && (Math.Abs(x.e.Body.GlobalPosition.X) > g.WallXMax || Math.Abs(x.e.Body.GlobalPosition.Z) > g.WallZMax)).FirstOrDefault();
        if (candidate.e == null)
        {
            return;
        }
        var radius = _physics.Element(group).RadiusM;
        var body = candidate.e.Body;
        var (x, z) = FreeSpot(g.LoadingZone(alliance), radius);
        Place(body, new Vector3((float)x, _field.TileTopY + (float)_config.NectarDropHeight + radius, (float)z));
        body.ContactMonitor = true;   // its first contact decides G427 (TrackNectar)
        body.MaxContactsReported = 4;
        _airborneNectar[candidate.i] = alliance;
        _nectarOffField.Remove(candidate.i);
    }

    /// <summary>Q-007: with the automatic human player, an allowed NECTAR is entered <c>auto_delay</c> after it becomes available.</summary>
    private void AutoHumanPlayer()
    {
        if (!Setup.AutoHumanPlayer || !Director.Match.IsRunning)
        {
            return;
        }
        foreach (var a in new[] { Alliance.Red, Alliance.Blue })
        {
            var allowed = Director.HumanPlayer.AllowedNow(a, Director.Match.NectarAllEntryOpen(_tick)) > 0;
            if (!allowed)
            {
                _nectarAvailableSince.Remove(a);
                continue;
            }
            var since = _nectarAvailableSince.TryGetValue(a, out var t) ? t : _nectarAvailableSince[a] = _tick;
            if (_tick - since >= (long)(_config.HumanPlayerAutoDelaySeconds * Engine.PhysicsTicksPerSecond))
            {
                _tickEvents.Add(new ReplayEvent(_tick, ReplayEventKind.NectarEntry, a == Alliance.Red ? Station.Red1 : Station.Blue1));
                _nectarAvailableSince[a] = _tick;   // the next one waits again
            }
        }
    }

    private void OpenReplay()
    {
        if (Mode != SessionMode.Live || _writer != null)
        {
            return;
        }
        var id = MatchHistory.NewId(DateTime.UtcNow);
        var history = new MatchHistory(_historyDir);
        var header = new ReplayHeader(ReplayFormat.Version, SimBuild.Id, Engine.PhysicsTicksPerSecond, _field.Layout.Name, Setup, _tick,
            _stations.Select(s => s.Station).ToList(), _field.Elements.Select(e => e.Group).ToList());
        _writer = new ReplayWriter(history.ReplayPathFor(id), header);
        _recordId = id;
    }

    // ------------------------------------------------------------------ tick end
    private void EndOfTick()
    {
        var match = Director.Match;
        FollowMatchEvents();
        foreach (var s in _stations)
        {
            var p = s.Robot.GlobalPosition;
            s.Stats.Update(p.X, p.Z, s.Robot.Powertrain.Battery.BusVolts, s.Robot.Powertrain.Hardware.Motors.Sum(m => Math.Abs(m.CurrentA)));
        }
        TrackNectar();
        if (Mode == SessionMode.Live && _writer != null)
        {
            _writer.Tick(_tick, _stations.Select(Record).ToList(), _tickEvents);
        }
        var sinceStart = _matchStartTick < 0 ? -1 : _tick - _matchStartTick;
        if (sinceStart >= 0 && sinceStart % _config.PoseEveryTicks == 0)
        {
            _writer?.Pose(PoseFrame());
        }
        if (sinceStart >= 0 && sinceStart % _config.CheckpointEveryTicks == 0)
        {
            var hash = StateHash();
            _checkpoints[_tick] = hash;
            _writer?.Checkpoint(new Checkpoint(_tick, hash));
        }
        if (match.Phase == MatchPhase.PreMatch && _tick % Engine.PhysicsTicksPerSecond == 0)
        {
            SetupProblems = CheckSetup();
        }
        if (_recordingMatch && (match.Phase is MatchPhase.Results or MatchPhase.Aborted))
        {
            Finish(match.Phase == MatchPhase.Results ? "completed" : "aborted");
        }
        if (Mode == SessionMode.Verify && ReplayFinished)
        {
            CompareCheckpoints();
        }
        _tickEvents.Clear();
        _tick++;
    }

    /// <summary>What drove the station's robot this tick, from its current driver.</summary>
    private static StationTick Record(StationRuntime s) => s.Robot.Driver switch
    {
        RobotProgram p => p.AppliedThisTick is { } r ? new StationTick(s.Station, DriveKind.Runner, r) : new StationTick(s.Station, DriveKind.None),
        DirectDrive d => new StationTick(s.Station, DriveKind.Gamepads, null, d.LastInput.Gamepad1, d.LastInput.Gamepad2),
        _ => new StationTick(s.Station, DriveKind.None),
    };

    /// <summary>Practice: switch a code station's robot between its OpModes and direct gamepad drive.</summary>
    public void SetDirectDrive(StationRuntime s, bool direct)
    {
        if (s.Program == null || s.Pads == null)
        {
            return;
        }
        s.Direct ??= new DirectDrive(s.Pads);
        s.Robot.Driver = direct ? s.Direct : s.Program;
    }

    /// <summary>Practice: an element dragged with the mouse gets this velocity at the next tick (recorded).</summary>
    public void MoveElement(int index, Vector3 velocity) =>
        _queue.Enqueue(new ReplayEvent(0, ReplayEventKind.ElementMoved, Values: [index, velocity.X, velocity.Y, velocity.Z]));

    /// <summary>The drive teams' Driver Station steps that follow the field: AUTO stops at 2:00 (the DS timer), the TeleOp
    /// is INIT-ed in the transition and started when TELEOP begins.</summary>
    private void FollowMatchEvents()
    {
        var events = Director.Log.Events;
        for (; _seenEvents < events.Count; _seenEvents++)
        {
            switch (events[_seenEvents])
            {
                case StopAutoOpMode:
                    foreach (var s in _stations.Where(s => s.Ds != null))
                    {
                        var ds = s.Ds!;
                        if (ds.Selected is { Autonomous: true } && ds.CanStop)
                        {
                            ds.Stop();
                        }
                    }
                    break;
                case PhaseChanged { To: MatchPhase.Transition }:
                    foreach (var s in _stations.Where(s => s.Ds != null))
                    {
                        InitTeleOp(s);
                    }
                    break;
                case PhaseChanged { To: MatchPhase.Teleop }:
                    foreach (var s in _stations.Where(s => s.Ds is { CanPlay: true }))
                    {
                        s.Ds!.Play(s.Robot.NowNanos);
                        _tickEvents.Add(new ReplayEvent(_tick, ReplayEventKind.DsAction, s.Station, "play"));
                    }
                    break;
            }
        }
    }

    private void InitTeleOp(StationRuntime s)
    {
        var ds = s.Ds!;
        if (ds.Selected is { Autonomous: false } && ds.Phase is DsPhase.Initialized)
        {
            return;   // a team without AUTO already INIT-ed its TeleOp
        }
        var preselected = ds.Selected?.PreselectTeleOp;
        var teleOp = ds.TeleOps.FirstOrDefault(o => o.Name == (s.Setup.TeleOpMode ?? preselected));
        if (teleOp == null || !ds.CanSelect)
        {
            return;
        }
        ds.Select(teleOp);
        ds.Init();
        _tickEvents.Add(new ReplayEvent(_tick, ReplayEventKind.DsAction, s.Station, "init " + teleOp.Name));
    }

    /// <summary>First contact of entered NECTAR (G427) and NECTAR that left the FIELD (§10.8.2).</summary>
    private void TrackNectar()
    {
        var g = Controller.Geometry;
        foreach (var (index, alliance) in _airborneNectar.ToList())
        {
            var body = _field.Elements[index].Body;
            // Other SCORING ELEMENTS are neither a ROBOT nor a field element (G427.C): keep watching until it meets one.
            var touched = body.GetCollidingBodies().Where(n => !n.HasMeta("element_id")).ToList();
            if (touched.Count == 0)
            {
                continue;
            }
            _airborneNectar.Remove(index);
            body.ContactMonitor = false;
            // Several first contacts in one step: a ROBOT counts before a field element before the TILES (G427 is
            // only met when the TILE alone was touched first).
            static bool IsTile(Node3D n) => n is StaticBody3D { Name: var name } && (name == "tiles" || name == "Ground");
            var contact = touched.Any(n => n is RobotBody) ? LandingContact.Robot
                : touched.All(IsTile) ? LandingContact.Tile
                : LandingContact.FieldElement;
            var p = body.GlobalPosition;
            Director.RecordNectarLanding(alliance, index + 1, contact, new Core.Numerics.Vec2(p.X, p.Z));
        }
        if (!Director.Match.IsRunning)
        {
            return;
        }
        for (var i = 0; i < _field.Elements.Count; i++)
        {
            var e = _field.Elements[i];
            if (!e.Group.StartsWith("nectar", StringComparison.Ordinal) || e.Body.Freeze)
            {
                continue;
            }
            var p = e.Body.GlobalPosition;
            var off = Math.Abs(p.X) > g.WallXMax + 0.05 || Math.Abs(p.Z) > g.WallZMax + 0.05;
            var startedOff = Math.Abs(e.StartPosition.X) > g.WallXMax || Math.Abs(e.StartPosition.Z) > g.WallZMax;
            if (off && !startedOff && _nectarOffField.Add(i))
            {
                Director.NectarLeftField(e.Group == "nectar_red" ? Alliance.Red : Alliance.Blue);
            }
            else if (!off)
            {
                _nectarOffField.Remove(i);
            }
        }
    }

    private IReadOnlyList<SetupProblem> CheckSetup()
    {
        if (Controller.Snapshot is not { } snap)
        {
            return [];
        }
        var list = new List<SetupProblem>();
        foreach (var s in _stations)
        {
            if (snap.Robot(s.Station.RobotId()) is not { } state)
            {
                continue;
            }
            var stored = s.Robot.StoredElements.Count();
            var footprint = state.Footprint;
            var touching = _field.Elements.Count(e => e.Group == "pollen" && !e.Body.Freeze
                && footprint.DistanceTo(new Core.Numerics.Vec2(e.Body.GlobalPosition.X, e.Body.GlobalPosition.Z)) < _physics.Element("pollen").RadiusM + 0.005);
            list.AddRange(SetupValidator.Validate(state, Controller.Geometry, Controller.Rules, stored + touching, s.Robot.LinearVelocity.Length() < 0.01f));
        }
        return list;
    }

    private void Finish(string outcome)
    {
        _recordingMatch = false;
        var snap = Controller.Snapshot ?? WorldSnapshot.Empty(_tick);
        var score = Director.FinalScore ?? Director.LiveScore(snap);
        var scoring = Director.Scoring.Events.ToList();
        var fouls = Director.Fouls.Records.ToList();
        var hash = new StableHasher().Add((long)Director.StateHash()).Add((long)StateHash()).Value;
        var id = _recordId ?? MatchHistory.NewId(DateTime.UtcNow);
        var record = new MatchRecord(id, Setup.Label, DateTime.UtcNow, Setup, outcome, score, fouls, scoring,
            _stations.Select(s => s.Stats.Finish(scoring, fouls)).ToList(), Engine.PhysicsTicksPerSecond, Math.Max(0, _matchStartTick),
            _matchStartTick < 0 ? 0 : _tick - _matchStartTick, _writer != null ? Path.GetFileName(_writer.FilePath) : null, hash, SimBuild.Id);
        _writer?.Close(new ReplayFooter(_tick, hash, outcome));
        _writer = null;
        if (Mode == SessionMode.Live)
        {
            new MatchHistory(_historyDir).Save(record);
        }
        LastRecord = record;
        MatchFinished?.Invoke(record);
    }

    private void CompareCheckpoints()
    {
        foreach (var c in _reader!.Checkpoints)
        {
            if (!_checkpoints.TryGetValue(c.Tick, out var mine) || mine != c.Hash)
            {
                if (!Mismatches.Contains(c.Tick))
                {
                    Mismatches.Add(c.Tick);
                }
            }
        }
    }

    /// <summary>Verify mode: the recorded footer (end tick, hash, outcome), once the replay has been read to its end.</summary>
    public ReplayFooter? RecordedFooter => _reader?.Footer;

    public int RecordedCheckpoints => _reader?.Checkpoints.Count ?? 0;

    // ------------------------------------------------------------------ state
    private PoseFrame PoseFrame()
    {
        static BodyPose Pose(Node3D n)
        {
            var t = n.GlobalTransform;
            var q = t.Basis.GetRotationQuaternion();
            return new BodyPose(t.Origin.X, t.Origin.Y, t.Origin.Z, q.X, q.Y, q.Z, q.W);
        }
        var live = Controller.Live;
        var m = Director.Match;
        var timer = m.Phase switch
        {
            MatchPhase.Transition => "T " + m.TransitionCountdownDisplay(_tick),
            MatchPhase.Auto or MatchPhase.Teleop => m.TimerDisplay(_tick),
            _ => m.Phase.ToString(),
        };
        return new PoseFrame(_tick, _stations.Select(s => Pose(s.Robot)).ToList(), _field.Elements.Select(e => Pose(e.Body)).ToList(),
            _field.RigidGroups.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => Pose(g.Value)).ToList(), live?.Red.Total ?? 0, live?.Blue.Total ?? 0, timer);
    }

    /// <summary>Hash of every body's pose and velocity (bit-exact floats), robots in station order, elements in field order.</summary>
    public ulong StateHash()
    {
        var h = new StableHasher();
        void Add(RigidBody3D b)
        {
            var t = b.GlobalTransform;
            foreach (var v in new[] { t.Origin, t.Basis.X, t.Basis.Y, t.Basis.Z, b.LinearVelocity, b.AngularVelocity })
            {
                h.Add(v.X).Add(v.Y).Add(v.Z);
            }
        }
        foreach (var s in _stations)
        {
            Add(s.Robot);
        }
        foreach (var e in _field.Elements)
        {
            Add(e.Body);
        }
        foreach (var b in _field.RigidGroups.Values)
        {
            Add(b);
        }
        return h.Value;
    }

    public override void _ExitTree()
    {
        if (_writer != null)
        {
            var path = _writer.FilePath;
            _writer.Dispose();
            if (!_matchPlayed)
            {
                File.Delete(path);   // practice without a MATCH: nothing to keep
            }
        }
        _reader?.Dispose();
        foreach (var s in _stations)
        {
            s.Program?.Dispose();
            if (_input != null && Mode == SessionMode.Live)
            {
                _input.RemoveSeat(s.Station.Key());
            }
        }
    }

    /// <summary>Runs after the robots and the rules engine in every physics tick: recording, statistics, results.</summary>
    private sealed partial class Tail : Node
    {
        private readonly MatchSession _session;

        public Tail(MatchSession session)
        {
            _session = session;
        }

        public override void _PhysicsProcess(double delta) => _session.EndOfTick();
    }
}
