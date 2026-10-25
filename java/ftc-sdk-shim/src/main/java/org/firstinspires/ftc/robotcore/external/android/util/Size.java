package org.firstinspires.ftc.robotcore.external.android.util;

/** The SDK's own copy of Android's Size, used by camera calibrations (SDK 12.0 public API). */
public final class Size {
  private final int width;
  private final int height;

  public Size(int width, int height) {
    this.width = width;
    this.height = height;
  }

  public int getWidth() {
    return width;
  }

  public int getHeight() {
    return height;
  }

  @Override
  public boolean equals(Object o) {
    return o instanceof Size && ((Size) o).width == width && ((Size) o).height == height;
  }

  @Override
  public String toString() {
    return width + "x" + height;
  }

  public static Size parseSize(String string) throws NumberFormatException {
    android.util.Size s = android.util.Size.parseSize(string);
    return new Size(s.getWidth(), s.getHeight());
  }

  @Override
  public int hashCode() {
    return height ^ ((width << 16) | (width >>> 16));
  }
}
