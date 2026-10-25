package android.graphics;

/** Stub of an Android bitmap: size and ARGB pixels (spec 06 "Android stubs"). */
public class Bitmap {
  /** Pixel formats (only ARGB_8888 is used by the SDK vision code). */
  public enum Config {
    ALPHA_8,
    RGB_565,
    ARGB_4444,
    ARGB_8888
  }

  private final int width;
  private final int height;
  private final int[] pixels;

  private Bitmap(int width, int height) {
    this.width = width;
    this.height = height;
    this.pixels = new int[width * height];
  }

  public static Bitmap createBitmap(int width, int height, Config config) {
    return new Bitmap(width, height);
  }

  public int getWidth() {
    return width;
  }

  public int getHeight() {
    return height;
  }

  public int getPixel(int x, int y) {
    return pixels[y * width + x];
  }

  public void setPixel(int x, int y, int color) {
    pixels[y * width + x] = color;
  }

  public void recycle() {}
}
