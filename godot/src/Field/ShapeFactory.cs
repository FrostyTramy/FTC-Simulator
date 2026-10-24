using System;
using Godot;

namespace Sim.Field;

/// <summary>Converts pipeline collider specs and row-major matrices into Godot physics shapes.</summary>
public static class ShapeFactory
{
    /// <summary>Row-major 4x4 (pipeline JSON) → Transform3D. Basis columns are the matrix columns.</summary>
    public static Transform3D ToTransform(double[] m)
    {
        if (m.Length != 16)
        {
            throw new ArgumentException($"expected 16 matrix values, got {m.Length}");
        }
        var basis = new Basis(
            new Vector3((float)m[0], (float)m[4], (float)m[8]),
            new Vector3((float)m[1], (float)m[5], (float)m[9]),
            new Vector3((float)m[2], (float)m[6], (float)m[10]));
        return new Transform3D(basis, new Vector3((float)m[3], (float)m[7], (float)m[11]));
    }

    /// <summary>Physics engines need rotation-only shape transforms. Removes scale/shear and turns a
    /// mirrored basis into a proper rotation (boxes, cylinders and spheres are symmetric, so
    /// flipping one axis does not change the shape).</summary>
    public static Transform3D RigidOnly(Transform3D t)
    {
        var b = t.Basis.Orthonormalized();
        if (b.Determinant() < 0)
        {
            b.Z = -b.Z;
        }
        return new Transform3D(b, t.Origin);
    }

    public static CollisionShape3D Create(ColliderSpec spec)
    {
        var node = new CollisionShape3D();
        switch (spec.Type)
        {
            case "box":
                node.Shape = new BoxShape3D { Size = Vec(spec.Size, "box size") };
                node.Transform = RigidOnly(ToTransform(Require(spec.Transform, "box transform")));
                break;
            case "sphere":
                node.Shape = new SphereShape3D { Radius = (float)Require(spec.Radius, "sphere radius") };
                node.Position = Vec(spec.Center, "sphere center");
                break;
            case "cylinder":
                node.Shape = new CylinderShape3D
                {
                    Radius = (float)Require(spec.Radius, "cylinder radius"),
                    Height = (float)Require(spec.Height, "cylinder height"),
                };
                node.Transform = RigidOnly(ToTransform(Require(spec.Transform, "cylinder transform")));
                break;
            case "convex":
                var pts = Require(spec.Points, "convex points");
                var points = new Vector3[pts.Length];
                for (var i = 0; i < pts.Length; i++)
                {
                    points[i] = new Vector3((float)pts[i][0], (float)pts[i][1], (float)pts[i][2]);
                }
                node.Shape = new ConvexPolygonShape3D { Points = points };
                break;
            default:
                throw new ArgumentException($"unknown collider type '{spec.Type}'");
        }
        return node;
    }

    private static Vector3 Vec(double[]? v, string what)
    {
        var a = Require(v, what);
        return new Vector3((float)a[0], (float)a[1], (float)a[2]);
    }

    private static T Require<T>(T? value, string what) where T : class =>
        value ?? throw new ArgumentException($"collider is missing {what}");

    private static double Require(double? value, string what) =>
        value ?? throw new ArgumentException($"collider is missing {what}");
}
