package com.qualcomm.robotcore.hardware;

/** A colour sensor that also measures distance (SDK 12.0 public API). */
public interface ColorRangeSensor
    extends ColorSensor, NormalizedColorSensor, DistanceSensor, OpticalDistanceSensor, LightSensor {}
