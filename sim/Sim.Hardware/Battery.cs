namespace Sim.Hardware;

/// <summary>
/// 12 V NiMH main battery (config/battery.yaml): open-circuit voltage from the state of charge (piecewise
/// linear curve), series resistance = pack × age multiplier + wiring, charge drained by the battery current.
/// Brownout (REV hubs operate 8–15 V): below the threshold all outputs are disabled for a hold time and come
/// back once the voltage recovers above threshold + hysteresis.
/// </summary>
public sealed class Battery
{
    private double _disabledFor;

    public Battery(BatterySpec spec, double stateOfCharge = 1.0, double resistanceMultiplier = 1.0)
    {
        Spec = spec;
        StateOfCharge = Math.Clamp(stateOfCharge, 0, 1);
        ResistanceMultiplier = resistanceMultiplier;
        BusVolts = OpenCircuitVolts;
    }

    public BatterySpec Spec { get; }
    public double StateOfCharge { get; private set; }
    public double ResistanceMultiplier { get; }

    /// <summary>Bus voltage after the last <see cref="Update"/> (what the SDK VoltageSensor reports).</summary>
    public double BusVolts { get; private set; }

    public double CurrentA { get; private set; }

    /// <summary>True while a brownout holds the motor and servo outputs off.</summary>
    public bool BrownedOut => _disabledFor > 0;

    public double SeriesResistanceOhm => Spec.PackResistanceOhm * ResistanceMultiplier + Spec.WiringResistanceOhm;

    public double OpenCircuitVolts => Interpolate(Spec.OcvCurve, StateOfCharge);

    /// <summary>Records the solved bus state of one step and drains the charge (Ah = A·s / 3600).</summary>
    public void Update(double busVolts, double currentA, double dt)
    {
        BusVolts = busVolts;
        CurrentA = currentA;
        StateOfCharge = Math.Clamp(StateOfCharge - currentA * dt / (3600 * Spec.CapacityAh), 0, 1);
        if (busVolts < Spec.BrownoutVoltage)
        {
            _disabledFor = Spec.OutputDisableTimeS;
        }
        else if (_disabledFor > 0 && busVolts > Spec.BrownoutVoltage + Spec.BrownoutHysteresisV)
        {
            _disabledFor = Math.Max(0, _disabledFor - dt);
        }
    }

    private static double Interpolate(double[][] curve, double x)
    {
        if (x <= curve[0][0])
        {
            return curve[0][1];
        }
        for (var i = 1; i < curve.Length; i++)
        {
            if (x <= curve[i][0])
            {
                var t = (x - curve[i - 1][0]) / (curve[i][0] - curve[i - 1][0]);
                return curve[i - 1][1] + t * (curve[i][1] - curve[i - 1][1]);
            }
        }
        return curve[^1][1];
    }
}
