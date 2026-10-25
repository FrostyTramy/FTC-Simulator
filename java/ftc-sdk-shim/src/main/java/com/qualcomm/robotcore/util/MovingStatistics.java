package com.qualcomm.robotcore.util;

import java.util.ArrayDeque;

/** Mean and standard deviation of the last N samples (SDK 12.0 public API). */
public class MovingStatistics {
  private final int capacity;
  private final ArrayDeque<Double> samples = new ArrayDeque<>();
  private double sum;
  private double sumSquares;

  public MovingStatistics(int capacity) {
    if (capacity <= 0) {
      throw new IllegalArgumentException("MovingStatistics capacity must be positive");
    }
    this.capacity = capacity;
  }

  public int getCount() {
    return samples.size();
  }

  public double getMean() {
    return samples.isEmpty() ? 0 : sum / samples.size();
  }

  public double getVariance() {
    int n = samples.size();
    return n < 2 ? 0 : Math.max(0, (sumSquares - sum * sum / n) / (n - 1));
  }

  public double getStandardDeviation() {
    return Math.sqrt(getVariance());
  }

  public void clear() {
    samples.clear();
    sum = 0;
    sumSquares = 0;
  }

  public void add(double x) {
    samples.addLast(x);
    sum += x;
    sumSquares += x * x;
    if (samples.size() > capacity) {
      double old = samples.removeFirst();
      sum -= old;
      sumSquares -= old * old;
    }
  }
}
