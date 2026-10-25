using System.Collections.Generic;
using Godot;
using Sim.Field;
using Sim.Sensors;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace Sim.Robot;

/// <summary>
/// Ground truth for a robot's sensors from the Godot scene (spec 09): the robot body's pose, and rays through the
/// physics space that ignore the robot's own colliders. A hit's colour is the albedo of the first mesh found under the
/// collider (linear RGB), cached per collider; surfaces without a material read mid grey.
/// </summary>
public sealed class GodotSensorWorld(RobotBody robot) : ISensorWorld
{
    private static readonly NVector3 Grey = new(0.5f, 0.5f, 0.5f);
    private const uint Mask = CollisionLayers.FieldStatic | CollisionLayers.FieldMoving | CollisionLayers.Elements | CollisionLayers.Robots;
    private readonly Dictionary<ulong, NVector3> _colors = new();

    public NVector3 RobotPosition => ToNumerics(robot.GlobalPosition);

    public NQuaternion RobotRotation
    {
        get
        {
            var q = robot.GlobalBasis.Orthonormalized().GetRotationQuaternion();
            return new NQuaternion(q.X, q.Y, q.Z, q.W);
        }
    }

    public SurfaceHit? Raycast(NVector3 origin, NVector3 direction, float maxDistance)
    {
        var from = ToGodot(origin);
        var to = from + ToGodot(NVector3.Normalize(direction)) * maxDistance;
        var query = PhysicsRayQueryParameters3D.Create(from, to, Mask, new Godot.Collections.Array<Rid> { robot.GetRid() });
        var hit = robot.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return null;
        }
        var position = (Vector3)hit["position"];
        var normal = (Vector3)hit["normal"];
        var collider = hit["collider"].As<GodotObject>();
        return new SurfaceHit(from.DistanceTo(position), ToNumerics(normal), ColorOf(collider));
    }

    private NVector3 ColorOf(GodotObject? collider)
    {
        if (collider is not Node node)
        {
            return Grey;
        }
        var id = node.GetInstanceId();
        if (!_colors.TryGetValue(id, out var color))
        {
            color = FindAlbedo(node) is { } c ? new NVector3(c.R, c.G, c.B) : Grey;
            _colors[id] = color;
        }
        return color;
    }

    private static Color? FindAlbedo(Node node)
    {
        if (node is MeshInstance3D mesh)
        {
            var material = mesh.MaterialOverride ?? (mesh.Mesh?.GetSurfaceCount() > 0 ? mesh.GetActiveMaterial(0) : null);
            if (material is BaseMaterial3D m)
            {
                return m.AlbedoColor.SrgbToLinear();
            }
        }
        foreach (var child in node.GetChildren())
        {
            if (FindAlbedo(child) is { } c)
            {
                return c;
            }
        }
        return null;
    }

    private static NVector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3 ToGodot(NVector3 v) => new(v.X, v.Y, v.Z);
}
