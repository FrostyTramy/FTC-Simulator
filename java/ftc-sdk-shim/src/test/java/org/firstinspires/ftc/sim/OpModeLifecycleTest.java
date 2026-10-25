package org.firstinspires.ftc.sim;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTimeoutPreemptively;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.qualcomm.robotcore.eventloop.opmode.LinearOpMode;
import com.qualcomm.robotcore.eventloop.opmode.OpMode;
import com.qualcomm.robotcore.hardware.DcMotor;
import com.qualcomm.robotcore.hardware.Gamepad;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

/** OpMode and LinearOpMode lifecycle, stop semantics and crash reporting (spec 06), on virtual time. */
class OpModeLifecycleTest {
  private static final long TICK = 1_000_000_000L / 240;
  static final List<String> calls = new ArrayList<>();

  /** Iterative OpMode recording its lifecycle calls. */
  public static class Recorder extends OpMode {
    @Override
    public void init() {
      calls.add("init");
    }

    @Override
    public void init_loop() {
      if (!calls.contains("init_loop")) {
        calls.add("init_loop");
      }
    }

    @Override
    public void start() {
      calls.add("start");
    }

    @Override
    public void loop() {
      if (!calls.contains("loop")) {
        calls.add("loop");
      }
      telemetry.addData("stick", gamepad1.left_stick_y);
      hardwareMap.get(DcMotor.class, "left_front_drive").setPower(-gamepad1.left_stick_y);
    }

    @Override
    public void stop() {
      calls.add("stop");
    }
  }

  /** LinearOpMode that drives for 1 s after START. */
  public static class TimedDrive extends LinearOpMode {
    @Override
    public void runOpMode() {
      DcMotor m = hardwareMap.get(DcMotor.class, "left_front_drive");
      waitForStart();
      double startTime = getRuntime();
      m.setPower(0.5);
      sleep(1000);
      m.setPower(0);
      calls.add(String.format("slept %.3f", getRuntime() - startTime));
      while (opModeIsActive()) {
        idle();
      }
      calls.add("left loop");
    }
  }

  /** LinearOpMode that ignores STOP (catches the interrupt and keeps going). */
  public static class Stubborn extends LinearOpMode {
    @Override
    public void runOpMode() {
      waitForStart();
      while (true) {
        sleep(10);
        Thread.interrupted(); // swallows the stop request, a classic mistake
      }
    }
  }

  /** LinearOpMode whose telemetry value producer reads an encoder: it spends virtual time while rendering. */
  public static class EncoderTelemetry extends LinearOpMode {
    @Override
    public void runOpMode() {
      DcMotor m = hardwareMap.get(DcMotor.class, "left_front_drive");
      telemetry.addData("pos", () -> m.getCurrentPosition());
      waitForStart();
      while (opModeIsActive()) {
        telemetry.update();
      }
    }
  }

  /** OpMode whose loop throws. */
  public static class Crasher extends OpMode {
    @Override
    public void init() {}

    @Override
    public void loop() {
      Object nothing = null;
      nothing.toString();
    }
  }

  private static OpModeDriver driver(SimRuntime runtime, Class<? extends OpMode> cls) {
    SimRobot robot = ShimTestSupport.starterBot(runtime);
    return new OpModeDriver(runtime, cls, robot.hardwareMap(), new SimTelemetry(runtime));
  }

  private static long run(OpModeDriver d, long fromTick, int ticks) throws InterruptedException {
    long t = fromTick;
    for (int i = 0; i < ticks; i++) {
      t++;
      assertTrue(d.runUntil(t * TICK, 5_000));
    }
    return t;
  }

  @Test
  void iterativeOpModeFollowsTheSdkLifecycle() throws InterruptedException {
    calls.clear();
    SimRuntime runtime = ShimTestSupport.freshRuntime();
    OpModeDriver d = driver(runtime, Recorder.class);
    d.init();
    long t = run(d, 0, 10);
    d.start();
    Gamepad g = new Gamepad();
    g.left_stick_y = -0.8f; // stick pushed forward
    d.setGamepads(g, new Gamepad());
    t = run(d, t, 10);
    assertEquals(0.8, runtime.bus().motorCommands().get("0:0").power, 1e-6);
    d.stop();
    run(d, t, 5);
    assertEquals(List.of("init", "init_loop", "start", "loop", "stop"), calls);
    assertEquals(OpModeDriver.State.STOPPED, d.state());
  }

