namespace Sim.Core.Events;

/// <summary>Base type of everything recorded in a match log (match, scoring, foul events).</summary>
public interface ISimEvent
{
    /// <summary>Simulation tick at which the event happened.</summary>
    long Tick { get; }

    /// <summary>Short machine-readable type, e.g. "PhaseChanged", "HiveTip", "Foul".</summary>
    string Type { get; }

    /// <summary>Adds every field that defines the event to a determinism hash.</summary>
    void AddTo(StableHasher hasher);
}

/// <summary>Append-only, tick-ordered event log with a running determinism hash.</summary>
public sealed class EventLog
{
    private readonly List<ISimEvent> _events = [];
    private readonly StableHasher _hash = new();

    public IReadOnlyList<ISimEvent> Events => _events;
    public ulong Hash => _hash.Value;

    public void Append(ISimEvent e)
    {
        if (_events.Count > 0 && e.Tick < _events[^1].Tick)
        {
            throw new InvalidOperationException($"event at tick {e.Tick} after tick {_events[^1].Tick}");
        }
        _events.Add(e);
        _hash.Add(e.Tick).Add(e.Type);
        e.AddTo(_hash);
    }

    public void AppendRange(IEnumerable<ISimEvent> events)
    {
        foreach (var e in events)
        {
            Append(e);
        }
    }

    public IEnumerable<T> OfType<T>() where T : ISimEvent => _events.OfType<T>();
}
