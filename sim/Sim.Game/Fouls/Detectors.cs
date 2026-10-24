using Sim.Core.Time;
using Sim.Game.Field;
using Sim.Game.Geometry;
using Sim.Game.Match;

namespace Sim.Game.Fouls;

/// <summary>What every foul detector can see besides the world snapshot.</summary>
public sealed record FoulContext(GameRules Rules, FieldGeometry Field, MatchStateMachine Match, FoulBook Book)
{
    public long Ticks(double seconds) => SimClock.TicksCeil(seconds, Match.TickRate);

    public double Seconds(long ticks) => (double)ticks / Match.TickRate;
}

/// <summary>Pure-logic detector of one rule, fed once per tick.</summary>
public interface IFoulDetector
{
    void Update(WorldSnapshot snap, FoulContext ctx);
}

/// <summary>G410 p109: no NECTAR into a FLOWER scoring volume before the last 60 s. MAJOR per NECTAR, attributed to the robot that controlled it last.</summary>
public sealed class G410NectarInFlowerDetector : IFoulDetector
{
    private HashSet<(int Element, string Flower)> _inside = [];

    public void Update(WorldSnapshot snap, FoulContext ctx)
    {
        var now = new HashSet<(int, string)>();
        foreach (var e in snap.ElementsById.Where(e => e.OnField && e.Kind.IsNectar()))
        {
            foreach (var flower in ctx.Field.Flowers)
            {
                if (!ZonePredicates.Intersects(flower.Volume, e))
                {
                    continue;
                }
                now.Add((e.Id, flower.Id));
                if (_inside.Contains((e.Id, flower.Id)) || !ctx.Match.IsRunning || ctx.Match.FlowerScoringOpen(snap.Tick))
                {
                    continue;
                }
                var robot = (e.ControlledBy ?? e.LastControlledBy) is { } id ? snap.Robot(id) : null;
                if (robot is null)
                {
                    continue; // nobody to attribute it to (e.g. knocked in by the field) - not a ROBOT entering NECTAR
                }
                var left = ctx.Seconds(ctx.Match.TimerRemainingTicks(snap.Tick));
                ctx.Book.Report(snap.Tick, "G410", robot.Alliance, robot.Id, Penalty.Major,
                    $"NECTAR {e.Id} entered {flower.Id} with {left:F2} s left; allowed only in the last {ctx.Rules.FlowerUnlockRemainingSeconds:F0} s");
            }
        }
        _inside = now;
    }
}

/// <summary>
/// G407 p108: CONTROL at most 4 SCORING ELEMENTS. More than MOMENTARY (&gt; 3 s, §10.6) → VERBAL WARNING per
/// instance. Likely STRATEGIC (6 or more, or REPEATED instances with 5) → MAJOR + YELLOW CARD once per MATCH.
/// </summary>
public sealed class G407ControlLimitDetector : IFoulDetector
{
    private sealed class State
    {
        public long OverSince = -1;
        public int EpisodeMax;
        public bool EpisodeReported;
        public int Episodes;
        public bool StrategicReported;
    }

    private readonly Dictionary<int, State> _states = [];

    public void Update(WorldSnapshot snap, FoulContext ctx)
    {
        if (!ctx.Match.IsRunning)
        {
            return;
        }
        var momentary = ctx.Ticks(ctx.Rules.MomentaryMaxSeconds);
        foreach (var robot in snap.RobotsById)
        {
            if (!_states.TryGetValue(robot.Id, out var s))
            {
                _states[robot.Id] = s = new State();
            }
            var count = robot.ControlledElements.Distinct().Count();
            if (count <= ctx.Rules.G407MaxControlled)
            {
                s.OverSince = -1;
                continue;
            }
            if (s.OverSince < 0)
            {
                s.OverSince = snap.Tick;
                s.EpisodeMax = 0;
                s.EpisodeReported = false;
            }
            s.EpisodeMax = Math.Max(s.EpisodeMax, count);
            if (s.EpisodeReported || snap.Tick - s.OverSince <= momentary)
            {
                continue;
            }
            s.EpisodeReported = true;
            s.Episodes++;
            ctx.Book.Report(snap.Tick, "G407", robot.Alliance, robot.Id, Penalty.VerbalWarning,
                $"robot {robot.Id} controlled {s.EpisodeMax} elements (max {ctx.Rules.G407MaxControlled}) for more than {ctx.Rules.MomentaryMaxSeconds:F0} s");
            var strategic = s.EpisodeMax >= ctx.Rules.G407LikelyStrategicCount || s.Episodes >= 2;
            if (strategic && !s.StrategicReported)
            {
                s.StrategicReported = true;
                ctx.Book.Report(snap.Tick, "G407", robot.Alliance, robot.Id, Penalty.Major,
                    s.Episodes >= 2
                        ? $"robot {robot.Id} REPEATEDLY controlled more than {ctx.Rules.G407MaxControlled} elements"
                        : $"robot {robot.Id} controlled {s.EpisodeMax} elements",
                    card: Card.Yellow, strategic: true);
            }
        }
    }
}

