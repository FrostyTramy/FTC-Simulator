using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Numerics;
using Sim.Field;
using Sim.Game;
using Sim.Game.Field;
using Sim.Game.Geometry;
using Sim.Game.Match;
using Sim.Game.Scoring;
using Sim.Robot;

namespace Sim.App;

/// <summary>A robot taking part in the match (spec 08 "Match setup").</summary>
public sealed record MatchRobot(int Id, Alliance Alliance, string Team, RobotBody Body);

/// <summary>
/// Runs the BIOBUZZ rules engine (<see cref="GameDirector"/>: match timing, scoring, fouls, human player) on the Godot
/// world. Every physics tick it builds a <see cref="WorldSnapshot"/>: elements (position, velocity, CONTROL by a robot =
/// inside its footprint and off the floor for control_support_min_time, physics.yaml ASSUMED), robots (footprint,
/// height, wall and robot contact, powered motion) and hive hinge angles. Without a running match it still reports the
/// live score of the field (practice).
/// </summary>
public partial class MatchController : Node
{
    private readonly LoadedField _field;
    private readonly List<MatchRobot> _robots;
    private readonly Dictionary<RigidBody3D, int> _ids = new();
    private readonly Dictionary<RigidBody3D, (int Robot, long Since)> _support = new();
    private readonly Dictionary<RigidBody3D, int> _lastControl = new();
    private readonly GameRules _rules;
    private readonly FieldGeometry _geometry;
    private readonly double _controlSeconds;
    private readonly PhysicsConfig _physics;
    private readonly Dictionary<string, double> _radii = new();
    private int _seenEvents;
    private long _tick;

    public MatchController(LoadedField field, IEnumerable<MatchRobot> robots, PhysicsConfig physics)
    {
        _field = field;
        _robots = robots.ToList();
        _rules = GameRules.Load(RepoPaths.Config("rules_biobuzz.yaml"));
        _geometry = FieldGeometry.Load(RepoPaths.Config("field_mechanisms.yaml"));
        _controlSeconds = physics.ControlSupportSeconds();
        _physics = physics;
        Director = NewDirector();
    }

    public GameDirector Director { get; private set; }
    public WorldSnapshot? Snapshot { get; private set; }
    public ScoreSheet? Live { get; private set; }
    public long Tick => _tick;
    public IReadOnlyList<MatchRobot> Robots => _robots;

    /// <summary>The DS 30-second AUTO timer ran out (Table 9-1): drive teams' Autonomous OpModes must stop.</summary>
    public event Action? AutoEnded;

    /// <summary>Starts a new MATCH now (a fresh rules engine: score, fouls and timer from zero).</summary>
    public void StartMatch()
    {
        Director = NewDirector();
        _seenEvents = 0;
        Director.StartMatch(_tick);
    }

    public void Abort() => Director.Abort();

    /// <summary>Back to practice: no match, robots enabled, live score of the field.</summary>
    public void Practice()
    {
        Director = NewDirector();
        _seenEvents = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        var t0 = TickProfiler.Start();
        Snapshot = Build(_tick);
        TickProfiler.Stop("snapshot", t0);
        var t1 = TickProfiler.Start();
        Director.Step(Snapshot);
        TickProfiler.Stop("rules", t1);
        if (_tick % 24 == 0 || Director.FinalScore != null)   // the display needs 10 Hz, not 240 Hz
        {
            var t2 = TickProfiler.Start();
            Live = Director.LiveScore(Snapshot);
            TickProfiler.Stop("score", t2);
        }
        var events = Director.Log.Events;
        for (; _seenEvents < events.Count; _seenEvents++)
        {
            if (events[_seenEvents] is StopAutoOpMode)
            {
                AutoEnded?.Invoke();
            }
        }
        // Field control (spec 03 Assisted mode): outside practice the rules engine decides when outputs are enabled.
        var match = Director.Match;
        var disabled = match.Phase != MatchPhase.PreMatch && !match.OutputsEnabled;
        foreach (var r in _robots)
        {
            r.Body.FieldDisabled = disabled;
        }
        _tick++;
    }

    private GameDirector NewDirector() =>
        new(_rules, _geometry, new GameOptions(TickRate: Engine.PhysicsTicksPerSecond));

