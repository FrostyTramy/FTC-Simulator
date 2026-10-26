package com.bylazar.configurables.annotations;

import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * Panels (com.bylazar, D-039): marks a class whose public static fields Panels shows and lets the team edit while the
 * robot runs. The simulator has no Panels web page yet: the fields keep their values.
 */
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.TYPE)
public @interface Configurable {}