/// <summary>
/// G421 p114: a PIN may last at most 3 s; MAJOR, plus one MAJOR for every further 3 s (Table 10-6: 15 s of
/// violation = 6 MAJOR). The count pauses while the robots are ≥ 2 ft apart or either robot is ≥ 2 ft from
/// where the PIN started, and the PIN ends after 3 s of that, or when the pinning robot gets pinned.
/// Which contacts are PINs ("preventing the movement") is decided by the physics side (<see cref="PinContact"/>).
/// </summary>
public sealed class G421PinDetector : IFoulDetector
{
    private sealed class Pin
    {
        public required Sim.Core.Numerics.Vec2 PinnerStart;
        public required Sim.Core.Numerics.Vec2 PinnedStart;
        public long CountTicks;
        public long PausedTicks;
        public int MajorsIssued;
    }

    private readonly Dictionary<(int Pinner, int Pinned), Pin> _pins = [];

    public IReadOnlyCollection<(int Pinner, int Pinned)> ActivePins => _pins.Keys;

    public double CountSeconds(int pinner, int pinned, FoulContext ctx) =>
        _pins.TryGetValue((pinner, pinned), out var p) ? ctx.Seconds(p.CountTicks) : 0;

    public void Update(WorldSnapshot snap, FoulContext ctx)
    {
        if (!ctx.Match.IsRunning)
        {
            _pins.Clear();
            return;
        }
        foreach (var contact in snap.Pins.OrderBy(p => p.PinnerId).ThenBy(p => p.PinnedId))
        {
            var key = (contact.PinnerId, contact.PinnedId);
            var pinner = snap.Robot(contact.PinnerId);
            var pinned = snap.Robot(contact.PinnedId);
            if (!_pins.ContainsKey(key) && pinner is not null && pinned is not null && pinner.Alliance != pinned.Alliance)
            {
                _pins[key] = new Pin { PinnerStart = pinner.Centre, PinnedStart = pinned.Centre };
            }
        }

        var maxPin = ctx.Ticks(ctx.Rules.G421MaxPinSeconds);
        var repeat = ctx.Ticks(ctx.Rules.RepeatIntervalMajorSeconds);
        var endAfter = ctx.Ticks(ctx.Rules.G421SeparationSeconds);
        var pinnedNow = snap.Pins.Select(p => p.PinnedId).ToHashSet();
        foreach (var key in _pins.Keys.OrderBy(k => k.Pinner).ThenBy(k => k.Pinned).ToList())
        {
            var pin = _pins[key];
            var pinner = snap.Robot(key.Pinner);
            var pinned = snap.Robot(key.Pinned);
            if (pinner is null || pinned is null || pinnedNow.Contains(key.Pinner))
            {
                _pins.Remove(key); // G421.C: the pinning robot gets pinned (or a robot left the snapshot)
                continue;
            }
            var separated = pinner.Footprint.DistanceTo(pinned.Footprint) >= ctx.Rules.G421SeparationMetres;
            var moved = (pinner.Centre - pin.PinnerStart).Length >= ctx.Rules.G421SeparationMetres
                || (pinned.Centre - pin.PinnedStart).Length >= ctx.Rules.G421SeparationMetres;
            if (separated || moved)
            {
                pin.PausedTicks++;
                if (pin.PausedTicks > endAfter)
                {
                    _pins.Remove(key); // G421.A / G421.B
                }
                continue;
            }
            pin.PausedTicks = 0;
            pin.CountTicks++;
            if (pin.CountTicks <= maxPin)
            {
                continue;
            }
            var due = 1 + (int)((pin.CountTicks - maxPin) / repeat);
            while (pin.MajorsIssued < due)
            {
                pin.MajorsIssued++;
                ctx.Book.Report(snap.Tick, "G421", pinner.Alliance, pinner.Id, Penalty.Major,
                    $"robot {pinner.Id} pinned robot {pinned.Id} for {ctx.Seconds(pin.CountTicks):F2} s (MAJOR #{pin.MajorsIssued})");
            }
        }
    }
}

/// <summary>
/// G402 p106: during AUTO no interference on the opponent's side: CONTROLLING elements located there, or
/// contacting an opponent ROBOT that stayed on its own side. MAJOR per MATCH per robot. When both robots
/// reach across the centre line the contact is left to the referee (not reported).
/// </summary>
public sealed class G402AutoInterferenceDetector : IFoulDetector
{
    private readonly HashSet<int> _reported = [];

