package com.qualcomm.robotcore.hardware;

import java.util.Locale;

/** P, I, D and F gains plus the control algorithm (SDK 12.0 public API). */
public class PIDFCoefficients {
  public double p;
  public double i;
  public double d;
  public double f;
  public MotorControlAlgorithm algorithm = MotorControlAlgorithm.PIDF;

  public PIDFCoefficients() {}

  public PIDFCoefficients(double p, double i, double d, double f, MotorControlAlgorithm algorithm) {
    this.p = p;
    this.i = i;
    this.d = d;
    this.f = f;
    this.algorithm = algorithm;
  }

  public PIDFCoefficients(double p, double i, double d, double f) {
    this(p, i, d, f, MotorControlAlgorithm.PIDF);
  }

  public PIDFCoefficients(PIDFCoefficients other) {
    this(other.p, other.i, other.d, other.f, other.algorithm);
  }

  public PIDFCoefficients(PIDCoefficients other) {
    this(other.p, other.i, other.d, 0, MotorControlAlgorithm.LegacyPID);
  }

  @Override
  public String toString() {
    return String.format(Locale.US, "%s(p=%f i=%f d=%f f=%f alg=%s)", getClass().getSimpleName(), p, i, d, f, algorithm);
  }
}
