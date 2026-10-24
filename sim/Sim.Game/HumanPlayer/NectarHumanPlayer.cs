using Sim.Core.Numerics;
using Sim.Game.Field;
using Sim.Game.Fouls;

namespace Sim.Game.HumanPlayer;

/// <summary>What a NECTAR first touched after a human released it (G427.C).</summary>
public enum LandingContact
{
    Tile,
    Robot,
    FieldElement,
}

public enum EntrySource
{
    /// <summary>One of the NECTAR staged in the ALLIANCE AREA at the start (§10.3.1 B.ii).</summary>
    Reserve,

    /// <summary>NECTAR that left the FIELD and was handed back to the drive team (§10.8.2); may be re-entered any time.</summary>
    Returned,
}

public sealed record EntryDecision(bool Allowed, EntrySource Source, string Reason);

/// <summary>
/// HUMAN PLAYER NECTAR handling (§10.1 p81, G426 p116, G427 p117, §10.8.2 p97): one extra NECTAR may be entered
/// for each TIP of the ALLIANCE's own HIVE, all remaining NECTAR once 60 s or less remain; NECTAR that left the
/// FIELD may be re-entered at any time. Entry is by hand into the own LOADING ZONE.
/// </summary>
public sealed class NectarHumanPlayer
{
    private readonly GameRules _rules;
    private readonly FieldGeometry _field;
    private readonly Dictionary<Alliance, AllianceState> _state = new()
    {
        [Alliance.Red] = new(),
        [Alliance.Blue] = new(),
    };

    public NectarHumanPlayer(GameRules rules, FieldGeometry field)
    {
        _rules = rules;
        _field = field;
        foreach (var s in _state.Values)
        {
            s.Reserve = rules.NectarInAllianceArea;
        }
    }

    private sealed class AllianceState
    {
        public int Reserve;
        public int Returned;
        public int Unlocked;
        public int EnteredFromReserve;
    }

    /// <summary>NECTAR still staged in the ALLIANCE AREA.</summary>
    public int Reserve(Alliance a) => _state[a].Reserve;

    /// <summary>NECTAR that left the FIELD and is back with the drive team.</summary>
    public int Returned(Alliance a) => _state[a].Returned;

    /// <summary>Reserve NECTAR unlocked by TIPs so far (G426.A).</summary>
    public int UnlockedByTips(Alliance a) => _state[a].Unlocked;

    /// <summary>A HIVE of this ALLIANCE was TIPPED (§10.5.1): one more NECTAR may be entered (G426.A).</summary>
    public void OnTip(Alliance a) => _state[a].Unlocked += _rules.NectarUnlockedPerTip;

    /// <summary>A NECTAR of this ALLIANCE left the FIELD and was returned to its drive team (§10.8.2).</summary>
    public void OnNectarReturned(Alliance a) => _state[a].Returned++;

    /// <summary>How many NECTAR this ALLIANCE may enter right now without a foul.</summary>
    public int AllowedNow(Alliance a, bool allEntryOpen)
    {
        var s = _state[a];
        var fromReserve = allEntryOpen ? s.Reserve : Math.Min(s.Reserve, Math.Max(0, s.Unlocked - s.EnteredFromReserve));
        return s.Returned + fromReserve;
    }

    public EntryDecision CanEnter(Alliance a, bool allEntryOpen)
    {
        var s = _state[a];
        if (s.Returned > 0)
        {
            return new EntryDecision(true, EntrySource.Returned, "NECTAR that left the FIELD may be re-entered at any time (G426 note, G427)");
        }
        if (s.Reserve == 0)
        {
            return new EntryDecision(false, EntrySource.Reserve, "no NECTAR left in the ALLIANCE AREA");
        }
        if (allEntryOpen)
        {
            return new EntryDecision(true, EntrySource.Reserve, "60 s or less remain: all remaining NECTAR may be entered (G426.B)");
        }
        return s.Unlocked > s.EnteredFromReserve
            ? new EntryDecision(true, EntrySource.Reserve, "unlocked by a TIP of the own HIVE (G426.A)")
            : new EntryDecision(false, EntrySource.Reserve, "no TIP has unlocked another NECTAR yet and more than 60 s remain (G426)");
    }

    /// <summary>
    /// A human enters one NECTAR. If it is not allowed, nothing happens unless <paramref name="force"/> is set, in
    /// which case it is entered and a MINOR FOUL (G426) is recorded. Returns the decision that was applied.
    /// </summary>
    public EntryDecision Enter(long tick, Alliance a, bool allEntryOpen, FoulBook book, bool force = false)
    {
        var decision = CanEnter(a, allEntryOpen);
        var s = _state[a];
        if (!decision.Allowed && (!force || s.Reserve == 0))
        {
            return decision;
        }
        if (decision.Source == EntrySource.Returned)
        {
            s.Returned--;
        }
        else
        {
            s.Reserve--;
            s.EnteredFromReserve++;
        }
        if (!decision.Allowed)
        {
            book.Report(tick, "G426", a, null, Penalty.Minor, $"{a.Key()} human player entered NECTAR early: {decision.Reason}");
        }
        return decision;
    }

    /// <summary>
    /// Checks where an entered NECTAR first landed (G427.C): it must touch a TILE inside the own LOADING ZONE
    /// before touching a ROBOT or field element; otherwise MINOR FOUL per NECTAR.
    /// </summary>
    public bool RecordLanding(long tick, Alliance a, int elementId, LandingContact firstContact, Vec2 point, FoulBook book)
    {
        var ok = firstContact == LandingContact.Tile && _field.LoadingZone(a).Contains(point);
        if (!ok)
        {
            book.Report(tick, "G427", a, null, Penalty.Minor,
                $"NECTAR {elementId} first touched {firstContact} at ({point.X:F3}, {point.Z:F3}), not the TILE in the {a.Key()} LOADING ZONE");
        }
        return ok;
    }
}
