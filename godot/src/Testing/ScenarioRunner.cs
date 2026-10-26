using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;
using Sim.App;
using Sim.Field;
using Sim.Robot;
using YamlDotNet.Serialization;

namespace Sim.Testing;

/// <summary>Headless scenario tests (spec 12 layer 4): <c>godot --headless --fixed-fps 240 --path godot
/// -- --run-scenarios [FILTER]</c>. Each tests/scenarios/*.yaml loads a layout, optionally spawns
/// elements, steps physics for duration_s at the project tick rate and evaluates its checks.
/// Exit code 1 if any scenario fails. Results: reports/scenarios/results.json.</summary>
public partial class ScenarioRunner : Node3D
{
    private readonly Queue<(string File, Dictionary<object, object> Def)> _queue = new();
    private readonly List<Dictionary<string, object>> _results = new();
    private Dictionary<object, object>? _current;
    private string _currentFile = "";
    private LoadedField? _field;
    private PhysicsConfig _physics = null!;
    private int _step;
    private int _targetSteps;
    private int _cooldown;
    private ulong _startUsec;
    private int _failures;
    private readonly List<(int Step, Dictionary<object, object> Action)> _pendingActions = new();
    private RobotBody? _robot;
    private readonly Dictionary<FieldElement, float> _peakHeight = new();

    public string Filter { get; set; } = "";

    public override void _Ready()
    {
        _physics = PhysicsConfig.Load(RepoPaths.Config("physics.yaml"));
        var files = System.IO.Directory.Exists(RepoPaths.Scenarios)
            ? System.IO.Directory.GetFiles(RepoPaths.Scenarios, "*.yaml").OrderBy(f => f, StringComparer.Ordinal).ToList()
            : new List<string>();
        var deserializer = new DeserializerBuilder().Build();
        foreach (var file in files)
        {
            var def = deserializer.Deserialize<Dictionary<object, object>>(System.IO.File.ReadAllText(file));
            var name = Str(def, "name", System.IO.Path.GetFileNameWithoutExtension(file));
            if (Filter.Length == 0 || name.Contains(Filter, StringComparison.Ordinal))
            {
                _queue.Enqueue((file, def));
            }
        }
        GD.Print($"scenario runner: {_queue.Count} scenario(s), physics {Engine.PhysicsTicksPerSecond} Hz");
        if (_physics.AssumedValues.Count > 0)
        {
            GD.Print("ASSUMED physics values: " + string.Join(", ", _physics.AssumedValues));
        }
        StartNext();
    }

    /// <summary>The current scenario runs on stand-in assets (no CAD pipeline output).</summary>
    private bool _standIn;