    private WorldSnapshot Build(long tick)
    {
        var ground = _field.TileTopY;   // the sim frame's y = 0 is the tile top
        var footprints = _robots.ToDictionary(r => r.Id, r => Footprint(r.Body));
        var controlled = new Dictionary<int, List<int>>();
        var elements = new List<ElementState>(_field.Elements.Count);
        foreach (var e in _field.Elements)
        {
            var body = e.Body;
            if (!GodotObject.IsInstanceValid(body))
            {
                continue;
            }
            if (!_ids.TryGetValue(body, out var id))
            {
                id = _ids.Count + 1;
                _ids[body] = id;
            }
            var p = body.GlobalPosition;
            if (!_radii.TryGetValue(e.Group, out var radius))
            {
                radius = _radii[e.Group] = _physics.Element(e.Group).RadiusM;   // physics.yaml elements.*.radius
            }
            int? controller = null;
            var lifted = p.Y - ground - radius > 0.005f;
            var holder = lifted ? _robots.FirstOrDefault(r => footprints[r.Id].Contains(new Vec2(p.X, p.Z))) : null;
            if (holder != null)
            {
                var since = _support.TryGetValue(body, out var s) && s.Robot == holder.Id ? s.Since : tick;
                _support[body] = (holder.Id, since);
                if ((tick - since) >= _controlSeconds * Engine.PhysicsTicksPerSecond)
                {
                    controller = holder.Id;
                    _lastControl[body] = holder.Id;
                    (controlled.TryGetValue(holder.Id, out var list) ? list : controlled[holder.Id] = new List<int>()).Add(id);
                }
            }
            else
            {
                _support.Remove(body);
            }
            var onField = Math.Abs(p.X) <= _geometry.WallXMax && Math.Abs(p.Z) <= _geometry.WallZMax && p.Y > ground - 0.2f;
            elements.Add(new ElementState(id, Kind(e.Group), V(p - new Vector3(0, ground, 0)), radius, V(body.LinearVelocity),
                controller, _lastControl.TryGetValue(body, out var last) ? last : null, onField));
        }

        var robots = new List<RobotState>(_robots.Count);
        foreach (var r in _robots)
        {
            var fp = footprints[r.Id];
            var body = r.Body;
            var touchingWall = fp.Vertices.Any(v => v.X <= _geometry.WallXMin + 0.005 || v.X >= _geometry.WallXMax - 0.005
                                                    || v.Z <= _geometry.WallZMin + 0.005 || v.Z >= _geometry.WallZMax - 0.005);
            var contacts = _robots.Where(o => o.Id != r.Id && footprints[o.Id].DistanceTo(fp) < 0.005).Select(o => o.Id).ToList();
            var powered = body.Powertrain.Hardware.Motors.Any(m => Math.Abs(m.Duty) > 0.01 && Math.Abs(m.OutputOmega) > 0.5)
                          || body.Powertrain.Hardware.Servos.Any(s => s.Model.Enabled && Math.Abs(s.Model.SpeedDegPerS) > 5);
            robots.Add(new RobotState(r.Id, r.Alliance, r.Team, fp, body.GlobalPosition.Y - ground + body.TopLocal, V(body.LinearVelocity),
                body.PlanarMotion().YawRate, touchingWall, controlled.TryGetValue(r.Id, out var mine) ? mine : new List<int>(), contacts, powered));
        }

        var hives = new List<HiveState>();
        foreach (var (name, hinge) in _field.Mechanisms)
        {
            var alliance = name.Contains("red", StringComparison.Ordinal) ? Alliance.Red : Alliance.Blue;
            hives.Add(new HiveState(alliance, Mathf.RadToDeg(hinge.AngleRad), Mathf.RadToDeg(hinge.AngularSpeed)));
        }
        foreach (var alliance in new[] { Alliance.Red, Alliance.Blue }.Where(a => hives.All(h => h.Alliance != a)))
        {
            hives.Add(new HiveState(alliance, 0));   // hinge not simulated: hive held in its CAD pose
        }
        return new WorldSnapshot(tick, elements, robots, hives, Array.Empty<PinContact>());
    }

    private static Polygon2 Footprint(RobotBody robot)
    {
        var t = robot.GlobalTransform;
        return new Polygon2(robot.FootprintLocal.Select(p =>
        {
            var w = t * new Vector3(p.X, 0, p.Y);
            return new Vec2(w.X, w.Z);
        }));
    }

    private static ElementKind Kind(string group) => group switch
    {
        "nectar_red" => ElementKind.NectarRed,
        "nectar_blue" => ElementKind.NectarBlue,
        _ => ElementKind.Pollen,
    };

    private static Vec3 V(Vector3 v) => new(v.X, v.Y, v.Z);
}