  @Test
  void linearOpModeSleepsOnVirtualTimeAndStopsCleanly() throws InterruptedException {
    calls.clear();
    SimRuntime runtime = ShimTestSupport.freshRuntime();
    OpModeDriver d = driver(runtime, TimedDrive.class);
    d.init();
    long t = run(d, 0, 24); // 0.1 s in INIT: waitForStart blocks
    assertEquals(0.0, runtime.bus().motorCommands().get("0:0").power);
    d.start();
    t = run(d, t, 120); // 0.5 s: still sleeping, motor on
    assertEquals(0.5, runtime.bus().motorCommands().get("0:0").power);
    t = run(d, t, 240); // past 1 s: motor off
    assertEquals(0.0, runtime.bus().motorCommands().get("0:0").power);
    assertTrue(calls.get(0).startsWith("slept 1.00"), calls.toString());
    d.stop();
    run(d, t, 5);
    assertEquals(OpModeDriver.State.STOPPED, d.state());
    assertEquals("left loop", calls.get(calls.size() - 1));
  }

  @Test
  void linearOpModeThatIgnoresStopIsReportedAfter900Ms() throws InterruptedException {
    SimRuntime runtime = ShimTestSupport.freshRuntime();
    OpModeDriver d = driver(runtime, Stubborn.class);
    d.init();
    long t = run(d, 0, 2);
    d.start();
    t = run(d, t, 10);
    d.stop();
    for (int i = 0; i < 260 && d.state() != OpModeDriver.State.CRASHED; i++) {
      t++;
      d.runUntil(t * TICK, 5_000);
    }
    assertEquals(OpModeDriver.State.CRASHED, d.state());
    assertTrue(d.error().contains("900 ms"), d.error());
  }

  @Test
  void uncaughtExceptionStopsTheOpModeWithTheDriverStationMessage() throws InterruptedException {
    SimRuntime runtime = ShimTestSupport.freshRuntime();
    OpModeDriver d = driver(runtime, Crasher.class);
    d.init();
    long t = run(d, 0, 2);
    d.start();
    run(d, t, 5);
    assertEquals(OpModeDriver.State.CRASHED, d.state());
    assertTrue(d.error().startsWith("User code threw an uncaught exception: NullPointerException"), d.error());
  }

  @Test
  void runnerCanReadTelemetryWhileTheOpModeIsParkedInsideAValueProducer() {
    assertTimeoutPreemptively(java.time.Duration.ofSeconds(20), () -> {
      SimRuntime runtime = ShimTestSupport.freshRuntime();
      SimRobot robot = ShimTestSupport.starterBot(runtime);
      SimTelemetry telemetry = new SimTelemetry(runtime);
      OpModeDriver d = new OpModeDriver(runtime, EncoderTelemetry.class, robot.hardwareMap(), telemetry);
      d.init();
      long t = 0;
      for (int i = 0; i < 480; i++) { // 2 s
        if (i == 5) {
          d.start();
        }
        t++;
        assertTrue(d.runUntil(t * TICK, 5_000));
        d.eventLoopIteration();
        telemetry.transmissions(); // the runner's reads must never wait for the OpMode thread
        telemetry.lastTransmitted();
      }
      assertEquals(OpModeDriver.State.RUNNING, d.state());
      assertTrue(telemetry.transmissions() >= 7, "transmissions: " + telemetry.transmissions());
      assertEquals(List.of("pos : 0"), telemetry.lastTransmitted());
    });
  }

  @Test
  void sameInputsGiveTheSameCommandsAndTimes() throws InterruptedException {
    String a = traceOfTimedDrive();
    String b = traceOfTimedDrive();
    assertEquals(a, b);
  }

  private static String traceOfTimedDrive() throws InterruptedException {
    calls.clear();
    SimRuntime runtime = ShimTestSupport.freshRuntime();
    OpModeDriver d = driver(runtime, TimedDrive.class);
    d.init();
    StringBuilder trace = new StringBuilder();
    long t = 0;
    for (int i = 0; i < 400; i++) {
      if (i == 20) {
        d.start();
      }
      t++;
      d.runUntil(t * TICK, 5_000);
      trace.append(runtime.nowNanos()).append(':').append(runtime.bus().motorCommands().get("0:0").power).append(';');
    }
    d.stop();
    d.runUntil((t + 1) * TICK, 5_000);
    return trace.toString();
  }
}