    public void Update(WorldSnapshot snap, FoulContext ctx)
    {
        if (ctx.Match.Phase != MatchPhase.Auto)
        {
            return;
        }
        foreach (var robot in snap.RobotsById.Where(r => !_reported.Contains(r.Id)))
        {
            var opponentSide = ctx.Field.Side(robot.Alliance.Opponent());
            var element = robot.ControlledElements.Order()
                .Select(snap.Element)
                .FirstOrDefault(e => e is not null && opponentSide.Contains(e.Position.Floor) && !ctx.Field.Side(robot.Alliance).Contains(e.Position.Floor));
            string? evidence = element is not null
                ? $"robot {robot.Id} controlled element {element.Id} on the {robot.Alliance.Opponent().Key()} side during AUTO"
                : null;
            if (evidence is null)
            {
                foreach (var otherId in robot.ContactingRobots.Order())
                {
                    var other = snap.Robot(otherId);
                    if (other is null || other.Alliance == robot.Alliance)
                    {
                        continue;
                    }
                    var crossed = Crossed(robot.Footprint, ctx.Field.Side(robot.Alliance), opponentSide);
                    var otherStayedHome = !Crossed(other.Footprint, opponentSide, ctx.Field.Side(robot.Alliance));
                    if (crossed && otherStayedHome)
                    {
                        evidence = $"robot {robot.Id} contacted opponent robot {other.Id} on the {other.Alliance.Key()} side during AUTO";
                        break;
                    }
                }
            }
            if (evidence is not null)
            {
                _reported.Add(robot.Id);
                ctx.Book.Report(snap.Tick, "G402", robot.Alliance, robot.Id, Penalty.Major, evidence);
            }
        }
    }

    // A convex footprint reaches into the other side iff one of its vertices lies there and not on the shared
    // centre line (points on the line belong to both sides), so merely touching the line does not count.
    private static bool Crossed(Polygon2 footprint, Polygon2 ownSide, Polygon2 otherSide) =>
        footprint.Vertices.Any(v => otherSide.Contains(v) && !ownSide.Contains(v));
}

/// <summary>
/// G403 p106 / G404 p107: no powered movement in the AUTO→TELEOP transition or after 0:00. VERBAL WARNING once per
/// robot and period (STRATEGIC escalation is the referee's call). Only fires when outputs are not disabled by
/// the field (FieldControlMode.Realistic) or a robot moves anyway.
/// </summary>
public sealed class PoweredMotionDetector : IFoulDetector
{
    private readonly HashSet<(int Robot, string Rule)> _reported = [];

    public void Update(WorldSnapshot snap, FoulContext ctx)
    {
        var rule = ctx.Match.Phase switch
        {
            MatchPhase.Transition => "G403",
            MatchPhase.MatchEnd or MatchPhase.PostMatchSettle => "G404",
            _ => null,
        };
        if (rule is null)
        {
            return;
        }
        foreach (var robot in snap.RobotsById.Where(r => r.PoweredMotion))
        {
            if (_reported.Add((robot.Id, rule)))
            {
                ctx.Book.Report(snap.Tick, rule, robot.Alliance, robot.Id, Penalty.VerbalWarning,
                    rule == "G403" ? $"robot {robot.Id} powered movement during the AUTO to TELEOP transition"
                                   : $"robot {robot.Id} powered movement after the end of the MATCH");
            }
        }
    }
}

/// <summary>
/// G416 p111 / R105.A p122: the robot must stay inside an 18 × 24 in footprint, 29 in tall, at all times.
/// VERBAL WARNING per instance (STRATEGIC MAJOR is the referee's call).
/// </summary>
public sealed class G416ExpansionDetector : IFoulDetector
{
    private readonly HashSet<int> _violating = [];

    public static bool Violates(RobotState robot, GameRules rules)
    {
        var limit = rules.ExpansionMetres;
        return !robot.Footprint.FitsInRectangle(limit[0], limit[1], 1e-6) || robot.TopY > limit[2] + 1e-6;
    }

    public void Update(WorldSnapshot snap, FoulContext ctx)
    {
        if (!ctx.Match.IsRunning)
        {
            return;
        }
        foreach (var robot in snap.RobotsById)
        {
            if (!Violates(robot, ctx.Rules))
            {
                _violating.Remove(robot.Id);
                continue;
            }
            if (_violating.Add(robot.Id))
            {
                ctx.Book.Report(snap.Tick, "G416", robot.Alliance, robot.Id, Penalty.VerbalWarning,
                    $"robot {robot.Id} exceeds the 18 x 24 x 29 in expansion limit (R105.A)");
            }
        }
    }
}
