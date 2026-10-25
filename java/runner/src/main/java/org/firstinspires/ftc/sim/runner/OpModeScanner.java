package org.firstinspires.ftc.sim.runner;

import com.qualcomm.robotcore.eventloop.opmode.Autonomous;
import com.qualcomm.robotcore.eventloop.opmode.Disabled;
import com.qualcomm.robotcore.eventloop.opmode.OpMode;
import com.qualcomm.robotcore.eventloop.opmode.TeleOp;
import java.io.IOException;
import java.lang.reflect.Modifier;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.Enumeration;
import java.util.List;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;

/**
 * Finds the OpModes in a TeamCode jar like the SDK's annotated OpMode registration: concrete
 * subclasses of OpMode annotated with @TeleOp or @Autonomous and not @Disabled. An empty name
 * defaults to the class's simple name, as on the Driver Station.
 */
final class OpModeScanner {
  /** One registered OpMode. */
  record Entry(String className, String name, String group, boolean autonomous, String preselectTeleOp) {}

  private OpModeScanner() {}

  static List<Entry> scan(String jarPath, ClassLoader loader) throws IOException {
    List<Entry> result = new ArrayList<>();
    try (JarFile jar = new JarFile(jarPath)) {
      Enumeration<JarEntry> entries = jar.entries();
      while (entries.hasMoreElements()) {
        String file = entries.nextElement().getName();
        if (!file.endsWith(".class") || file.contains("$")) {
          continue;
        }
        String className = file.substring(0, file.length() - 6).replace('/', '.');
        Class<?> cls;
        try {
          cls = Class.forName(className, false, loader);
        } catch (ClassNotFoundException | LinkageError e) {
          continue;
        }
        if (!OpMode.class.isAssignableFrom(cls) || Modifier.isAbstract(cls.getModifiers()) || cls.isAnnotationPresent(Disabled.class)) {
          continue;
        }
        TeleOp teleOp = cls.getAnnotation(TeleOp.class);
        Autonomous auto = cls.getAnnotation(Autonomous.class);
        if (teleOp != null) {
          result.add(new Entry(className, nameOr(teleOp.name(), cls), teleOp.group(), false, ""));
        } else if (auto != null) {
          result.add(new Entry(className, nameOr(auto.name(), cls), auto.group(), true, auto.preselectTeleOp()));
        }
      }
    }
    result.sort(Comparator.comparing(Entry::name));
    return result;
  }

  private static String nameOr(String name, Class<?> cls) {
    return name == null || name.isEmpty() ? cls.getSimpleName() : name;
  }
}
