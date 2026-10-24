package com.qualcomm.robotcore.eventloop.opmode;

import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/** Registers an autonomous OpMode; {@link #preselectTeleOp()} is selected on the DS after it stops (SDK 12.0). */
@Documented
@Target(ElementType.TYPE)
@Retention(RetentionPolicy.RUNTIME)
public @interface Autonomous {
  String name() default "";

  String group() default "";

  String preselectTeleOp() default "";
}
