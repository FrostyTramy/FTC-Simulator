package com.qualcomm.robotcore.hardware;

import java.util.Locale;

/** P, I and D gains (SDK 12.0 public API). */
public class PIDCoefficients {
  public double p;
  public double i;
  public double d;

  public PIDCoefficients() {}

  public PIDCoefficients(double p, double i, double d) {
    this.p = p;
    this.i = i;
    this.d = d;
  }

  @Override
  public String toString() {
    return String.format(Locale.US, "%s(p=%f i=%f d=%f)", getClass().getSimpleName(), p, i, d);
  }
}
