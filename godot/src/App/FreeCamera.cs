using System;
using Godot;

namespace Sim.App;

/// <summary>Fly camera: hold the right mouse button to look, WASD to move, Q/E down/up, Shift
/// for 4× speed, mouse wheel changes speed, keys 1-4 select the preset views.</summary>
public partial class FreeCamera : Camera3D
{
    public enum View
    {
        Audience,
        RedAlliance,
        BlueAlliance,
        TopDown,
    }

    private const float LookSensitivity = 0.0035f;
    private float _speed = 2.0f;
    private float _yaw;
    private float _pitch;

    public static readonly View[] Presets = { View.Audience, View.RedAlliance, View.BlueAlliance, View.TopDown };

    public override void _Ready()
    {
        Near = 0.02f;
        Far = 200f;
        ApplyPreset(View.Audience);
    }

    /// <summary>Preset views in the field frame (audience on +Z, red on -X, blue on +X).</summary>
    public void ApplyPreset(View view)
    {
        Projection = ProjectionType.Perspective;
        Fov = 60f;
        switch (view)
        {
            case View.Audience:
                LookAtFromPosition(new Vector3(0f, 2.4f, 5.2f), new Vector3(0f, 0.3f, 0f), Vector3.Up);
                break;
            case View.RedAlliance:
                LookAtFromPosition(new Vector3(-3.0f, 1.7f, 0f), new Vector3(0f, 0.4f, 0f), Vector3.Up);
                break;
            case View.BlueAlliance:
                LookAtFromPosition(new Vector3(3.0f, 1.7f, 0f), new Vector3(0f, 0.4f, 0f), Vector3.Up);
                break;
            case View.TopDown:
                // Orthographic, audience at the bottom of the image (screen up = -Z).
                Projection = ProjectionType.Orthogonal;
                Size = 4.4f;
                Position = new Vector3(0f, 8f, 0f);
                Rotation = new Vector3(-Mathf.Pi / 2f, 0f, 0f);
                break;
        }
        _pitch = Rotation.X;
        _yaw = Rotation.Y;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } b:
                Input.MouseMode = b.Pressed ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _speed = Math.Min(_speed * 1.25f, 20f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _speed = Math.Max(_speed / 1.25f, 0.1f);
                break;
            case InputEventMouseMotion m when Input.IsMouseButtonPressed(MouseButton.Right):
                if (Projection == ProjectionType.Orthogonal)
                {
                    Projection = ProjectionType.Perspective;
                }
                _yaw -= m.Relative.X * LookSensitivity;
                _pitch = Mathf.Clamp(_pitch - m.Relative.Y * LookSensitivity, -1.55f, 1.55f);
                Rotation = new Vector3(_pitch, _yaw, 0f);
                break;
            case InputEventKey { Pressed: true, Echo: false } k:
                var index = k.Keycode - Key.Key1;
                if (index >= 0 && index < Presets.Length)
                {
                    ApplyPreset(Presets[index]);
                }
                break;
        }
    }

    public override void _Process(double delta)
    {
        var dir = Vector3.Zero;
        if (UiFocus.Typing(this))
        {
            return;   // keys belong to the code editor
        }
        if (Input.IsKeyPressed(Key.W)) dir -= Basis.Z;
        if (Input.IsKeyPressed(Key.S)) dir += Basis.Z;
        if (Input.IsKeyPressed(Key.A)) dir -= Basis.X;
        if (Input.IsKeyPressed(Key.D)) dir += Basis.X;
        if (Input.IsKeyPressed(Key.Q)) dir -= Vector3.Up;
        if (Input.IsKeyPressed(Key.E)) dir += Vector3.Up;
        if (dir == Vector3.Zero)
        {
            return;
        }
        var speed = _speed * (Input.IsKeyPressed(Key.Shift) ? 4f : 1f);
        Position += dir.Normalized() * speed * (float)delta;
    }
}
