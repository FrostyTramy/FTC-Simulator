package com.acmerobotics.dashboard.config;

import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * FTC Dashboard (D-039): marks a class whose public static fields the dashboard lets the team edit live. The simulator
 * has no dashboard page yet: the fields keep their values.
 */
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.TYPE)
public @interface Config {
  /** Name shown in the dashboard (default: the class name). */
  String value() default "";
}
