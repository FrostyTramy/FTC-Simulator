using Sim.DriverStation;
using Sim.Runner;

namespace DriverStationTests;

/// <summary>Several Driver Stations sharing the PC's controllers (spec 08).</summary>
public class GamepadSeatTests
{
    private static void Press(GamepadSeats seats, int device, GamepadButtons button)
    {
        seats.Update(device, GamepadButtons.Start);
        seats.Update(device, GamepadButtons.Start | button);
        seats.Update(device, GamepadButtons.None);
    }

    [Fact]
    public void Start_A_assigns_to_the_focused_station_and_moves_a_controller_between_stations()
    {
        var seats = new GamepadSeats();
        seats.Add("red_1");
        seats.Add("blue_1");
        Assert.Equal("red_1", seats.Focused);
        Press(seats, 7, GamepadButtons.A);
        Assert.Equal(7, seats.Gamepad1("red_1"));
        Assert.Null(seats.Gamepad1("blue_1"));

        seats.Focused = "blue_1";
        Press(seats, 8, GamepadButtons.B);
        Assert.Equal(8, seats.Gamepad2("blue_1"));
        Press(seats, 7, GamepadButtons.A);                 // controller 7 moves from red 1 to blue 1
        Assert.Equal(7, seats.Gamepad1("blue_1"));
        Assert.Null(seats.Gamepad1("red_1"));

        seats.Disconnected(8);
        Assert.Null(seats.Gamepad2("blue_1"));
        seats.Remove("blue_1");
        Assert.Equal("red_1", seats.Focused);
    }
}
