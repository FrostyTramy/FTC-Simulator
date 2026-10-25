using System.Collections.Generic;
using Godot;
using Sim.Field;
using Sim.Robot;

namespace Sim.App;

/// <summary>
/// Puts the field back the way it was loaded (practice "Reset", key R): every game element and both HIVES to their
/// starting pose at rest, the robot to its start pose with an empty hopper. Elements spawned later (P/N) stay where
/// they are. The match state is not touched.
/// </summary>
public sealed class FieldReset
{
    private readonly List<(RigidBody3D Body, Transform3D Start)> _bodies = new();
    private readonly RobotBody? _robot;
    private readonly Transform3D _robotStart;

    public FieldReset(LoadedField field, RobotBody? robot)
    {
        foreach (var body in field.RigidGroups.Values)
        {
            _bodies.Add((body, body.GlobalTransform));
        }
        foreach (var element in field.Elements)
        {
            _bodies.Add((element.Body, element.Body.GlobalTransform));
        }
        _robot = robot;
        _robotStart = robot?.GlobalTransform ?? Transform3D.Identity;
    }

    public void Apply()
    {
        _robot?.ResetTo(_robotStart);
        foreach (var (body, start) in _bodies)
        {
            body.GlobalTransform = start;
            body.LinearVelocity = Vector3.Zero;
            body.AngularVelocity = Vector3.Zero;
            body.Sleeping = false;
        }
    }
}
