using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Sim.Field;

/// <summary>Physics layers (bit values). Names are also set in project.godot [layer_names].</summary>
public static class CollisionLayers
{
    public const uint FieldStatic = 1u << 0;
    public const uint FieldMoving = 1u << 1;
    public const uint Elements = 1u << 2;
    public const uint Robots = 1u << 3;
    public const uint Zones = 1u << 4;
    public const uint RobotMechanisms = 1u << 5;
}

/// <summary>A scoring element body created from a layout instance (or spawned by a test/tool).</summary>
public sealed record FieldElement(string Group, string Type, string Id, RigidBody3D Body, Vector3 StartPosition);

/// <summary>What is needed to create more bodies of one element group.</summary>
public sealed record ElementTemplate(string Group, string Type, string? MeshPath, List<ColliderSpec> Colliders, ElementProps Props, PhysicsMaterial Material, Vector3 Inertia);

/// <summary>Everything the loader created for one layout.</summary>
public sealed class LoadedField
{
    public LoadedField(Node3D root, FieldLayout layout)
    {
        Root = root;
        Layout = layout;
    }

    public Node3D Root { get; }
    public FieldLayout Layout { get; }
    public List<StaticBody3D> StaticBodies { get; } = new();
    public Dictionary<string, RigidBody3D> RigidGroups { get; } = new();

    /// <summary>Orientation of each rigid group right after loading (for rotation checks).</summary>
    public Dictionary<string, Basis> RigidStartBasis { get; } = new();

    public Dictionary<string, ElementTemplate> ElementTemplates { get; } = new();

    /// <summary>Hinged parts created by FieldMechanisms (key = joint id).</summary>
    public Dictionary<string, HingeMechanism> Mechanisms { get; } = new();
    public List<FieldElement> Elements { get; } = new();
    public float GroundY { get; set; }

    /// <summary>Top of the field tiles: the playing surface, y = 0 of the CAD/sim frame (robots stand here).</summary>
    public float TileTopY { get; set; }
    public int ColliderCount { get; set; }
}

/// <summary>Builds bodies, meshes and colliders from a pipeline layout.json
/// (tools/cad_pipeline/run.py). Coordinates are the CAD frame: metres, Y up, red alliance on -X,
/// blue on +X, audience on +Z. Group kinds: static → StaticBody3D; rigid → RigidBody3D (frozen
/// until the field mechanisms of Phase 3/4 add joints); element → one RigidBody3D per instance.</summary>
public static class FieldLoader
{
    public static LoadedField Load(Node parent, string layoutPath, PhysicsConfig physics)
    {
        var layout = ReadJson<FieldLayout>(layoutPath);
        var dir = layoutPath[..(layoutPath.LastIndexOf('/') + 1)];
        var root = new Node3D { Name = $"Field_{layout.Name}" };
        parent.AddChild(root);
        var field = new LoadedField(root, layout);
        float? tilesBottom = null;
        float? staticBottom = null;

        foreach (var group in layout.Groups)
        {
            var colliders = group.Colliders != null
                ? ReadJson<List<ColliderSpec>>(dir + group.Colliders)
                : new List<ColliderSpec>();
            switch (group.Kind)
            {
                case "element":
                    AddElements(field, group, dir, colliders, physics);
                    break;
                case "rigid":
                    var rigid = new RigidBody3D
                    {
                        Name = group.Name,
                        Freeze = true,
                        FreezeMode = RigidBody3D.FreezeModeEnum.Static,
                        CollisionLayer = CollisionLayers.FieldMoving,
                        CollisionMask = CollisionLayers.FieldStatic | CollisionLayers.Elements | CollisionLayers.Robots,
                        PhysicsMaterialOverride = physics.Surface(group.Name),
                    };
                    AddBody(field, rigid, group, dir, colliders);
                    field.RigidGroups[group.Name] = rigid;
                    field.RigidStartBasis[group.Name] = rigid.GlobalBasis;
                    break;
                default:
                    var body = new StaticBody3D
                    {
                        Name = group.Name,
                        CollisionLayer = CollisionLayers.FieldStatic,
                        CollisionMask = 0,
                        PhysicsMaterialOverride = physics.Surface(group.Name),
                    };
                    AddBody(field, body, group, dir, colliders);
                    field.StaticBodies.Add(body);
                    var bottom = LowestBoxBottom(colliders);
                    if (bottom.HasValue)
                    {
                        staticBottom = Math.Min(staticBottom ?? bottom.Value, bottom.Value);
                        if (group.Name == "tiles")
                        {
                            tilesBottom = bottom;
                            field.TileTopY = HighestBoxTop(colliders) ?? 0f;
                        }
                    }
                    break;
            }
        }

        // The venue floor is at the bottom of the tiles (alliance areas and trays are on it).
        field.GroundY = tilesBottom ?? staticBottom ?? 0f;
        AddGround(root, field.GroundY, physics.Surface("ground"));
        return field;
    }

    private static void AddBody(LoadedField field, CollisionObject3D body, LayoutGroup group, string dir, List<ColliderSpec> colliders)
    {
        field.Root.AddChild(body);
        if (group.Mesh != null)
        {
            body.AddChild(MeshLoader.Instantiate(dir + group.Mesh));
        }
        foreach (var spec in colliders)
        {
            body.AddChild(ShapeFactory.Create(spec));
            field.ColliderCount++;
        }
    }

