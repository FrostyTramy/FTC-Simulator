using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Sim.App;

/// <summary>
/// Cheap per-section timing of the physics tick (runner round trip, robot step, rules engine...), shown in the HUD as
/// average milliseconds per tick so slow parts are visible without a profiler.
/// </summary>
public static class TickProfiler
{
    private static readonly Dictionary<string, (long Ticks, int Count)> Sections = new();

    public static long Start() => Stopwatch.GetTimestamp();

    public static void Stop(string section, long start)
    {
        var elapsed = Stopwatch.GetTimestamp() - start;
        Sections[section] = Sections.TryGetValue(section, out var s) ? (s.Ticks + elapsed, s.Count + 1) : (elapsed, 1);
    }

    /// <summary>"name 0.12 ms" per section since the last call (average per call), slowest first; resets the counters.</summary>
    public static string TakeSummary()
    {
        var text = string.Join(" · ", Sections
            .OrderByDescending(kv => kv.Value.Ticks / (double)kv.Value.Count)
            .Select(kv => $"{kv.Key} {kv.Value.Ticks * 1000.0 / Stopwatch.Frequency / kv.Value.Count:0.00}"));
        Sections.Clear();
        return text;
    }
}
