package com.qualcomm.robotcore.hardware;

/** A light that can be switched (SDK 12.0 public API). */
public interface SwitchableLight extends Light {
  void enableLight(boolean enable);
}
