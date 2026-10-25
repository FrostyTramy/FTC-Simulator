package org.firstinspires.ftc.teamcode;

import java.io.File;
import java.lang.reflect.Field;
import java.net.Socket;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.file.Files;
import java.nio.file.Path;

/** Team code that tries what the sandbox must allow or forbid (runner SandboxTest calls each method). */
public final class SandboxProbe {
  private SandboxProbe() {}

  public static void startProcess() throws Exception {
    new ProcessBuilder("cmd", "/c", "echo", "escaped").start();
  }

  public static void connect() throws Exception {
    try (Socket s = new Socket("127.0.0.1", 9)) {
      s.getOutputStream().write(1);
    }
  }

  public static void exit() {
    System.exit(3);
  }

  public static void write(String file) throws Exception {
    Files.writeString(Path.of(file), "written by team code");
  }

  public static String read(String file) throws Exception {
    return Files.readString(Path.of(file));
  }

  public static String environment() {
    return System.getenv("PATH");
  }

  public static void newClassLoader() throws Exception {
    try (URLClassLoader loader = new URLClassLoader(new URL[] {new File(".").toURI().toURL()})) {
      loader.getURLs();
    }
  }

  public static void privateAccess(String className, String field) throws Exception {
    Field f = Class.forName(className).getDeclaredField(field);
    f.setAccessible(true);
  }

  public static void replaceSecurityManager() {
    System.setSecurityManager(null);
  }

  public static void loadNativeLibrary() {
    System.loadLibrary("kernel32");
  }

  /** Ordinary things team code does: threads, reflection on its own classes, system properties. */
  public static String ordinaryWork() throws Exception {
    Thread t = new Thread(() -> {});
    t.start();
    t.join();
    return SandboxProbe.class.getDeclaredMethods().length + " " + System.getProperty("java.version");
  }
}
