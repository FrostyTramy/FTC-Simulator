package android.graphics;

/**
 * Stub of Android's Canvas for VisionProcessor.onDrawFrame (spec 06 "Android stubs"). The simulator does not draw the
 * Robot Controller live view yet; the calls are accepted and ignored.
 */
public class Canvas {
  private final int width;
  private final int height;

  public Canvas(int width, int height) {
    this.width = width;
    this.height = height;
  }

  public int getWidth() {
    return width;
  }

  public int getHeight() {
    return height;
  }

  public void drawLine(float startX, float startY, float stopX, float stopY, Paint paint) {}

  public void drawLines(float[] pts, Paint paint) {}

  public void drawCircle(float cx, float cy, float radius, Paint paint) {}

  public void drawRect(float left, float top, float right, float bottom, Paint paint) {}

  public void drawText(String text, float x, float y, Paint paint) {}

  public void drawColor(int color) {}

  public void drawBitmap(Bitmap bitmap, float left, float top, Paint paint) {}

  public int save() {
    return 1;
  }

  public void restore() {}

  public void rotate(float degrees) {}

  public void rotate(float degrees, float px, float py) {}

  public void translate(float dx, float dy) {}

  public void scale(float sx, float sy) {}
}
