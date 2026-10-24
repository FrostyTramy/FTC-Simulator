using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;
using YamlDotNet.Serialization;

namespace Sim.Field;

/// <summary>Applies the joints of a mechanisms YAML (config/field_mechanisms.yaml format) to a
/// loaded layout: the rigid group becomes a dynamic body on a HingeJoint3D at the configured anchor
/// with the configured angle range, plus pivot friction, end dampers and TIP detection
/// (<see cref="HingeMechanism"/>). Hinge bodies do not collide with body_a (the static field group
/// that holds the frame and axle holders): the pivot parts overlap by design.</summary>
public static class FieldMechanisms
{
    public static void Apply(LoadedField field, string yamlPath, PhysicsConfig physics, string? manifestPath)
    {
        var root = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(System.IO.File.ReadAllText(yamlPath));
        var bodies = (root.GetValueOrDefault("bodies") as List<object> ?? new List<object>())
            .OfType<Dictionary<object, object>>()
            .ToDictionary(b => b["id"].ToString()!, b => b);
        var tipEngageDeg = Num(Value(Get(root, "tip_detection", "damper_engage")), 2.0);
        var manifest = manifestPath != null && System.IO.File.Exists(manifestPath) ? ReadManifest(manifestPath) : null;

        foreach (var j in (root.GetValueOrDefault("joints") as List<object> ?? new List<object>()).OfType<Dictionary<object, object>>())
        {
            if (j.GetValueOrDefault("type")?.ToString() != "hinge")
            {
                continue;
            }
            var id = j["id"].ToString()!;
            var bodyId = j["body_b"].ToString()!;
            if (!field.RigidGroups.TryGetValue(bodyId, out var body))
            {
                GD.PushWarning($"joint {id}: rigid group '{bodyId}' not in layout {field.Layout.Name}");
                continue;
            }
            var bodyCfg = bodies.GetValueOrDefault(bodyId) ?? new Dictionary<object, object>();
            SetMassProperties(body, bodyId, bodyCfg, manifest, physics);

            var anchor = Vec(Value(j["anchor"]));
            var axis = Vec(Value(j["axis"])).Normalized();
            var range = Floats(Value(j["angle_range"]));
            var stable = Value(j.GetValueOrDefault("stable_states")) as Dictionary<object, object>;
            var staticBody = field.StaticBodies.FirstOrDefault(b => b.Name == j.GetValueOrDefault("body_a")?.ToString());

            var mech = new HingeMechanism
            {
                Name = id,
                Body = body,
                Axis = axis,
                Anchor = anchor,
                LowerRad = Mathf.DegToRad(range[0]),
                UpperRad = Mathf.DegToRad(range[1]),
                TipEngageRad = Mathf.DegToRad((float)tipEngageDeg),
                Params = physics.Hive,
                StateNames = stable == null
                    ? new[] { "lower", "upper" }
                    : stable.OrderBy(kv => Num(kv.Value, 0)).Select(kv => kv.Key.ToString()!).ToArray(),
            };
            field.Root.AddChild(mech);
            mech.CreateJoint(staticBody);
            field.Mechanisms[id] = mech;
            body.Freeze = false;
        }
    }

