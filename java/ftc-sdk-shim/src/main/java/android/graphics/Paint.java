package android.graphics;

/** Stub of Android's Paint: holds the drawing style processors set up for onDrawFrame (spec 06 "Android stubs"). */
public class Paint {
  /** Fill/stroke style. */
  public enum Style {
    FILL,
    STROKE,
    FILL_AND_STROKE
  }

  /** Text alignment. */
  public enum Align {
    LEFT,
    CENTER,
    RIGHT
  }

  public static final int ANTI_ALIAS_FLAG = 1;
  private int color;
  private float strokeWidth;
  private float textSize;
  private Style style = Style.FILL;
  private Align textAlign = Align.LEFT;
  private boolean antiAlias;

  public Paint() {}

  public Paint(int flags) {
    antiAlias = (flags & ANTI_ALIAS_FLAG) != 0;
  }

  public void setColor(int color) {
    this.color = color;
  }

  public int getColor() {
    return color;
  }

  public void setStrokeWidth(float width) {
    strokeWidth = width;
  }

  public float getStrokeWidth() {
    return strokeWidth;
  }

  public void setTextSize(float size) {
    textSize = size;
  }

  public float getTextSize() {
    return textSize;
  }

  public void setStyle(Style style) {
    this.style = style;
  }

  public Style getStyle() {
    return style;
  }

  public void setTextAlign(Align align) {
    textAlign = align;
  }

  public Align getTextAlign() {
    return textAlign;
  }

  public void setAntiAlias(boolean aa) {
    antiAlias = aa;
  }

  public boolean isAntiAlias() {
    return antiAlias;
  }

  public void setTypeface(Object typeface) {}

  public void setStrokeCap(Object cap) {}

  public void setAlpha(int alpha) {
    color = (color & 0x00FFFFFF) | (alpha << 24);
  }

  /** Width of {@code text} with a rough average glyph width (the simulator draws no text yet). */
  public float measureText(String text) {
    return text.length() * textSize * 0.55f;
  }
}
