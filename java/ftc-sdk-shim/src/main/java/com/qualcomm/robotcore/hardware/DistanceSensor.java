package com.qualcomm.robotcore.hardware;

import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;

/** A sensor that measures distance (SDK 12.0 public API). */
public interface DistanceSensor extends HardwareDevice {
  double distanceOutOfRange = Double.MAX_VALUE;

  double getDistance(DistanceUnit unit);
}