    /// <summary>Mass, centre of mass and inertia: numeric <c>mass_kg</c> (+ optional
    /// <c>center_of_mass</c>) from the YAML, or "auto" = sum of the CAD parts of the group in the
    /// pipeline manifest (volume × material density, parts approximated as solid boxes).</summary>
    private static void SetMassProperties(RigidBody3D body, string bodyId, Dictionary<object, object> cfg, List<ManifestPart>? manifest, PhysicsConfig physics)
    {
        var massRaw = cfg.GetValueOrDefault("mass_kg")?.ToString() ?? "auto";
        if (massRaw != "auto")
        {
            body.Mass = float.Parse(massRaw, CultureInfo.InvariantCulture);
            if (cfg.GetValueOrDefault("center_of_mass") is List<object> com)
            {
                body.CenterOfMassMode = RigidBody3D.CenterOfMassModeEnum.Custom;
                body.CenterOfMass = body.ToLocal(Vec(com));
            }
            return;
        }
        var parts = manifest?.Where(p => p.Group == bodyId).ToList();
        if (parts == null || parts.Count == 0)
        {
            body.Mass = 3.0f;
            physics.AssumedValues.Add($"{bodyId} mass 3 kg (no manifest)");
            return;
        }
        var mass = parts.Sum(p => p.Mass);
        var c = parts.Aggregate(Vector3.Zero, (acc, p) => acc + p.Com * (float)p.Mass) / (float)mass;
        double ixx = 0, iyy = 0, izz = 0;
        foreach (var p in parts)
        {
            var d = p.Com - c;
            var e = p.Extent;
            ixx += p.Mass * (d.Y * d.Y + d.Z * d.Z + (e.Y * e.Y + e.Z * e.Z) / 12.0);
            iyy += p.Mass * (d.X * d.X + d.Z * d.Z + (e.X * e.X + e.Z * e.Z) / 12.0);
            izz += p.Mass * (d.X * d.X + d.Y * d.Y + (e.X * e.X + e.Y * e.Y) / 12.0);
        }
        body.Mass = (float)mass;
        body.CenterOfMassMode = RigidBody3D.CenterOfMassModeEnum.Custom;
        body.CenterOfMass = body.ToLocal(c);
        body.Inertia = new Vector3((float)ixx, (float)iyy, (float)izz);
        GD.Print(string.Format(CultureInfo.InvariantCulture, "{0}: mass {1:F3} kg from {2} CAD parts, centre of mass ({3:F4}, {4:F4}, {5:F4}), inertia ({6:F4}, {7:F4}, {8:F4}) kg·m²",
            bodyId, mass, parts.Count, c.X, c.Y, c.Z, ixx, iyy, izz));
    }

    public sealed record ManifestPart(string Group, double Mass, Vector3 Com, Vector3 Extent);

    private static List<ManifestPart> ReadManifest(string path)
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var list = new List<ManifestPart>();
        foreach (var p in doc.RootElement.GetProperty("parts").EnumerateArray())
        {
            Vector3 V(string key)
            {
                var a = p.GetProperty(key);
                return new Vector3(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
            }
            list.Add(new ManifestPart(p.GetProperty("group").GetString() ?? "", p.GetProperty("mass_kg_estimate").GetDouble(),
                V("center_of_mass"), V("bbox_max") - V("bbox_min")));
        }
        return list;
    }

    private static object? Get(Dictionary<object, object> d, params string[] keys)
    {
        object? node = d;
        foreach (var k in keys)
        {
            if (node is not Dictionary<object, object> m || !m.TryGetValue(k, out node))
            {
                return null;
            }
        }
        return node;
    }

    private static object? Value(object? node) =>
        node is Dictionary<object, object> q && q.TryGetValue("value", out var v) ? v : node;

    private static double Num(object? raw, double def) =>
        raw != null && double.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

    private static float[] Floats(object? raw) =>
        (raw as List<object> ?? throw new InvalidOperationException("expected a list of numbers"))
        .Select(o => float.Parse(o.ToString()!, CultureInfo.InvariantCulture)).ToArray();

    private static Vector3 Vec(object? raw)
    {
        var f = Floats(raw);
        return new Vector3(f[0], f[1], f[2]);
    }
}

/// <summary>One hinged field part (a HIVE). Angle 0 = loaded (CAD) pose, positive = right-handed
/// rotation about <see cref="Axis"/>. Each physics step applies pivot friction (Coulomb, limited so
/// it never reverses the motion), viscous pivot damping and the end dampers (config/physics.yaml
/// hive.damper: F = c·v + k·x + F0 while compressed, pushing only, capped) as torques about the
/// axis; the HingeJoint3D limits are the hard stops. A TIP (§10.5.1) is counted when the hive
/// leaves one stable state and arrives within TipEngageRad of the other.</summary>
public partial class HingeMechanism : Node3D
{
    // Godot's hinge measures the joint angle with the opposite sign of a right-handed rotation of
    // body B about the joint's +Z axis (verified by tests/scenarios/hinge_*.yaml).
    private const float GodotHingeAngleSign = -1f;

    private Basis _startBasis;
    private int _state;

    public RigidBody3D Body { get; set; } = null!;
    public Vector3 Axis { get; set; }
    public Vector3 Anchor { get; set; }
    public float LowerRad { get; set; }
    public float UpperRad { get; set; }
    public float TipEngageRad { get; set; }
    public HiveParams Params { get; set; } = null!;
    public string[] StateNames { get; set; } = { "lower", "upper" };

    /// <summary>Completed TIPs since load.</summary>
    public int Tips { get; private set; }

    /// <summary>Simulation time (s since load) of each TIP.</summary>
    public List<double> TipTimes { get; } = new();

