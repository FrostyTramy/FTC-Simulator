package com.qualcomm.robotcore.hardware;

/** Colour channels scaled to 0..1 (SDK 12.0 public API). */
public class NormalizedRGBA {
  public float red;
  public float green;
  public float blue;
  public float alpha;

  public NormalizedRGBA() {}

  /** Android colour int (alpha, red, green, blue bytes). */
  public int toColor() {
    return (byte8(alpha) << 24) | (byte8(red) << 16) | (byte8(green) << 8) | byte8(blue);
  }

  private static int byte8(float v) {
    return (int) (Math.max(0f, Math.min(1f, v)) * 255) & 0xFF;
  }
}
