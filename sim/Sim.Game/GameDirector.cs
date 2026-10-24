using Sim.Core.Events;
using Sim.Core.Time;
using Sim.Game.Field;
using Sim.Game.Fouls;
using Sim.Game.HumanPlayer;
using Sim.Game.Match;
using Sim.Game.Scoring;

namespace Sim.Game;

public sealed record GameOptions(
    FoulMode FoulMode = FoulMode.Strict,
    FieldControlMode FieldControl = FieldControlMode.Assisted,
    string? EventType = null,
    int TickRate = SimClock.DefaultTickRate,
    SettleSettings? Settle = null);

/// <summary>
/// Runs one MATCH: feeds each tick's <see cref="WorldSnapshot"/> to the state machine, scoring engine,
/// foul detectors and human player, and keeps a deterministic event log (spec 01 determinism, spec 03).
/// </summary>
public sealed class GameDirector
{
    private readonly List<IFoulDetector> _detectors;
    private readonly AtRestDetector _rest;
    private readonly FoulContext _foulContext;
    private int _loggedFouls;

    public GameDirector(GameRules rules, FieldGeometry field, GameOptions? options = null)
    {
        Options = options ?? new GameOptions();
        Rules = rules;
        Field = field;
        Match = new MatchStateMachine(rules, Options.TickRate, Options.FieldControl);
        Scoring = new ScoringEngine(rules, field);
        Fouls = new FoulBook(rules, Options.FoulMode);
        HumanPlayer = new NectarHumanPlayer(rules, field);
        _rest = new AtRestDetector(Options.Settle ?? new SettleSettings(), Options.TickRate);
        _foulContext = new FoulContext(rules, field, Match, Fouls);
        _detectors =
        [
            new G402AutoInterferenceDetector(),
            new PoweredMotionDetector(),
            new G407ControlLimitDetector(),
            new G410NectarInFlowerDetector(),
            new G416ExpansionDetector(),
            new G421PinDetector(),
        ];
    }

    public GameOptions Options { get; }
    public GameRules Rules { get; }
    public FieldGeometry Field { get; }
    public MatchStateMachine Match { get; }
    public ScoringEngine Scoring { get; }
    public FoulBook Fouls { get; }
    public NectarHumanPlayer HumanPlayer { get; }
    public EventLog Log { get; } = new();
    public long Tick { get; private set; }

    /// <summary>Final score, available once the phase is <see cref="MatchPhase.Results"/>.</summary>
    public ScoreSheet? FinalScore { get; private set; }

    public string EventType => Options.EventType ?? Rules.DefaultEventType;

    public void StartMatch(long tick)
    {
        Tick = tick;
        Log.AppendRange(Match.Start(tick));
    }

    public void Abort() => Log.AppendRange(Match.Abort(Tick));

    /// <summary>Processes one simulation tick.</summary>
    public void Step(WorldSnapshot snap)
    {
        Tick = snap.Tick;
        var matchEvents = Match.Advance(snap.Tick);
        Log.AppendRange(matchEvents);
        foreach (var change in matchEvents.OfType<PhaseChanged>())
        {
            if (change.To == MatchPhase.Transition)
            {
                Log.AppendRange(Scoring.AssessAutoEnd(snap));
            }
            else if (change.To == MatchPhase.MatchEnd)
            {
                Log.AppendRange(Scoring.AssessMatchEnd(snap));
            }
        }

        if (Match.IsRunning || Match.Phase is MatchPhase.MatchEnd or MatchPhase.PostMatchSettle)
        {
            foreach (var tip in Scoring.ObserveHives(snap, Match.PeriodAt(snap.Tick)))
            {
                Log.Append(tip);
                HumanPlayer.OnTip(tip.Alliance);
            }
        }

        foreach (var detector in _detectors)
        {
            detector.Update(snap, _foulContext);
        }
        LogNewFouls();

        if (Match.Phase == MatchPhase.PostMatchSettle && _rest.Update(snap))
        {
            Log.AppendRange(Scoring.AssessFinal(snap));
            Log.AppendRange(Match.FinishSettle(snap.Tick));
            FinalScore = ScoreSheet.Compute(Scoring.Events, Fouls.Records, Rules, EventType);
        }
    }

    /// <summary>Score shown during the MATCH: recorded events plus the current CELL/FLOWER/GARDEN state.</summary>
    public ScoreSheet LiveScore(WorldSnapshot snap)
    {
        if (FinalScore is not null)
        {
            return FinalScore;
        }
        var provisional = Match.Phase == MatchPhase.PreMatch ? [] : Scoring.EvaluateEndState(snap);
        return ScoreSheet.Compute(Scoring.Events.Concat(provisional), Fouls.Records, Rules, EventType);
    }

    /// <summary>A human player enters one NECTAR (G426). Without <paramref name="force"/>, disallowed entries are refused.</summary>
    public EntryDecision EnterNectar(Alliance alliance, bool force = false)
    {
        if (!Match.IsRunning)
        {
            return new EntryDecision(false, EntrySource.Reserve, "NECTAR can only be entered while the MATCH is running");
        }
        var decision = HumanPlayer.Enter(Tick, alliance, Match.NectarAllEntryOpen(Tick), Fouls, force);
        LogNewFouls();
        return decision;
    }

    /// <summary>Where an entered NECTAR first landed (G427).</summary>
    public bool RecordNectarLanding(Alliance alliance, int elementId, LandingContact firstContact, Sim.Core.Numerics.Vec2 point)
    {
        var ok = HumanPlayer.RecordLanding(Tick, alliance, elementId, firstContact, point, Fouls);
        LogNewFouls();
        return ok;
    }

    /// <summary>A NECTAR left the FIELD and goes back to its drive team (§10.8.2).</summary>
    public void NectarLeftField(Alliance owner) => HumanPlayer.OnNectarReturned(owner);

    /// <summary>Determinism hash of everything that happened so far (event log + score).</summary>
    public ulong StateHash()
    {
        var h = new StableHasher().Add((long)Log.Hash);
        if (FinalScore is not null)
        {
            h.Add((long)FinalScore.Hash());
        }
        return h.Value;
    }

    private void LogNewFouls()
    {
        while (_loggedFouls < Fouls.Records.Count)
        {
            Log.Append(Fouls.Records[_loggedFouls++]);
        }
    }
}
