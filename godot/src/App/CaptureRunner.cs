using Godot;

namespace Sim.App;

/// <summary>
/// <c>--capture PNG [--capture-seconds N] [--capture-panels]</c>: runs the normal interactive app, saves one screenshot
/// of the window after N seconds and quits (UI checks without a person at the keyboard). With --capture-panels the
/// code editor is opened first.
/// </summary>
public partial class CaptureRunner : Node
{
    private double _elapsed;
    private bool _panelsShown;

    public string Path { get; set; } = "";
    public double Seconds { get; set; } = 3;
    public bool ShowPanels { get; set; }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (ShowPanels && !_panelsShown && _elapsed > 0.5)
        {
            _panelsShown = true;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F4, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F4, Pressed = false });
        }
        if (_elapsed < Seconds)
        {
            return;
        }
        var image = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        var error = image.SavePng(Path);
        GD.Print(error == Error.Ok ? $"captured {Path}" : $"capture failed: {error}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
        SetProcess(false);
    }
}
