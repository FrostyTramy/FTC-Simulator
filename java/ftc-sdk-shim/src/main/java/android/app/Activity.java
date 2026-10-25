package android.app;

import android.content.Context;
import android.view.View;
import java.util.HashMap;
import java.util.Map;

/**
 * Stub of Android's Activity: the Robot Controller app screen. Views found by id accept the calls team code makes on
 * them (post, setBackgroundColor); the simulator keeps their state but shows no Robot Controller screen.
 */
public class Activity extends Context {
  private final Map<Integer, View> views = new HashMap<>();

  public synchronized View findViewById(int id) {
    return views.computeIfAbsent(id, View::new);
  }

  public void runOnUiThread(Runnable action) {
    action.run();
  }
}