    public float AngleRad { get; private set; }

    public float AngularSpeed { get; private set; }

    /// <summary>Name of the stable state the hinge is in ("" while between states).</summary>
    public string State => _state >= 0 ? StateNames[_state] : "";

    [Signal]
    public delegate void TippedEventHandler(string newState);

    private double _time;

    public void CreateJoint(StaticBody3D? staticBody)
    {
        _startBasis = Body.GlobalBasis;
        var z = Axis.Normalized();
        var helper = Mathf.Abs(z.Y) < 0.9f ? Vector3.Up : Vector3.Right;
        var x = helper.Cross(z).Normalized();
        var joint = new HingeJoint3D { Name = "Hinge" };
        AddChild(joint);
        joint.GlobalTransform = new Transform3D(new Basis(x, z.Cross(x), z), Anchor);
        if (staticBody != null)
        {
            joint.NodeA = joint.GetPathTo(staticBody);
        }
        joint.NodeB = joint.GetPathTo(Body);
        joint.ExcludeNodesFromCollision = true;
        joint.SetFlag(HingeJoint3D.Flag.UseLimit, true);
        var lo = GodotHingeAngleSign * LowerRad;
        var hi = GodotHingeAngleSign * UpperRad;
        joint.SetParam(HingeJoint3D.Param.LimitLower, Math.Min(lo, hi));
        joint.SetParam(HingeJoint3D.Param.LimitUpper, Math.Max(lo, hi));
        _state = StateFor(0f);
    }

    private int StateFor(float angle)
    {
        if (angle <= LowerRad + TipEngageRad)
        {
            return 0;
        }
        return angle >= UpperRad - TipEngageRad ? 1 : -1;
    }

    public override void _PhysicsProcess(double delta)
    {
        _time += delta;
        AngleRad = Angles.SignedAngle(_startBasis, Body.GlobalBasis, Axis);
        AngularSpeed = Body.AngularVelocity.Dot(Axis);
        var p = Params;

        // End dampers: index 0 acts near the lower stop, index 1 near the upper stop (ASSUMED order).
        var torque = 0f;
        for (var k = 0; k < 2; k++)
        {
            var arm = p.DamperLeverArms[Math.Min(k, p.DamperLeverArms.Length - 1)];
            var engage = Mathf.DegToRad(p.DamperEngageDeg[Math.Min(k, p.DamperEngageDeg.Length - 1)]);
            var distance = k == 0 ? AngleRad - LowerRad : UpperRad - AngleRad;
            if (distance >= engage)
            {
                continue;
            }
            var compression = arm * (engage - Math.Max(distance, 0f));
            var closing = arm * (k == 0 ? -AngularSpeed : AngularSpeed);
            var force = Math.Clamp(p.DamperCoefficient * Math.Max(closing, 0f) + p.DamperSpringRate * compression + p.DamperPreload, 0f, p.DamperMaxForce);
            torque += (k == 0 ? 1f : -1f) * force * arm;
        }

        // Pivot: viscous term and a Coulomb term that can stop the hive but never reverse it.
        torque -= p.PivotViscousDamping * AngularSpeed;
        var inertia = PivotInertia();
        var stopTorque = inertia * Math.Abs(AngularSpeed) / (float)delta;
        torque -= Math.Sign(AngularSpeed) * Math.Min(p.PivotFrictionTorque, stopTorque);

        if (torque != 0f)
        {
            Body.ApplyTorque(Axis * torque);
        }

        var state = StateFor(AngleRad);
        if (state >= 0 && _state >= 0 && state != _state)
        {
            Tips++;
            TipTimes.Add(_time);
            EmitSignal(SignalName.Tipped, StateNames[state]);
        }
        if (state >= 0)
        {
            _state = state;
        }
    }

    /// <summary>Moment of inertia about the hinge axis through the pivot (body inertia about its
    /// centre of mass plus m·d², d = distance of the centre of mass from the axis).</summary>
    private float PivotInertia()
    {
        var state = PhysicsServer3D.BodyGetDirectState(Body.GetRid());
        var axisInvInertia = Axis.Dot(state.InverseInertiaTensor * Axis);
        var iCom = axisInvInertia > 1e-9f ? 1f / axisInvInertia : 0f;
        var d = Body.GlobalPosition + state.CenterOfMass - Anchor;
        var perp = d - Axis * d.Dot(Axis);
        return iCom + Body.Mass * perp.LengthSquared();
    }
}
