package android.view;

/** Stub of an Android View on the Robot Controller screen (spec 06 "Android stubs"). */
public class View {
  private final int id;
  private volatile int backgroundColor;
  private volatile int visibility;

  public static final int VISIBLE = 0;
  public static final int INVISIBLE = 4;
  public static final int GONE = 8;

  public View(int id) {
    this.id = id;
  }

  public int getId() {
    return id;
  }

  /** Android queues the action on the UI thread; the simulator runs it at once (it only changes view state). */
  public boolean post(Runnable action) {
    action.run();
    return true;
  }

  public void setBackgroundColor(int color) {
    backgroundColor = color;
  }

  /** The last background colour team code set (for a future Robot Controller screen preview). */
  public int getBackgroundColorForSimulator() {
    return backgroundColor;
  }

  public void setVisibility(int visibility) {
    this.visibility = visibility;
  }

  public int getVisibility() {
    return visibility;
  }
}
