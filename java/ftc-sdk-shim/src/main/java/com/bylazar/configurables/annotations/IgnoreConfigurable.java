package com.bylazar.configurables.annotations;

import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/** Panels (com.bylazar, D-039): a static field of a {@link Configurable} class that Panels does not show. */
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.FIELD)
public @interface IgnoreConfigurable {}
