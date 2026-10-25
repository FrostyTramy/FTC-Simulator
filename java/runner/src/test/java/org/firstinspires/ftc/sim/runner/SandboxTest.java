package org.firstinspires.ftc.sim.runner;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.Policy;
import java.util.Arrays;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

/**
 * The runner's sandbox (spec 01 "Security") against a team-code jar that tries forbidden operations. The policy only
 * restricts classes from that jar, so the test JVM itself keeps working; the security manager is removed afterwards.
 */
@SuppressWarnings("removal")
class SandboxTest {
  private static Policy previousPolicy;
  private static Class<?> probe;
  private static Path workDir;
  private static Path outsideDir;

  @BeforeAll
  static void installSandbox() throws Exception {
    URL jar = Path.of(System.getProperty("probeJar")).toUri().toURL();
    workDir = Files.createTempDirectory("sandbox-work");
    outsideDir = Files.createTempDirectory("sandbox-outside");
    URLClassLoader loader = new URLClassLoader(new URL[] {jar}, SandboxTest.class.getClassLoader());
    probe = Class.forName("org.firstinspires.ftc.teamcode.SandboxProbe", true, loader);
    previousPolicy = Policy.getPolicy();
    Sandbox.install(jar, workDir);
  }

  @AfterAll
  static void removeSandbox() {
    System.setSecurityManager(null);
    Policy.setPolicy(previousPolicy);
  }

  private static Object call(String method, Object... args) throws Throwable {
    Method m = Arrays.stream(probe.getMethods()).filter(x -> x.getName().equals(method)).findFirst().orElseThrow();
    try {
      return m.invoke(null, args);
    } catch (InvocationTargetException e) {
      throw e.getCause();
    }
  }

  private static void assertDenied(String method, Object... args) {
    Throwable t = assertThrows(Throwable.class, () -> call(method, args), method + " must be denied");
    assertInstanceOf(SecurityException.class, t, method + " failed for another reason: " + t);
  }

  @Test
  void teamCodeCannotStartProcesses() {
    assertDenied("startProcess");
  }

  @Test
  void teamCodeCannotOpenNetworkConnections() {
    assertDenied("connect");
  }

  @Test
  void teamCodeCannotExitTheJvm() {
    assertDenied("exit");
  }

  @Test
  void teamCodeCanOnlyWriteInsideTheWorkDirectory() throws Throwable {
    assertDenied("write", outsideDir.resolve("x.txt").toString());
    assertTrue(Files.notExists(outsideDir.resolve("x.txt")));
    Path inside = workDir.resolve("log.txt");
    call("write", inside.toString());
    assertEquals("written by team code", Files.readString(inside));
  }

  @Test
  void teamCodeCanReadFiles() throws Throwable {
    Path file = outsideDir.resolve("readme.txt");
    Files.writeString(file, "hello"); // written by the test, not by team code
    assertEquals("hello", call("read", file.toString()));
  }

  @Test
  void teamCodeCannotReadEnvironmentVariables() {
    assertDenied("environment");
  }

  @Test
  void teamCodeCannotCreateClassLoadersOrLoadNativeCode() {
    assertDenied("newClassLoader");
    assertDenied("loadNativeLibrary");
  }

  @Test
  void teamCodeCannotTamperWithTheSandbox() {
    assertDenied("privateAccess", Sandbox.class.getName(), "teamCode");
    assertDenied("replaceSecurityManager");
  }

  @Test
  void ordinaryTeamCodeStillWorks() throws Throwable {
    String result = (String) call("ordinaryWork");
    assertTrue(result.endsWith(System.getProperty("java.version")), result);
  }
}
