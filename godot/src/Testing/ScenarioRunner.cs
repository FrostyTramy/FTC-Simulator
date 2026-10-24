using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;
using Sim.App;
using Sim.Field;
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
            _current = def;
            _currentFile = file;
            _field = FieldLoader.Load(this, layoutPath, _physics);
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
                var spacing = (float)Num(action, "spacing_m", 0.08);
                var perRow = Math.Max(1, (int)Num(action, "per_row", 3));
                for (var i = 0; i < count; i++)
                {
                    var layer = i / (perRow * perRow);
                    var inLayer = i % (perRow * perRow);
                    var offset = new Vector3(
                        (inLayer % perRow - (perRow - 1) / 2f) * spacing,
                        layer * spacing,
                        (inLayer / perRow - (perRow - 1) / 2f) * spacing);
                    FieldLoader.SpawnElement(_field!, group, new Transform3D(Basis.Identity, center + offset));
                }
                break;
            default:
                throw new InvalidOperationException($"unknown scenario action '{kind}'");
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
            default:
                return (false, $"unknown check '{check}'");
        }
    }

    private void Record(string name, string status, List<string> messages, double wallMs)
    {
        if (status == "FAIL")
        {
            _failures++;
        }
        GD.Print($"[{status}] {name} ({wallMs:F0} ms wall)");
        foreach (var m in messages)
        {
            GD.Print($"    {m}");
        }
        _results.Add(new Dictionary<string, object> { ["name"] = name, ["status"] = status, ["wall_ms"] = Math.Round(wallMs), ["messages"] = messages });
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
