package com.qualcomm.robotcore.hardware.configuration.annotations;

import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/** Marks an I2C driver class for the robot configuration (SDK 12.0 public API). */
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.TYPE)
public @interface I2cDeviceType {}
