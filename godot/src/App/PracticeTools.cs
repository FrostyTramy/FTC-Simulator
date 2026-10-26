using System.Linq;
using Godot;
using Sim.Field;
using Sim.Match;

namespace Sim.App;

/// <summary>
/// Practice-mode tools (spec 08 "Practice tools", Phase 4): left-drag picks up a game element and carries it under the
/// mouse (release to drop or throw it); P spawns a POLLEN and N a NECTAR (red on the red half, blue on the blue half)
/// 0.4 m above the point under the mouse. Clicks on the Driver Station or scoreboard never reach these tools. The
/// actions go through the match session (recorded in the replay) and are off while a MATCH runs.
/// </summary>
public partial class PracticeTools : Node
{
    private const float CarryGain = 18f;      // 1/s: velocity towards the cursor per metre of distance
    private const float MaxCarrySpeed = 6f;   // m/s
    private readonly LoadedField _field;
    private readonly Camera3D _camera;
    private readonly System.Func<MatchSession?> _session;
    private RigidBody3D? _held;
    private float _heldDistance;

    public PracticeTools(LoadedField field, Camera3D camera, System.Func<MatchSession?> session)
    {
        _field = field;
        _camera = camera;
        _session = session;
    }

    private bool Allowed => _session() is { } s && !s.Director.Match.IsRunning;

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Allowed)
        {
            _held = null;
            return;
        }
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                var hit = Pick(press.Position);
                if (hit is { } h && _field.Elements.FirstOrDefault(x => x.Body == h.Collider) is { } element)
                {
                    _held = element.Body;
                    _heldDistance = _camera.GlobalPosition.DistanceTo(h.Point);
                    _held.Sleeping = false;
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                _held = null;
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.P or Key.N } key:
                if (Pick(GetViewport().GetMousePosition()) is { } at)
                {
                    var group = key.Keycode == Key.P ? "pollen" : at.Point.X < 0 ? "nectar_red" : "nectar_blue";
                    _session()?.SpawnElement(group, at.Point + new Vector3(0, 0.4f, 0));
                }
                break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_held == null || !IsInstanceValid(_held) || !Allowed)
        {
            _held = null;
            return;
        }
        var mouse = GetViewport().GetMousePosition();
        var target = _camera.ProjectRayOrigin(mouse) + _camera.ProjectRayNormal(mouse) * _heldDistance;
        var velocity = (target - _held.GlobalPosition) * CarryGain;
        var index = _field.Elements.FindIndex(x => x.Body == _held);
        _session()?.MoveElement(index, velocity.Length() > MaxCarrySpeed ? velocity.Normalized() * MaxCarrySpeed : velocity);
    }

    private (GodotObject Collider, Vector3 Point)? Pick(Vector2 screen)
    {
        var from = _camera.ProjectRayOrigin(screen);
        var to = from + _camera.ProjectRayNormal(screen) * 50f;
        var query = PhysicsRayQueryParameters3D.Create(from, to,
            CollisionLayers.FieldStatic | CollisionLayers.FieldMoving | CollisionLayers.Elements | CollisionLayers.Robots);
        var hit = _camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? null : ((GodotObject)hit["collider"], (Vector3)hit["position"]);
    }
}
