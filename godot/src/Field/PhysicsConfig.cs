using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using YamlDotNet.Serialization;

namespace Sim.Field;

/// <summary>Physical properties of one scoring-element type.</summary>
public sealed record ElementProps(float MassKg, float RadiusM, float InertiaFactor, float Friction, float Bounce, float AngularDamp, string Source);

/// <summary>Hive hinge behaviour (config/physics.yaml <c>hive:</c>).</summary>
public sealed record HiveParams(
    float PivotFrictionTorque,
    float PivotViscousDamping,
    float EndStopRestitution,
    float DamperCoefficient,
    float DamperSpringRate,
    float DamperPreload,
    float DamperMaxForce,
    float[] DamperLeverArms,
    float[] DamperEngageDeg);

/// <summary>Reads config/physics.yaml for the Godot side: scoring elements, the contact table and
/// the hive hinge. Missing values fall back to the ASSUMED defaults in this file and are listed in
/// <see cref="AssumedValues"/>.
///
/// Contact table → Godot materials. Godot's Jolt backend combines two bodies' materials as
/// friction = min(a, b) and restitution = a + b (measured 2026-10-24, docs/notes/godot.md). So:
/// element friction = μ(foam_tile, shell), element bounce = e(shell, shell) / 2; a field surface M
/// gets friction μ(M, shell) and bounce e(M, shell) − element bounce. Element–tile, element–surface
/// and element–element restitution then match the table; element–element friction becomes
/// μ(foam_tile, shell) instead of μ(shell, shell) (documented limitation).</summary>
public sealed class PhysicsConfig
{
    // ASSUMED fallbacks, used only when config/physics.yaml lacks a value.
    private static readonly Dictionary<string, ElementProps> AssumedElements = new()
    {
        ["pollen"] = new ElementProps(0.024f, 0.0356f, 0.6667f, 0.5f, 0.25f, 0.05f, "ASSUMED (godot/src/Field/PhysicsConfig.cs)"),
        ["nectar"] = new ElementProps(0.041f, 0.0457f, 0.6667f, 0.5f, 0.25f, 0.05f, "ASSUMED (godot/src/Field/PhysicsConfig.cs)"),
    };

    // ASSUMED surface material of each pipeline group (config/cad/field.yaml) until physics.yaml
    // provides `field_surfaces`: tiles are EVA foam, perimeter and hive skins polycarbonate.
    private static readonly Dictionary<string, string> AssumedFieldSurfaces = new()
    {
        ["tiles"] = "foam_tile",
        ["tape"] = "foam_tile",
        ["perimeter"] = "polycarbonate",
        ["hive_red"] = "polycarbonate",
        ["hive_blue"] = "polycarbonate",
        ["ground"] = "default",
    };

    private const string ReferenceShell = "pollen_shell";

    private static readonly HiveParams AssumedHive = new(0.05f, 0.02f, 0.1f, 150f, 300f, 2f, 120f, new[] { 0.05f, 0.09f }, new[] { 16f, 8.9f });

    private readonly Dictionary<object, object> _root;
    private readonly Dictionary<string, ElementProps> _elements = new();
    private readonly Dictionary<string, PhysicsMaterial> _surfaces = new();

    public SortedSet<string> AssumedValues { get; } = new(StringComparer.Ordinal);

    public string SourcePath { get; }

    public HiveParams Hive { get; }

    private PhysicsConfig(string path, Dictionary<object, object> root)
    {
        SourcePath = path;
        _root = root;
        Hive = ReadHive();
    }

    public static PhysicsConfig Load(string yamlPath)
    {
        var root = new Dictionary<object, object>();
        var config = (Dictionary<object, object>?)null;
        if (System.IO.File.Exists(yamlPath))
        {
            try
            {
                config = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(System.IO.File.ReadAllText(yamlPath));
            }
            catch (Exception e)
            {
                GD.PushWarning($"cannot parse {yamlPath}: {e.Message}; using ASSUMED physics");
            }
        }
        var physics = new PhysicsConfig(yamlPath, config ?? root);
        if (config == null)
        {
            physics.AssumedValues.Add($"{yamlPath} missing or unreadable");
        }
        return physics;
    }

    /// <summary>Element type for a layout group name: nectar_red/nectar_blue → nectar.</summary>
    public static string TypeOf(string group) => group.StartsWith("nectar", StringComparison.Ordinal) ? "nectar" : group;

    public ElementProps Element(string group)
    {
        var type = TypeOf(group);
        if (_elements.TryGetValue(type, out var cached))
        {
            return cached;
        }
        var fallback = AssumedElements.TryGetValue(type, out var d) ? d : AssumedElements["pollen"];
        var shell = Text(Path("elements", type, "material", "value")) ?? $"{type}_shell";
        var tile = Pair("foam_tile", shell, "elements");
        var self = Pair(shell, shell, "elements");
        var props = new ElementProps(
            Number(Path("elements", type, "mass", "value"), fallback.MassKg, $"elements.{type}.mass"),
            Number(Path("elements", type, "radius", "value"), fallback.RadiusM, $"elements.{type}.radius"),
            Number(Path("elements", type, "inertia_factor", "value"), fallback.InertiaFactor, $"elements.{type}.inertia_factor"),
            tile?.Friction ?? fallback.Friction,
            self != null ? self.Value.Restitution / 2f : fallback.Bounce,
            Number(Path("elements", type, "angular_damping", "value"), fallback.AngularDamp, $"elements.{type}.angular_damping"),
            SourcePath);
        if (tile == null || self == null)
        {
            AssumedValues.Add($"contacts for {shell}");
        }
        _elements[type] = props;
        return props;
    }