    private static void AddElements(LoadedField field, LayoutGroup group, string dir, List<ColliderSpec> colliders, PhysicsConfig physics)
    {
        var props = physics.Element(group.Name);
        // Hollow ball: I = k·m·r² with k from physics.yaml (2/3 for a thin shell), r from the collider.
        var radius = colliders.Find(c => c.Type == "sphere")?.Radius is double r ? (float)r : props.RadiusM;
        var inertia = props.InertiaFactor * props.MassKg * radius * radius;
        var template = new ElementTemplate(
            group.Name,
            PhysicsConfig.TypeOf(group.Name),
            group.Mesh == null ? null : dir + group.Mesh,
            colliders,
            props,
            new PhysicsMaterial { Friction = props.Friction, Bounce = props.Bounce },
            new Vector3(inertia, inertia, inertia));
        field.ElementTemplates[group.Name] = template;
        foreach (var inst in group.Instances ?? new List<LayoutInstance>())
        {
            CreateElement(field, template, inst.Id, ShapeFactory.RigidOnly(ShapeFactory.ToTransform(inst.Transform)));
        }
    }

    /// <summary>Adds one more element of an existing group (tests, practice mode, human player).</summary>
    public static FieldElement SpawnElement(LoadedField field, string group, Transform3D pose)
    {
        if (!field.ElementTemplates.TryGetValue(group, out var template))
        {
            throw new ArgumentException($"no element group '{group}' in layout {field.Layout.Name}");
        }
        var id = $"spawned_{group}_{field.Elements.Count:D3}";
        return CreateElement(field, template, id, pose);
    }

    private static FieldElement CreateElement(LoadedField field, ElementTemplate t, string id, Transform3D pose)
    {
        var index = field.Elements.Count;
        var body = new RigidBody3D
        {
            Name = $"{t.Group}_{index:D3}",
            Mass = t.Props.MassKg,
            Inertia = t.Inertia,
            PhysicsMaterialOverride = t.Material,
            ContinuousCd = true,
            CollisionLayer = CollisionLayers.Elements,
            CollisionMask = CollisionLayers.FieldStatic | CollisionLayers.FieldMoving | CollisionLayers.Elements | CollisionLayers.Robots,
            AngularDampMode = RigidBody3D.DampMode.Replace,
            AngularDamp = t.Props.AngularDamp,
            LinearDampMode = RigidBody3D.DampMode.Replace,
            LinearDamp = 0f,
        };
        body.SetMeta("element_id", id);
        body.SetMeta("element_type", t.Type);
        field.Root.AddChild(body);
        body.GlobalTransform = pose;
        if (t.MeshPath != null)
        {
            body.AddChild(MeshLoader.Instantiate(t.MeshPath));
        }
        foreach (var spec in t.Colliders)
        {
            body.AddChild(ShapeFactory.Create(spec));
            field.ColliderCount++;
        }
        var element = new FieldElement(t.Group, t.Type, id, body, pose.Origin);
        field.Elements.Add(element);
        return element;
    }

    private static void AddGround(Node3D root, float y, PhysicsMaterial material)
    {
        var ground = new StaticBody3D
        {
            Name = "Ground",
            CollisionLayer = CollisionLayers.FieldStatic,
            CollisionMask = 0,
            PhysicsMaterialOverride = material,
        };
        ground.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D { Plane = new Plane(Vector3.Up, y) } });
        var floor = new MeshInstance3D
        {
            Name = "VenueFloor",
            Mesh = new PlaneMesh { Size = new Vector2(14f, 10f) },
            Position = new Vector3(0f, y - 0.0005f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.17f, 0.19f), Roughness = 0.9f },
        };
        ground.AddChild(floor);
        root.AddChild(ground);
    }

    /// <summary>Highest Y of all box colliders (corners transformed), or null when there are none.</summary>
    private static float? HighestBoxTop(List<ColliderSpec> colliders)
    {
        float? highest = null;
        foreach (var spec in colliders)
        {
            if (spec.Type != "box" || spec.Transform == null || spec.Size == null)
            {
                continue;
            }
            var t = ShapeFactory.RigidOnly(ShapeFactory.ToTransform(spec.Transform));
            var h = new Vector3((float)spec.Size[0], (float)spec.Size[1], (float)spec.Size[2]) / 2;
            for (var i = 0; i < 8; i++)
            {
                var corner = t * new Vector3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z);
                highest = Math.Max(highest ?? corner.Y, corner.Y);
            }
        }
        return highest;
    }

    /// <summary>Lowest Y of all box colliders (corners transformed), or null when there are none.</summary>
    private static float? LowestBoxBottom(List<ColliderSpec> colliders)
    {
        float? lowest = null;
        foreach (var spec in colliders)
        {
            if (spec.Type != "box" || spec.Transform == null || spec.Size == null)
            {
                continue;
            }
            var t = ShapeFactory.ToTransform(spec.Transform);
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (float)spec.Size[0] * ((i & 1) == 0 ? -0.5f : 0.5f),
                    (float)spec.Size[1] * ((i & 2) == 0 ? -0.5f : 0.5f),
                    (float)spec.Size[2] * ((i & 4) == 0 ? -0.5f : 0.5f));
                var y = (t * corner).Y;
                lowest = Math.Min(lowest ?? y, y);
            }
        }
        return lowest;
    }

    public static bool LayoutExists(string layoutPath) => FileAccess.FileExists(layoutPath);

    private static T ReadJson<T>(string resPath)
    {
        if (!FileAccess.FileExists(resPath))
        {
            throw new System.IO.FileNotFoundException($"{resPath} not found (run tools/cad_pipeline/run.py)");
        }
        return JsonSerializer.Deserialize<T>(FileAccess.GetFileAsString(resPath), FieldLayout.Json)
            ?? throw new InvalidOperationException($"{resPath} is empty");
    }
}
