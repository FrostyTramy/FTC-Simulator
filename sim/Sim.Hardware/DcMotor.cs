namespace Sim.Hardware;

/// <summary>
/// Brushed DC gear motor seen at its output shaft (spec 04 "DC motor model"). Constants come from the
/// datasheet points at the nominal voltage (config/motors.yaml header):
///   R = V / I_stall,  Kt = T_stall / I_stall,  Ke = (V − I_free·R) / ω_free,  T_friction = Kt·I_free.
/// The gearbox efficiency is implicit in the datasheet stall torque. Electrical inductance is ignored
/// (time constant ≪ one 240 Hz step).
///   Driving:  I = (V_applied − Ke·ω) / R,   τ = Kt·I − T_friction·sign(ω)
///   BRAKE (windings shorted): V_applied = 0 → I = −Ke·ω / R.   FLOAT (open): I = 0.
/// </summary>
public sealed class DcMotor
{
    public DcMotor(MotorSpec spec)
    {
        Spec = spec;
        ResistanceOhm = spec.NominalVoltage / spec.StallCurrentA;
        TorqueConstant = spec.StallTorqueNm / spec.StallCurrentA;
        BackEmfConstant = (spec.NominalVoltage - spec.FreeCurrentA * ResistanceOhm) / spec.FreeSpeedRadPerS;
        FrictionTorqueNm = TorqueConstant * spec.FreeCurrentA;
    }

    public MotorSpec Spec { get; }
    public double ResistanceOhm { get; }

    /// <summary>Kt at the output shaft, N·m/A.</summary>
    public double TorqueConstant { get; }

    /// <summary>Ke at the output shaft, V·s/rad.</summary>
    public double BackEmfConstant { get; }

    public double FrictionTorqueNm { get; }

    /// <summary>Rotor inertia reflected to the output shaft (J_rotor × ratio²).</summary>
    public double ReflectedInertia => Spec.RotorInertiaKgM2 * Spec.GearRatio * Spec.GearRatio;

    /// <summary>Winding current for an applied average voltage (PWM duty × bus voltage) at shaft speed ω.</summary>
    public double Current(double appliedVolts, double omega) => (appliedVolts - BackEmfConstant * omega) / ResistanceOhm;

    /// <summary>Output torque for a winding current at shaft speed ω, including the gearbox/brush friction.
    /// At (almost) zero speed friction only cancels drive torque, it never reverses it.</summary>
    public double Torque(double current, double omega)
    {
        var drive = TorqueConstant * current;
        if (Math.Abs(omega) > 1e-6)
        {
            return drive - FrictionTorqueNm * Math.Sign(omega);
        }
        return Math.Abs(drive) <= FrictionTorqueNm ? 0 : drive - FrictionTorqueNm * Math.Sign(drive);
    }
}

/// <summary>Electrical state of one motor port after a bus solve.</summary>
public readonly record struct MotorElectrical(double AppliedVolts, double CurrentA, double TorqueNm);

/// <summary>
/// Battery + wiring + all motors on one supply, solved exactly for one tick. For motors driven with duty d_k
/// (d·V_bus applied) and shorted/open motors with zero duty:
///   I_k = (d_k·V − Ke_k·ω_k) / R_k,   I_batt = Σ d_k·I_k + I_q,   V = OCV − R_s·I_batt
/// gives V·(1 + R_s·Σ d_k²/R_k) = OCV − R_s·(I_q − Σ d_k·Ke_k·ω_k/R_k)  (closed form, no iteration).
/// </summary>
public static class PowerBus
{
    public readonly record struct Load(DcMotor Motor, double Duty, double Omega, bool Brake);

    public static (double BusVolts, double BatteryCurrentA, MotorElectrical[] Motors) Solve(Battery battery, IReadOnlyList<Load> loads)
    {
        var rs = battery.SeriesResistanceOhm;
        double sumD2R = 0, sumDKeW = 0;
        foreach (var l in loads)
        {
            sumD2R += l.Duty * l.Duty / l.Motor.ResistanceOhm;
            sumDKeW += l.Duty * l.Motor.BackEmfConstant * l.Omega / l.Motor.ResistanceOhm;
        }
        var ocv = battery.OpenCircuitVolts;
        var volts = (ocv - rs * (battery.Spec.QuiescentCurrentA - sumDKeW)) / (1 + rs * sumD2R);
        var result = new MotorElectrical[loads.Count];
        double batteryCurrent = battery.Spec.QuiescentCurrentA;
        for (var k = 0; k < loads.Count; k++)
        {
            var l = loads[k];
            double applied, current;
            if (l.Duty != 0)
            {
                applied = l.Duty * volts;
                current = l.Motor.Current(applied, l.Omega);
                batteryCurrent += l.Duty * current;
            }
            else if (l.Brake)
            {
                applied = 0;
                current = l.Motor.Current(0, l.Omega);   // shorted windings: dynamic braking, no battery current
            }
            else
            {
                applied = 0;
                current = 0;   // FLOAT: H-bridge open
            }
            result[k] = new MotorElectrical(applied, current, l.Motor.Torque(current, l.Omega));
        }
        return (volts, batteryCurrent, result);
    }
}

/// <summary>Output shaft with inertia, integrated semi-implicitly (spec 04 fixed step). Used by mechanisms
/// that are not simulated as physics joints (e.g. a flywheel in tests) and by the unit tests.</summary>
public sealed class Shaft(double inertiaKgM2)
{
    public double Inertia { get; set; } = inertiaKgM2;
    public double Angle { get; private set; }
    public double Omega { get; private set; }

    public void Step(double torqueNm, double dt)
    {
        Omega += torqueNm / Inertia * dt;
        Angle += Omega * dt;
    }
}
