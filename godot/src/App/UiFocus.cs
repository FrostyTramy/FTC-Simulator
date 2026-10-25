using Godot;

namespace Sim.App;

/// <summary>Keyboard ownership: while a text field or the code editor has focus, keys are for typing only.</summary>
public static class UiFocus
{
    public static bool Typing(Node node) => node.GetViewport()?.GuiGetFocusOwner() is TextEdit or LineEdit;
}
