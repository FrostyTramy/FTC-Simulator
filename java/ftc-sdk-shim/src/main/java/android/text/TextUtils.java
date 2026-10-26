package android.text;

/** Android's TextUtils (the parts team code uses on the Robot Controller). */
public class TextUtils {
  public static boolean isEmpty(CharSequence s) {
    return s == null || s.length() == 0;
  }

  public static boolean equals(CharSequence a, CharSequence b) {
    return a == b || (a != null && b != null && a.toString().contentEquals(b));
  }

  public static String join(CharSequence delimiter, Iterable<?> tokens) {
    StringBuilder sb = new StringBuilder();
    for (Object token : tokens) {
      if (sb.length() > 0) {
        sb.append(delimiter);
      }
      sb.append(token);
    }
    return sb.toString();
  }

  public static String join(CharSequence delimiter, Object[] tokens) {
    return join(delimiter, java.util.Arrays.asList(tokens));
  }
}
