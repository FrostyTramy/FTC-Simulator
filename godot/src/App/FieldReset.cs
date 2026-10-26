using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Field;
using Sim.Robot;

namespace Sim.App;

/// <summary>
/// Puts the field back the way it was loaded (practice "Reset", key R; match setup): every game element and both HIVES
/// to their starting pose at rest, the robots to their start poses with empty hoppers. Elements spawned later (P/N) stay
/// where they are. The match state is not touched.
/// </summary>
public sealed class FieldReset
{
    private readonly List<(RigidBody3D Body, Transform3D Start)> _bodies = new();
    private readonly List<(RobotBody Robot, Transform3D Start)> _robots = new();

    public FieldReset(LoadedField field, IEnumerable<RobotBody> robots)
    {
        foreach (var body in field.RigidGroups.Values)
        {
            _bodies.Add((body, body.GlobalTransform));
        }
        foreach (var element in field.Elements)
        {
            _bodies.Add((element.Body, element.Body.GlobalTransform));
        }
        _robots.AddRange(robots.Select(r => (r, r.GlobalTransform)));
    }

    public void Apply()
    {
        foreach (var (robot, start) in _robots.Where(r => GodotObject.IsInstanceValid(r.Robot)))
        {
            robot.ResetTo(start);
        }
        foreach (var (body, start) in _bodies)
        {
            body.GlobalTransform = start;
            body.LinearVelocity = Vector3.Zero;
            body.AngularVelocity = Vector3.Zero;
            body.Sleeping = false;
        }
    }
}
