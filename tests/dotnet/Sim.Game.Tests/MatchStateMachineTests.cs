using Sim.Game.Match;

namespace GameTests;

/// <summary>MATCH timing (§10.4 p85, Table 9-1 p79) at the 240 Hz sim rate.</summary>
public class MatchStateMachineTests
{
    private static MatchStateMachine Started()
    {
        var m = new MatchStateMachine(TestWorld.Rules);
        m.Start(0);
        return m;
    }

    [Fact]
    public void Period_lengths_come_from_the_rules()
    {
        var m = new MatchStateMachine(TestWorld.Rules);
        Assert.Equal(30 * 240, m.AutoTicks);
        Assert.Equal(8 * 240, m.TransitionTicks);
        Assert.Equal(120 * 240, m.TeleopTicks);
    }

    [Fact]
    public void Phases_change_on_the_exact_ticks()
    {
        var m = Started();
        Assert.Equal(MatchPhase.Auto, m.Phase);
        Assert.Empty(m.Advance(7199).OfType<PhaseChanged>());
        var autoEnd = m.Advance(7200);
        Assert.Equal(MatchPhase.Transition, m.Phase);
        Assert.Contains(autoEnd, e => e is StopAutoOpMode { Tick: 7200 });
        Assert.Contains(autoEnd, e => e is RobotOutputsChanged { Enabled: false });
        m.Advance(9119);
        Assert.Equal(MatchPhase.Transition, m.Phase);
        Assert.Contains(m.Advance(9120), e => e is RobotOutputsChanged { Enabled: true });
        Assert.Equal(MatchPhase.Teleop, m.Phase);
        m.Advance(37919);
        Assert.Equal(MatchPhase.Teleop, m.Phase);
        Assert.Contains(m.Advance(37920), e => e is RobotOutputsChanged { Enabled: false });
        Assert.Equal(MatchPhase.MatchEnd, m.Phase);
        m.Advance(37921);
        Assert.Equal(MatchPhase.PostMatchSettle, m.Phase);
    }

    [Fact]
    public void Advancing_over_several_boundaries_emits_all_of_them_in_order()
    {
        var m = Started();
        var events = m.Advance(40000);
        var phases = events.OfType<PhaseChanged>().Select(p => p.To).ToList();
        Assert.Equal([MatchPhase.Transition, MatchPhase.Teleop, MatchPhase.MatchEnd, MatchPhase.PostMatchSettle], phases);
        Assert.Equal(events.Select(e => e.Tick).OrderBy(t => t), events.Select(e => e.Tick));
    }

    [Fact]
    public void Audio_cues_follow_table_9_1()
    {
        var m = Started();
        var cues = m.Advance(37920).OfType<AudioCue>().ToDictionary(c => c.Kind);
        Assert.Equal(7200, cues[AudioCueKind.AutoEnd].Tick);
        Assert.Equal("Buzzer x 3", cues[AudioCueKind.AutoEnd].Text);
        Assert.Equal(9120, cues[AudioCueKind.TeleopStart].Tick);
        Assert.Equal("3 Bells", cues[AudioCueKind.TeleopStart].Text);
        Assert.Equal(37920 - 60 * 240, cues[AudioCueKind.FlowerUnlock].Tick);
        Assert.Equal(37920 - 20 * 240, cues[AudioCueKind.Final20].Tick);
        Assert.Equal("3-second Buzzer", cues[AudioCueKind.MatchEnd].Text);
    }

    [Theory]
    [InlineData(0, "2:30")]
    [InlineData(1, "2:30")]
    [InlineData(240, "2:29")]
    [InlineData(7200, "2:00")]
    [InlineData(8000, "2:00")]
    [InlineData(9120, "2:00")]
    [InlineData(23520, "1:00")]
    [InlineData(37920, "0:00")]
    public void Field_timer_counts_down_and_freezes_in_the_transition(long tick, string expected)
    {
        var m = Started();
        m.Advance(tick);
        Assert.Equal(expected, m.TimerDisplay(tick));
    }

    [Fact]
    public void Transition_has_its_own_8_second_countdown()
    {
        var m = Started();
        m.Advance(7200);
        Assert.Equal("0:08", m.TransitionCountdownDisplay(7200));
        Assert.Equal("0:01", m.TransitionCountdownDisplay(9119));
        Assert.Null(m.TransitionCountdownDisplay(9120));
    }

    [Fact]
    public void Achievements_before_teleop_start_belong_to_auto()
    {
        var m = Started();
        Assert.Equal(MatchPeriod.Auto, m.PeriodAt(7200 + 100)); // §10.5 B: complete before TELEOP starts
        Assert.Equal(MatchPeriod.Teleop, m.PeriodAt(9120));
    }

    [Fact]
    public void Flower_scoring_and_all_nectar_entry_open_with_60_seconds_left()
    {
        var m = Started();
        m.Advance(23519);
        Assert.False(m.FlowerScoringOpen(23519));
        Assert.False(m.NectarAllEntryOpen(23519));
        m.Advance(23520);
        Assert.True(m.FlowerScoringOpen(23520));
        Assert.True(m.NectarAllEntryOpen(23520));
    }

    [Fact]
    public void Abort_disables_the_robots_and_plays_the_foghorn()
    {
        var m = Started();
        m.Advance(1000);
        var events = m.Abort(1000);
        Assert.Equal(MatchPhase.Aborted, m.Phase);
        Assert.Contains(events, e => e is AudioCue { Kind: AudioCueKind.MatchStopped, Text: "Foghorn" });
        Assert.False(m.OutputsEnabled);
    }

    [Fact]
    public void Time_cannot_go_backwards()
    {
        var m = Started();
        m.Advance(100);
        Assert.Throws<ArgumentException>(() => m.Advance(99));
    }
}
