package android.content.res;

import java.util.LinkedHashMap;
import java.util.Map;

/** Stub of Android's Resources: every (name, type) gets a stable non-zero id, like the RC app's generated R class. */
public class Resources {
  private final Map<String, Integer> ids = new LinkedHashMap<>();

  public synchronized int getIdentifier(String name, String defType, String defPackage) {
    return ids.computeIfAbsent(defType + "/" + name, k -> 0x7f0b0000 + ids.size() + 1);
  }
}
