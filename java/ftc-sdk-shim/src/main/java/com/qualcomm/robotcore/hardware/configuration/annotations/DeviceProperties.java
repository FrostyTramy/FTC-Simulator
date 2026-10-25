package com.qualcomm.robotcore.hardware.configuration.annotations;

import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/** Name and XML tag of a driver class in the robot configuration (SDK 12.0 public API, subset). */
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.TYPE)
public @interface DeviceProperties {
  String name();

  String xmlTag();

  String description() default "";

  boolean builtIn() default false;
}
