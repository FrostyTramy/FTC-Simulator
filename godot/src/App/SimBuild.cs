namespace Sim.App;

/// <summary>
/// Identifies the simulator build in replays and match records (spec 01 "Replay files ... include sim build hash"):
/// the module version id of the compiled game code, which changes whenever the code changes.
/// </summary>
public static class SimBuild
{
    public static string Id { get; } = typeof(SimBuild).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..12];
}
