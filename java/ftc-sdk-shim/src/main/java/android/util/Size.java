package android.util;

/** Android Size (width, height in pixels), as used by the SDK vision API. */
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
  public int hashCode() {
    return height ^ ((width << 16) | (width >>> 16));
  }

  @Override
  public String toString() {
    return width + "x" + height;
  }

  public static Size parseSize(String string) {
    int x = string.indexOf('x') >= 0 ? string.indexOf('x') : string.indexOf('*');
    if (x < 0) {
      throw new NumberFormatException("Invalid Size: \"" + string + "\"");
    }
    return new Size(Integer.parseInt(string.substring(0, x)), Integer.parseInt(string.substring(x + 1)));
  }
}
