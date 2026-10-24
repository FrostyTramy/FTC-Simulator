using Sim.Core.Time;

namespace Sim.Game.Match;

/// <summary>
/// Thresholds for "all SCORING ELEMENTS and ROBOTS have come to rest" (§10.5 A, C, D, E). The manual gives no
/// numbers; all values are ASSUMED and will move to config/physics.yaml.
/// </summary>
public sealed record SettleSettings(
    double MaxElementSpeed = 0.02,
    double MaxRobotSpeed = 0.02,
    double MaxRobotYawRate = 0.05,
    double MaxHiveSpeedDegPerS = 2.0,
    double RequiredSeconds = 0.5,
    double MaxSettleSeconds = 10.0)
{
    public const string Source = "ASSUMED (manual §10.5 says 'come to rest' without numbers)";
}

/// <summary>Detects when everything on the field has been still for <see cref="SettleSettings.RequiredSeconds"/>.</summary>
public sealed class AtRestDetector
{
    private readonly SettleSettings _settings;
    private readonly long _requiredTicks;
    private readonly long _maxTicks;
    private long _stillSince = -1;
    private long _startedAt = -1;

    public AtRestDetector(SettleSettings settings, int tickRate = SimClock.DefaultTickRate)
    {
        _settings = settings;
        _requiredTicks = SimClock.TicksCeil(settings.RequiredSeconds, tickRate);
        _maxTicks = SimClock.TicksCeil(settings.MaxSettleSeconds, tickRate);
    }

    /// <summary>True when the world has been still long enough, or the maximum settle time has passed.</summary>
    public bool Update(WorldSnapshot snap)
    {
        if (_startedAt < 0)
        {
            _startedAt = snap.Tick;
        }
        if (IsStill(snap))
        {
            if (_stillSince < 0)
            {
                _stillSince = snap.Tick;
            }
        }
        else
        {
            _stillSince = -1;
        }
        TimedOut = snap.Tick - _startedAt >= _maxTicks;
        return (_stillSince >= 0 && snap.Tick - _stillSince >= _requiredTicks) || TimedOut;
    }

    /// <summary>The last <see cref="Update"/> returned true only because the maximum settle time passed.</summary>
    public bool TimedOut { get; private set; }

    public void Reset()
    {
        _stillSince = -1;
        _startedAt = -1;
        TimedOut = false;
    }

    public bool IsStill(WorldSnapshot snap) =>
        snap.Elements.Where(e => e.OnField).All(e => e.Velocity.Length <= _settings.MaxElementSpeed)
        && snap.Robots.All(r => r.Velocity.Length <= _settings.MaxRobotSpeed && Math.Abs(r.YawRate) <= _settings.MaxRobotYawRate)
        && snap.Hives.All(h => Math.Abs(h.AngularSpeedDegPerS) <= _settings.MaxHiveSpeedDegPerS);
}