    /// <summary>Godot material for a field group (pipeline group name) so that element contacts
    /// reproduce the physics.yaml contact table.</summary>
    public PhysicsMaterial Surface(string group)
    {
        if (_surfaces.TryGetValue(group, out var cached))
        {
            return cached;
        }
        var material = Text(Path("field_surfaces", group, "value")) ?? Text(Path("field_surfaces", group));
        if (material == null)
        {
            material = AssumedFieldSurfaces.TryGetValue(group, out var m) ? m : "default";
            AssumedValues.Add($"field surface of '{group}' = {material}");
        }
        var element = Element("pollen");
        var pair = material == "default" ? Default() : Pair(material, ReferenceShell, "surfaces") ?? Default();
        var result = new PhysicsMaterial
        {
            Friction = pair.Friction <= element.Friction ? pair.Friction : 1.0f,
            Bounce = Math.Clamp(pair.Restitution - element.Bounce, 0f, 1f),
        };
        _surfaces[group] = result;
        return result;
    }

    private HiveParams ReadHive()
    {
        var damperArms = Floats(Path("hive", "damper", "lever_arms", "value"));
        var damperEngage = Floats(Path("hive", "damper", "engage_angle", "value"));
        if (damperArms == null || damperEngage == null)
        {
            AssumedValues.Add("hive.damper geometry");
        }
        return new HiveParams(
            Number(Path("hive", "pivot_friction_torque", "value"), AssumedHive.PivotFrictionTorque, "hive.pivot_friction_torque"),
            Number(Path("hive", "pivot_viscous_damping", "value"), AssumedHive.PivotViscousDamping, "hive.pivot_viscous_damping"),
            Number(Path("hive", "end_stop_restitution", "value"), AssumedHive.EndStopRestitution, "hive.end_stop_restitution"),
            Number(Path("hive", "damper", "damping_coefficient", "value"), AssumedHive.DamperCoefficient, "hive.damper.damping_coefficient"),
            Number(Path("hive", "damper", "return_spring_rate", "value"), AssumedHive.DamperSpringRate, "hive.damper.return_spring_rate"),
            Number(Path("hive", "damper", "return_spring_preload", "value"), AssumedHive.DamperPreload, "hive.damper.return_spring_preload"),
            Number(Path("hive", "damper", "max_force", "value"), AssumedHive.DamperMaxForce, "hive.damper.max_force"),
            damperArms ?? AssumedHive.DamperLeverArms,
            damperEngage ?? AssumedHive.DamperEngageDeg);
    }

    private readonly record struct Contact(float Friction, float Restitution);

    private Contact Default()
    {
        var d = Path("contacts", "default") as Dictionary<object, object>;
        if (d == null)
        {
            AssumedValues.Add("contacts.default");
            return new Contact(0.4f, 0.2f);
        }
        return new Contact(ParseFloat(d.GetValueOrDefault("dynamic_friction")) ?? 0.4f, ParseFloat(d.GetValueOrDefault("restitution")) ?? 0.2f);
    }

    private Contact? Pair(string a, string b, string context)
    {
        if (Path("contacts", "pairs") is not List<object> pairs)
        {
            return null;
        }
        foreach (var p in pairs)
        {
            if (p is not Dictionary<object, object> row)
            {
                continue;
            }
            var ra = row.GetValueOrDefault("a")?.ToString();
            var rb = row.GetValueOrDefault("b")?.ToString();
            if ((ra == a && rb == b) || (ra == b && rb == a))
            {
                var f = ParseFloat(row.GetValueOrDefault("dynamic_friction"));
                var e = ParseFloat(row.GetValueOrDefault("restitution"));
                if (f.HasValue && e.HasValue)
                {
                    return new Contact(f.Value, e.Value);
                }
            }
        }
        return null;
    }

    private object? Path(params string[] keys)
    {
        object? node = _root;
        foreach (var k in keys)
        {
            if (node is not Dictionary<object, object> d || !d.TryGetValue(k, out node))
            {
                return null;
            }
        }
        return node;
    }

    private float Number(object? raw, float fallback, string name)
    {
        var v = ParseFloat(raw);
        if (v.HasValue)
        {
            return v.Value;
        }
        AssumedValues.Add(name);
        return fallback;
    }

    private static string? Text(object? raw) => raw is string s ? s : null;

    private static float? ParseFloat(object? raw) =>
        raw != null && float.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;

    private static float[]? Floats(object? raw)
    {
        if (raw is not List<object> list)
        {
            return null;
        }
        var result = new float[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            var v = ParseFloat(list[i]);
            if (!v.HasValue)
            {
                return null;
            }
            result[i] = v.Value;
        }
        return result;
    }
}