    private void StartNext()
    {
        while (_queue.Count > 0)
        {
            var (file, def) = _queue.Dequeue();
            var name = Str(def, "name", file);
            var layoutPath = RepoPaths.Layout(Str(def, "layout", "field"));
            if (!FieldLoader.LayoutExists(layoutPath))
            {
                Record(name, "SKIP", new List<string> { $"layout {layoutPath} not generated" }, 0);
                continue;
            }
            // Scenarios that measure the CAD's shapes (hive tipping, intake geometry) need the pipeline output; the
            // stand-in assets (tools/standin) only stand in for match flow, rules and code.
            _standIn = RepoPaths.IsStandIn(layoutPath);
            if (_standIn && Str(def, "requires_cad", "false") == "true")
            {
                Record(name, "SKIP", new List<string> { "needs the CAD pipeline output (stand-in assets loaded)" }, 0);
                continue;
            }
            _current = def;
            _currentFile = file;
            _field = FieldLoader.Load(this, layoutPath, _physics);
            if (Str(def, "mechanisms", "false") == "true")
            {
                // Hive hinges, dampers and tip detection (config/field_mechanisms.yaml joints, Phase 4).
                FieldMechanisms.Apply(_field, RepoPaths.Config("field_mechanisms.yaml"), _physics,
                    RepoPaths.FieldManifest(layoutPath));
            }
            AprilTagDecals.Build(_field, RepoPaths.Config("field_mechanisms.yaml"));
            _step = 0;
            _targetSteps = (int)Math.Round(Num(def, "duration_s", 5.0) * Engine.PhysicsTicksPerSecond);
            _pendingActions.Clear();
            foreach (var a in List(def, "actions"))
            {
                var action = (Dictionary<object, object>)a;
                _pendingActions.Add(((int)Math.Round(Num(action, "at_s", 0.0) * Engine.PhysicsTicksPerSecond), action));
            }
            _startUsec = Time.GetTicksUsec();
            return;
        }
        Finish();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_current == null)
        {
            if (_cooldown > 0 && --_cooldown == 0)
            {
                StartNext();
            }
            return;
        }
        foreach (var (step, action) in _pendingActions.Where(p => p.Step == _step).ToList())
        {
            RunAction(action);
        }
        foreach (var e in _field!.Elements)
        {
            var y = e.Body.GlobalPosition.Y - _field.TileTopY;
            _peakHeight[e] = Math.Max(_peakHeight.GetValueOrDefault(e, y), y);
        }
        Trace();
        _step++;
        if (_step < _targetSteps)
        {
            return;
        }
        var messages = new List<string>();
        var ok = true;
        foreach (var c in List(_current, "checks"))
        {
            var (pass, message) = Evaluate((Dictionary<object, object>)c);
            ok &= pass;
            messages.Add($"{(pass ? "ok  " : "FAIL")} {message}");
        }
        var wallMs = (Time.GetTicksUsec() - _startUsec) / 1000.0;
        Record(Str(_current, "name", _currentFile), ok ? "PASS" : "FAIL", messages, wallMs);
        _field!.Root.QueueFree();
        _field = null;
        _robot?.QueueFree();
        _robot = null;
        _peakHeight.Clear();
        _current = null;
        _cooldown = 3;
    }

    private void RunAction(Dictionary<object, object> action)
    {
        var kind = Str(action, "action", "");
        switch (kind)
        {
            case "spawn":
                // Spawns COUNT elements of GROUP on a grid around CENTER (spacing_m apart, layered upward).
                var group = Str(action, "group", "pollen");
                var count = (int)Num(action, "count", 1);
                var center = Vec(action, "center");
                if (Str(action, "relative_to", "field") == "robot")
                {
                    center = (_robot ?? throw new InvalidOperationException("spawn relative_to robot needs a robot")).GlobalTransform * center;
                }
                var spacing = (float)Num(action, "spacing_m", 0.08);
                var perRow = Math.Max(1, (int)Num(action, "per_row", 3));
                for (var i = 0; i < count; i++)
                {
                    var layer = i / (perRow * perRow);
                    var inLayer = i % (perRow * perRow);
                    var offset = count == 1 ? Vector3.Zero : new Vector3(   // a single element goes exactly to CENTER
                        (inLayer % perRow - (perRow - 1) / 2f) * spacing,
                        layer * spacing,
                        (inLayer / perRow - (perRow - 1) / 2f) * spacing);
                    FieldLoader.SpawnElement(_field!, group, new Transform3D(Basis.Identity, center + offset));
                }
                break;
            case "robot":
            {
                // Spawns robot NAME at POSITION (y above the ground) turned YAW_DEG about +Y (0 = facing −Z), driven by
                // a direct-drive script: drive: [{at_s, forward, strafe, rotate}, ...] (seconds since the scenario start).
                var position = Vec(action, "position") + new Vector3(0, _field!.TileTopY, 0);
                var yaw = Mathf.DegToRad((float)Num(action, "yaw_deg", 0));
                _robot = RobotBody.Spawn(this, Str(action, "name", "gobilda_starter"), new Transform3D(new Basis(Vector3.Up, yaw), position), _physics);
                var script = List(action, "drive").Cast<Dictionary<object, object>>()
                    .Select(d => ((int)Math.Round(Num(d, "at_s", 0) * Engine.PhysicsTicksPerSecond), d))
                    .ToList();
                _robot.Driver = new ScriptedDrive(script, _step);
                _robot.Elements = _field.Elements;
                break;
            }
            default:
                throw new InvalidOperationException($"unknown scenario action '{kind}'");
        }
    }

    /// <summary>
    /// Direct drive from a scenario script. Each entry (at_s) sets some of: forward/strafe/rotate (mecanum mixing),
    /// intake (power on the intake motor and both corner servos, as the example TeleOp), launcher (ticks/s, velocity
    /// mode with the example's PIDF), windmill (CR servo power). Any robot: power {motor: power}, velocity {motor:
    /// ticks/s, PIDF from the robot YAML}, servo {servo: position}. Values hold until an entry changes them.
    /// </summary>
    private sealed class ScriptedDrive : IRobotDriver
    {
        private static readonly Sim.Hardware.Pidf LauncherPidf = new(40, 0, 0, 12.5);
        private readonly List<(int Step, Dictionary<object, object> Entry)> _script;
        private readonly Dictionary<string, double> _state = new();
        private readonly Dictionary<string, double> _power = new();
        private readonly Dictionary<string, double> _velocity = new();
        private readonly Dictionary<string, double> _servo = new();
        private readonly int _startStep;

        public ScriptedDrive(List<(int Step, Dictionary<object, object> Entry)> script, int startStep)
        {
            _script = script;
            _startStep = startStep;
        }

        public void BeforeStep(RobotBody robot, long tick)
        {
            var now = _startStep + (int)tick;
            foreach (var (_, entry) in _script.Where(s => s.Step == now))
            {
                foreach (var key in new[] { "forward", "strafe", "rotate", "intake", "launcher", "windmill" })
                {
                    if (entry.ContainsKey(key))
                    {
                        _state[key] = Num(entry, key, 0);
                    }
                }
                foreach (var (key, target) in new[] { ("power", _power), ("velocity", _velocity), ("servo", _servo) })
                {
                    if (entry.TryGetValue(key, out var raw) && raw is Dictionary<object, object> map)
                    {
                        foreach (var (name, _) in map)
                        {
                            target[name.ToString()!] = Num(map, name.ToString()!, 0);
                        }
                    }
                }
            }
            foreach (var (name, power) in _power)
            {
                robot.Powertrain.SetPowerFromSdk(name, power);
            }
            foreach (var (name, velocity) in _velocity)
            {
                var pidf = robot.Definition.Device(name).Pidf is { Length: 4 } k ? new Sim.Hardware.Pidf(k[0], k[1], k[2], k[3]) : LauncherPidf;
                robot.Powertrain.SetVelocityFromSdk(name, velocity, pidf);
            }
            foreach (var (name, position) in _servo)
            {
                robot.Powertrain.SetServoFromSdk(name, position);
            }
            var p = robot.Powertrain;
            if (_state.ContainsKey("forward") || _state.ContainsKey("strafe") || _state.ContainsKey("rotate"))
            {
                p.MecanumDrive(_state.GetValueOrDefault("forward"), _state.GetValueOrDefault("strafe"), _state.GetValueOrDefault("rotate"));
            }
            if (_state.TryGetValue("intake", out var intake))
            {
                p.SetPowerFromSdk("intake", intake);
                p.SetCrServoFromSdk("left_intake_servo", intake);
                p.SetCrServoFromSdk("right_intake_servo", intake);
            }
            if (_state.TryGetValue("launcher", out var launcher))
            {
                p.SetVelocityFromSdk("launcher", launcher, LauncherPidf);
            }
            if (_state.TryGetValue("windmill", out var windmill))
            {
                p.SetCrServoFromSdk("windmillServo", windmill);
            }
        }

        public void AfterStep(RobotBody robot, long tick)
        {
        }
    }

    private (bool Pass, string Message) Evaluate(Dictionary<object, object> c)
    {
        var field = _field!;
        var elements = field.Elements;
        var check = Str(c, "check", "");
        switch (check)
        {
            case "element_count":
            {
                var expected = (int)Num(c, "equals", 0);
                return (elements.Count == expected, $"element_count = {elements.Count} (expected {expected})");
            }
            case "elements_above_ground":
            {
                var margin = (float)Num(c, "margin_m", 0.01);
                var below = elements.Where(e => e.Body.GlobalPosition.Y < field.GroundY - margin).ToList();
                return (below.Count == 0, $"elements below ground-{margin} m: {below.Count}{Names(below)}");
            }
            case "elements_within":
            {
                var min = Vec(c, "min");
                var max = Vec(c, "max");
                var outside = elements.Where(e => !Inside(e.Body.GlobalPosition, min, max)).ToList();
                return (outside.Count == 0, $"elements outside [{min} .. {max}]: {outside.Count}{Names(outside)}");
            }
            case "elements_max_displacement":
            {
                var limit = (float)Num(c, "meters", 0.15);
                var moved = elements.Where(e => e.Body.GlobalPosition.DistanceTo(e.StartPosition) > limit).ToList();
                var worst = elements.Count == 0 ? 0f : elements.Max(e => e.Body.GlobalPosition.DistanceTo(e.StartPosition));
                return (moved.Count == 0, $"elements moved > {limit} m: {moved.Count} (max {worst:F3} m){Names(moved)}");
            }
            case "elements_at_rest":
            {
                var lin = (float)Num(c, "linear_mps", 0.02);
                var ang = (float)Num(c, "angular_rps", 0.5);
                var moving = elements.Where(e => !e.Body.Sleeping &&
                    (e.Body.LinearVelocity.Length() > lin || e.Body.AngularVelocity.Length() > ang)).ToList();
                return (moving.Count == 0, $"elements still moving (> {lin} m/s or > {ang} rad/s): {moving.Count}{Names(moving)}");
            }
            case "element_in_box":
            {
                var group = Str(c, "group", "");
                var index = (int)Num(c, "index", 0);
                var matching = elements.Where(e => e.Group == group).ToList();
                if (index >= matching.Count)
                {
                    return (false, $"element {group}[{index}] does not exist");
                }
                var p = matching[index].Body.GlobalPosition;
                var min = Vec(c, "min");
                var max = Vec(c, "max");
                return (Inside(p, min, max), $"{group}[{index}] at {p} inside [{min} .. {max}]");
            }
            case "body_rotation":
            {
                // Rotation angle of a rigid group about AXIS since load, in degrees.
                var name = Str(c, "body", "");
                if (!field.RigidGroups.TryGetValue(name, out var body))
                {
                    return (false, $"rigid group '{name}' not found");
                }
                var axis = Vec(c, "axis").Normalized();
                var deg = Mathf.RadToDeg(Angles.SignedAngle(field.RigidStartBasis[name], body.GlobalBasis, axis));
                var lo = (float)Num(c, "min_deg", -1e9);
                var hi = (float)Num(c, "max_deg", 1e9);
                return (deg >= lo && deg <= hi, $"{name} rotation about {axis} = {deg:F2}° (expected {lo}..{hi})");
            }
            case "element_in_robot":
            {
                // Element GROUP[INDEX] inside a box in the robot frame (+X right, +Y up, −Z forward).
                if (_robot == null)
                {
                    return (false, "no robot in this scenario");
                }
                var group = Str(c, "group", "");
                var index = (int)Num(c, "index", 0);
                var matching = elements.Where(e => e.Group == group).ToList();
                index = index < 0 ? matching.Count + index : index;   // -1 = the last one (e.g. spawned by the scenario)
                if (index < 0 || index >= matching.Count)
                {
                    return (false, $"element {group}[{index}] does not exist");
                }
                var local = _robot.GlobalTransform.AffineInverse() * matching[index].Body.GlobalPosition;
                var min = Vec(c, "min");
                var max = Vec(c, "max");
                return (Inside(local, min, max), $"{group}[{index}] at {local} (robot frame) inside [{min} .. {max}]");
            }
            case "element_peak_height":
            case "element_peak_max":
            {
                var group = Str(c, "group", "");
                var index = (int)Num(c, "index", 0);
                var matching = elements.Where(e => e.Group == group).ToList();
                index = index < 0 ? matching.Count + index : index;
                if (index < 0 || index >= matching.Count)
                {
                    return (false, $"element {group}[{index}] does not exist");
                }
                var peak = _peakHeight.GetValueOrDefault(matching[index]);
                if (check == "element_peak_max")
                {
                    var max = Num(c, "max_m", 1e9);
                    return (peak <= max, $"{group}[{index}] peak height {peak:F3} m (max {max})");
                }
                var min = Num(c, "min_m", 0);
                return (peak >= min, $"{group}[{index}] peak height {peak:F3} m (min {min})");
            }
            case "robot_position":
            {
                // Robot origin (on the floor, between the wheels); y = height above the surface under it (tile top).
                if (_robot == null)
                {
                    return (false, "no robot in this scenario");
                }
                var origin = _robot.GlobalPosition;
                var down = PhysicsRayQueryParameters3D.Create(origin + Vector3.Up * 0.5f, origin + Vector3.Down * 0.5f, CollisionLayers.FieldStatic,
                    new Godot.Collections.Array<Rid> { _robot.GetRid() });
                var hit = _robot.GetWorld3D().DirectSpaceState.IntersectRay(down);
                var surface = hit.Count > 0 ? ((Vector3)hit["position"]).Y : field.GroundY;
                var p = new Vector3(origin.X, origin.Y - surface, origin.Z);
                var min = Vec(c, "min");
                var max = Vec(c, "max");
                return (Inside(p, min, max), $"robot at {p} inside [{min} .. {max}]");
            }
            case "arm_lowest_point":
            {
                // Lowest collider point of a robot CAD group (an arm), height above the tile top, m.
                if (_robot == null)
                {
                    return (false, "no robot in this scenario");
                }
                var group = Str(c, "group", "");
                if (_robot.LowestPoint(group) is not { } low)
                {
                    return (false, $"robot has no collider group '{group}'");
                }
                var y = low.Y - field.TileTopY;
                var lo = Num(c, "min_m", -1e9);
                var hi = Num(c, "max_m", 1e9);
                return (y >= lo && y <= hi, $"{group} lowest point {y:F4} m above the tiles (expected {lo}..{hi})");
            }
            case "robot_upright":
            {
                if (_robot == null)
                {
                    return (false, "no robot in this scenario");
                }
                var tilt = Mathf.RadToDeg(_robot.GlobalBasis.Y.Normalized().AngleTo(Vector3.Up));
                var limit = Num(c, "max_tilt_deg", 2);
                return (tilt <= limit, $"robot tilt {tilt:F2}° (max {limit}°)");
            }
            case "robot_yaw":
            {
                if (_robot == null)
                {
                    return (false, "no robot in this scenario");
                }
                var forward = -_robot.GlobalBasis.Z;
                var yaw = Mathf.RadToDeg(Mathf.Atan2(-forward.X, -forward.Z));   // 0 = facing −Z, CCW from above positive
                var lo = Num(c, "min_deg", -180);
                var hi = Num(c, "max_deg", 180);
                return (yaw >= lo && yaw <= hi, $"robot yaw {yaw:F2}° (expected {lo}..{hi})");
            }
            case "robot_at_rest":
            {
                if (_robot == null)
                {
                    return (false, "no robot in this scenario");
                }
                var lin = Num(c, "linear_mps", 0.01);
                var v = _robot.LinearVelocity.Length();
                return (v <= lin, $"robot speed {v:F4} m/s (max {lin})");
            }
            default:
                return (false, $"unknown check '{check}'");
        }
    }

    /// <summary>
    /// trace: {group, index, every_s}: prints the element's position and velocity in the robot frame and the mechanism
    /// speeds (intake roller rad/s, launcher ticks/s, windmill degrees) to diagnose ball paths.
    /// </summary>
    private void Trace()
    {
        if (_current == null || _robot == null || !_current.TryGetValue("trace", out var raw) || raw is not Dictionary<object, object> trace)
        {
            return;
        }
        var every = Math.Max(1, (int)Math.Round(Num(trace, "every_s", 0.1) * Engine.PhysicsTicksPerSecond));
        if (_step % every != 0)
        {
            return;
        }
        var group = Str(trace, "group", "pollen");
        var matching = _field!.Elements.Where(e => e.Group == group).ToList();
        var index = (int)Num(trace, "index", -1);
        index = index < 0 ? matching.Count + index : index;
        if (index < 0 || index >= matching.Count)
        {
            return;
        }
        var body = matching[index].Body;
        var inv = _robot.GlobalTransform.AffineInverse();
        var local = inv * body.GlobalPosition;
        var v = inv.Basis * body.LinearVelocity;
        var hw = _robot.Powertrain.Hardware;
        string Motor(string name) => hw.Motors.FirstOrDefault(m => m.Device.Name == name) is { } m ? $"{m.OutputOmega:F1}" : "-";
        var servos = string.Join(" ", hw.Servos.Select(sv => $"{sv.Device.Name}={sv.Model.AngleDeg:F0}°"));
        var motors = string.Join(" ", hw.Motors.Where(m => m.LoadInertia != null).Select(m => $"{m.Device.Name}={Motor(m.Device.Name)}rad/s"));
        var stored = _robot.StoredElements.Count();
        GD.Print($"    t={_step / (double)Engine.PhysicsTicksPerSecond:F2}s ball=({local.X:F3},{local.Y:F3},{local.Z:F3}) v=({v.X:F2},{v.Y:F2},{v.Z:F2}) " +
                 $"stored={stored} {motors} {servos}" +
                 (_robot.LastLaunch is { } l ? $" last launch {l.Group} ideal {l.IdealSpeed:F2} exit {l.ExitSpeed:F2} m/s" : ""));
    }

    private void Record(string name, string status, List<string> messages, double wallMs)
    {
        if (status == "FAIL")
        {
            _failures++;
        }
        var assets = _standIn && status != "SKIP" ? ", stand-in assets" : "";
        GD.Print($"[{status}] {name} ({wallMs:F0} ms wall{assets})");
        foreach (var m in messages)
        {
            GD.Print($"    {m}");
        }
        _results.Add(new Dictionary<string, object>
        {
            ["name"] = name, ["status"] = status, ["wall_ms"] = Math.Round(wallMs), ["stand_in"] = _standIn && status != "SKIP", ["messages"] = messages,
        });
    }

    private void Finish()
    {
        var dir = RepoPaths.Reports("scenarios");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "results.json"),
            JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
        var passed = _results.Count(r => (string)r["status"] == "PASS");
        var skipped = _results.Count(r => (string)r["status"] == "SKIP");
        GD.Print($"scenarios: {passed} passed, {_failures} failed, {skipped} skipped");
        GetTree().Quit(_failures > 0 ? 1 : 0);
    }

    private static bool Inside(Vector3 p, Vector3 min, Vector3 max) =>
        p.X >= min.X && p.Y >= min.Y && p.Z >= min.Z && p.X <= max.X && p.Y <= max.Y && p.Z <= max.Z;

    private static string Names(List<FieldElement> list) =>
        list.Count == 0 ? "" : " [" + string.Join(", ", list.Take(6).Select(e => $"{e.Body.Name}@{e.Body.GlobalPosition}")) + (list.Count > 6 ? ", ..." : "") + "]";

    private static string Str(Dictionary<object, object> d, string key, string def) =>
        d.TryGetValue(key, out var v) && v != null ? v.ToString() ?? def : def;

    private static double Num(Dictionary<object, object> d, string key, double def) =>
        d.TryGetValue(key, out var v) && v != null ? double.Parse(v.ToString()!, CultureInfo.InvariantCulture) : def;

    private static List<object> List(Dictionary<object, object> d, string key) =>
        d.TryGetValue(key, out var v) && v is List<object> l ? l : new List<object>();

    private static Vector3 Vec(Dictionary<object, object> d, string key)
    {
        var l = List(d, key);
        if (l.Count != 3)
        {
            throw new InvalidOperationException($"'{key}' must be a list of 3 numbers");
        }
        float F(object o) => float.Parse(o.ToString()!, CultureInfo.InvariantCulture);
        return new Vector3(F(l[0]), F(l[1]), F(l[2]));
    }
}
