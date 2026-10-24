namespace Sim.Core.Time;

/// <summary>
/// Fixed-step simulation clock. Time is an integer tick count; seconds are derived as
/// tick / rate so no floating-point error accumulates (determinism, spec 01).
/// </summary>
public sealed class SimClock
{
    public const int DefaultTickRate = 240;

    public SimClock(int tickRate = DefaultTickRate)
    {
        if (tickRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickRate));
        }
        TickRate = tickRate;
    }

    public int TickRate { get; }
    public long Tick { get; private set; }
    public double Seconds => ToSeconds(Tick);

    public void Advance() => Tick++;

    public void Reset(long tick = 0) => Tick = tick;

    public double ToSeconds(long ticks) => (double)ticks / TickRate;

    /// <summary>Exact tick count for a duration. Throws if the duration is not a whole number of ticks.</summary>
    public long TicksFor(double seconds) => TicksFor(seconds, TickRate);

    public static long TicksFor(double seconds, int tickRate)
    {
        var exact = seconds * tickRate;
        var rounded = Math.Round(exact);
        if (Math.Abs(exact - rounded) > 1e-6)
        {
            throw new ArgumentException($"{seconds} s is not a whole number of ticks at {tickRate} Hz");
        }
        return (long)rounded;
    }

    /// <summary>Tick count for a duration, rounded up (for thresholds that need not align with ticks).</summary>
    public static long TicksCeil(double seconds, int tickRate) => (long)Math.Ceiling(seconds * tickRate - 1e-9);
}
