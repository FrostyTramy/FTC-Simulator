package com.qualcomm.robotcore.hardware;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * A Driver Station gamepad (SDK 12.0 public API). Sticks: −1..1 with Y up = −1; triggers 0..1. PS
 * aliases (cross = a, circle = b, square = x, triangle = y, share = back, options = start, ps = guide)
 * are kept in sync. Edge-detection methods (xxxWasPressed/WasReleased) report whether the button went
 * down/up since the previous call of that method. Rumble and LED effects are recorded for the
 * simulator (the DS window shows them).
 */
public class Gamepad {
  public static final int ID_UNASSOCIATED = -1;
  public static final int ID_SYNTHETIC = -2;
  public static final float DEFAULT_TRIGGER_THRESHOLD = 0.5f;
  public static final int LED_DURATION_CONTINUOUS = -1;
  public static final int RUMBLE_DURATION_CONTINUOUS = -1;

  /** Controller families the DS distinguishes. */
  public enum Type {
    UNKNOWN,
    LOGITECH_F310,
    XBOX_360,
    SONY_PS4,
    SONY_PS4_SUPPORTED_BY_KERNEL
  }

  public volatile Type type = Type.UNKNOWN;
  public volatile float left_stick_x;
  public volatile float left_stick_y;
  public volatile float right_stick_x;
  public volatile float right_stick_y;
  public volatile boolean dpad_up;
  public volatile boolean dpad_down;
  public volatile boolean dpad_left;
  public volatile boolean dpad_right;
  public volatile boolean a;
  public volatile boolean b;
  public volatile boolean x;
  public volatile boolean y;
  public volatile boolean guide;
  public volatile boolean start;
  public volatile boolean back;
  public volatile boolean left_bumper;
  public volatile boolean right_bumper;
  public volatile boolean left_stick_button;
  public volatile boolean right_stick_button;
  public volatile float left_trigger;
  public volatile float right_trigger;
  public volatile boolean left_trigger_pressed;
  public volatile boolean right_trigger_pressed;
  public volatile boolean circle;
  public volatile boolean cross;
  public volatile boolean triangle;
  public volatile boolean square;
  public volatile boolean share;
  public volatile boolean options;
  public volatile boolean touchpad;
  public volatile boolean touchpad_finger_1;
  public volatile boolean touchpad_finger_2;
  public volatile float touchpad_finger_1_x;
  public volatile float touchpad_finger_1_y;
  public volatile float touchpad_finger_2_x;
  public volatile float touchpad_finger_2_y;
  public volatile boolean ps;
  public volatile int id = ID_UNASSOCIATED;
  public volatile long timestamp;

  private float triggerThreshold = DEFAULT_TRIGGER_THRESHOLD;
  private final List<RumbleEffect> rumbleRequests = new ArrayList<>();
  private final List<LedEffect> ledRequests = new ArrayList<>();
  private long rumbleUntilNanos;

  // Edge latches per button: bit set = event not yet reported. Order = Button enum.
  private long pressedLatch;
  private long releasedLatch;
  private long lastButtons;

  private enum Button {
    DPAD_UP, DPAD_DOWN, DPAD_LEFT, DPAD_RIGHT, A, B, X, Y, GUIDE, START, BACK, LEFT_BUMPER, RIGHT_BUMPER,
    LEFT_STICK_BUTTON, RIGHT_STICK_BUTTON, CIRCLE, CROSS, TRIANGLE, SQUARE, SHARE, OPTIONS, TOUCHPAD, PS,
    LEFT_TRIGGER, RIGHT_TRIGGER
  }

  public Gamepad() {}

  public void setTriggerThreshold(float threshold) {
    triggerThreshold = threshold;
    left_trigger_pressed = left_trigger > triggerThreshold;
    right_trigger_pressed = right_trigger > triggerThreshold;
  }

  public float getTriggerThreshold() {
    return triggerThreshold;
  }

  public void setGamepadId(int id) {
    this.id = id;
  }

  public int getGamepadId() {
    return id;
  }

  public void setTimestamp(long timestamp) {
    this.timestamp = timestamp;
  }

  public void refreshTimestamp() {
    timestamp = org.firstinspires.ftc.sim.SimRuntime.get().nowNanos();
  }

  /** Copies the state of another gamepad and updates the edge latches (how new DS data arrives). */
  public void copy(Gamepad g) {
    type = g.type;
    left_stick_x = g.left_stick_x;
    left_stick_y = g.left_stick_y;
    right_stick_x = g.right_stick_x;
    right_stick_y = g.right_stick_y;
    dpad_up = g.dpad_up;
    dpad_down = g.dpad_down;
    dpad_left = g.dpad_left;
    dpad_right = g.dpad_right;
    a = cross = g.a || g.cross;
    b = circle = g.b || g.circle;
    x = square = g.x || g.square;
    y = triangle = g.y || g.triangle;
    guide = ps = g.guide || g.ps;
    start = options = g.start || g.options;
    back = share = g.back || g.share;
    left_bumper = g.left_bumper;
    right_bumper = g.right_bumper;
    left_stick_button = g.left_stick_button;
    right_stick_button = g.right_stick_button;
    left_trigger = g.left_trigger;
    right_trigger = g.right_trigger;
    left_trigger_pressed = left_trigger > triggerThreshold;
    right_trigger_pressed = right_trigger > triggerThreshold;
    touchpad = g.touchpad;
    touchpad_finger_1 = g.touchpad_finger_1;
    touchpad_finger_2 = g.touchpad_finger_2;
    touchpad_finger_1_x = g.touchpad_finger_1_x;
    touchpad_finger_1_y = g.touchpad_finger_1_y;
    touchpad_finger_2_x = g.touchpad_finger_2_x;
    touchpad_finger_2_y = g.touchpad_finger_2_y;
    id = g.id;
    timestamp = g.timestamp;
    updateEdges();
  }

  /** All controls released. */
  public void reset() {
    copy(new Gamepad());
  }

  private long buttons() {
    boolean[] b = {
      dpad_up, dpad_down, dpad_left, dpad_right, a, this.b, x, y, guide, start, back, left_bumper, right_bumper,
      left_stick_button, right_stick_button, circle, cross, triangle, square, share, options, touchpad, ps,
      left_trigger_pressed, right_trigger_pressed
    };
    long bits = 0;
    for (int i = 0; i < b.length; i++) {
      if (b[i]) {
        bits |= 1L << i;
      }
    }
    return bits;
  }

  private void updateEdges() {
    long now = buttons();
    pressedLatch |= now & ~lastButtons;
    releasedLatch |= ~now & lastButtons;
    lastButtons = now;
  }

  private boolean take(boolean pressed, Button button) {
    long bit = 1L << button.ordinal();
    if (pressed) {
      boolean hit = (pressedLatch & bit) != 0;
      pressedLatch &= ~bit;
      return hit;
    }
    boolean hit = (releasedLatch & bit) != 0;
    releasedLatch &= ~bit;
    return hit;
  }

  public void resetEdgeDetection() {
    pressedLatch = 0;
    releasedLatch = 0;
  }

  /** True when every stick and trigger is at rest and no button is pressed. */
  public boolean atRest() {
    return left_stick_x == 0 && left_stick_y == 0 && right_stick_x == 0 && right_stick_y == 0 && left_trigger == 0
        && right_trigger == 0;
  }

  public Type type() {
    return type;
  }

  @Override
  public String toString() {
    return String.format(Locale.US, "ID: %d user: lx: %.2f ly: %.2f rx: %.2f ry: %.2f lt: %.2f rt: %.2f", id, left_stick_x,
        left_stick_y, right_stick_x, right_stick_y, left_trigger, right_trigger);
  }

  // ------------------------------------------------------------------ rumble and LED

  /** A rumble pattern (SDK RumbleEffect, simplified: steps of left/right strength and duration). */
  public static class RumbleEffect {
    public final List<Step> steps;

    public RumbleEffect(List<Step> steps) {
      this.steps = steps;
    }

    public static class Step {
      public final double large;
      public final double small;
      public final int duration;

      public Step(double large, double small, int duration) {
        this.large = large;
        this.small = small;
        this.duration = duration;
      }
    }

    /** Builder like the SDK's RumbleEffect.Builder. */
    public static class Builder {
      private final List<Step> steps = new ArrayList<>();

      public Builder addStep(double rumble1, double rumble2, int durationMs) {
        steps.add(new Step(rumble1, rumble2, durationMs));
        return this;
      }

      public RumbleEffect build() {
        return new RumbleEffect(new ArrayList<>(steps));
      }
    }
  }

  /** An LED pattern (SDK LedEffect, simplified). */
  public static class LedEffect {
    public final List<double[]> steps;
    public final boolean repeating;

    public LedEffect(List<double[]> steps, boolean repeating) {
      this.steps = steps;
      this.repeating = repeating;
    }

    /** Builder like the SDK's LedEffect.Builder. */
    public static class Builder {
      private final List<double[]> steps = new ArrayList<>();
      private boolean repeating;

      public Builder addStep(double r, double g, double b, int durationMs) {
        steps.add(new double[] {r, g, b, durationMs});
        return this;
      }

      public Builder setRepeating(boolean repeating) {
        this.repeating = repeating;
        return this;
      }

      public LedEffect build() {
        return new LedEffect(new ArrayList<>(steps), repeating);
      }
    }
  }

  public void setLedColor(double r, double g, double b, int durationMs) {
    runLedEffect(new LedEffect.Builder().addStep(r, g, b, durationMs).build());
  }

  public void runLedEffect(LedEffect effect) {
    synchronized (ledRequests) {
      ledRequests.add(effect);
    }
  }

  public void runRumbleEffect(RumbleEffect effect) {
    int total = 0;
    for (RumbleEffect.Step s : effect.steps) {
      total += s.duration;
    }
    queueRumble(effect, total);
  }

  public void rumble(int durationMs) {
    rumble(1.0, 1.0, durationMs);
  }

  public void rumble(double rumble1, double rumble2, int durationMs) {
    queueRumble(new RumbleEffect.Builder().addStep(rumble1, rumble2, durationMs).build(), durationMs);
  }

  public void stopRumble() {
    queueRumble(new RumbleEffect.Builder().addStep(0, 0, 0).build(), 0);
    rumbleUntilNanos = 0;
  }

  /** Short pulses, like the SDK (75 ms on, 125 ms off per blip). */
  public void rumbleBlips(int count) {
    RumbleEffect.Builder builder = new RumbleEffect.Builder();
    for (int i = 0; i < count; i++) {
      builder.addStep(1.0, 0, 75).addStep(0, 0, 125);
    }
    runRumbleEffect(builder.build());
  }

  public boolean isRumbling() {
    return org.firstinspires.ftc.sim.SimRuntime.get().nowNanos() < rumbleUntilNanos;
  }

  private void queueRumble(RumbleEffect effect, int totalMs) {
    synchronized (rumbleRequests) {
      rumbleRequests.add(effect);
    }
    long now = org.firstinspires.ftc.sim.SimRuntime.get().nowNanos();
    rumbleUntilNanos = totalMs == RUMBLE_DURATION_CONTINUOUS ? Long.MAX_VALUE : now + totalMs * 1_000_000L;
  }

  /** Simulator hook: rumble effects requested since the last call. */
  public List<RumbleEffect> internalSimDrainRumble() {
    synchronized (rumbleRequests) {
      List<RumbleEffect> out = new ArrayList<>(rumbleRequests);
      rumbleRequests.clear();
      return out;
    }
  }

  /** Simulator hook: LED effects requested since the last call. */
  public List<LedEffect> internalSimDrainLed() {
    synchronized (ledRequests) {
      List<LedEffect> out = new ArrayList<>(ledRequests);
      ledRequests.clear();
      return out;
    }
  }

  // ------------------------------------------------------------------ edge detection (SDK 10.3+)

  public boolean dpadUpWasPressed() { return take(true, Button.DPAD_UP); }
  public boolean dpadUpWasReleased() { return take(false, Button.DPAD_UP); }
  public boolean dpadDownWasPressed() { return take(true, Button.DPAD_DOWN); }
  public boolean dpadDownWasReleased() { return take(false, Button.DPAD_DOWN); }
  public boolean dpadLeftWasPressed() { return take(true, Button.DPAD_LEFT); }
  public boolean dpadLeftWasReleased() { return take(false, Button.DPAD_LEFT); }
  public boolean dpadRightWasPressed() { return take(true, Button.DPAD_RIGHT); }
  public boolean dpadRightWasReleased() { return take(false, Button.DPAD_RIGHT); }
  public boolean aWasPressed() { return take(true, Button.A); }
  public boolean aWasReleased() { return take(false, Button.A); }
  public boolean bWasPressed() { return take(true, Button.B); }
  public boolean bWasReleased() { return take(false, Button.B); }
  public boolean xWasPressed() { return take(true, Button.X); }
  public boolean xWasReleased() { return take(false, Button.X); }
  public boolean yWasPressed() { return take(true, Button.Y); }
  public boolean yWasReleased() { return take(false, Button.Y); }
  public boolean guideWasPressed() { return take(true, Button.GUIDE); }
  public boolean guideWasReleased() { return take(false, Button.GUIDE); }
  public boolean startWasPressed() { return take(true, Button.START); }
  public boolean startWasReleased() { return take(false, Button.START); }
  public boolean backWasPressed() { return take(true, Button.BACK); }
  public boolean backWasReleased() { return take(false, Button.BACK); }
  public boolean leftBumperWasPressed() { return take(true, Button.LEFT_BUMPER); }
  public boolean leftBumperWasReleased() { return take(false, Button.LEFT_BUMPER); }
  public boolean rightBumperWasPressed() { return take(true, Button.RIGHT_BUMPER); }
  public boolean rightBumperWasReleased() { return take(false, Button.RIGHT_BUMPER); }
  public boolean leftStickButtonWasPressed() { return take(true, Button.LEFT_STICK_BUTTON); }
  public boolean leftStickButtonWasReleased() { return take(false, Button.LEFT_STICK_BUTTON); }
  public boolean rightStickButtonWasPressed() { return take(true, Button.RIGHT_STICK_BUTTON); }
  public boolean rightStickButtonWasReleased() { return take(false, Button.RIGHT_STICK_BUTTON); }
  public boolean circleWasPressed() { return take(true, Button.CIRCLE); }
  public boolean circleWasReleased() { return take(false, Button.CIRCLE); }
  public boolean crossWasPressed() { return take(true, Button.CROSS); }
  public boolean crossWasReleased() { return take(false, Button.CROSS); }
  public boolean triangleWasPressed() { return take(true, Button.TRIANGLE); }
  public boolean triangleWasReleased() { return take(false, Button.TRIANGLE); }
  public boolean squareWasPressed() { return take(true, Button.SQUARE); }
  public boolean squareWasReleased() { return take(false, Button.SQUARE); }
  public boolean shareWasPressed() { return take(true, Button.SHARE); }
  public boolean shareWasReleased() { return take(false, Button.SHARE); }
  public boolean optionsWasPressed() { return take(true, Button.OPTIONS); }
  public boolean optionsWasReleased() { return take(false, Button.OPTIONS); }
  public boolean touchpadWasPressed() { return take(true, Button.TOUCHPAD); }
  public boolean touchpadWasReleased() { return take(false, Button.TOUCHPAD); }
  public boolean psWasPressed() { return take(true, Button.PS); }
  public boolean psWasReleased() { return take(false, Button.PS); }
  public boolean leftTriggerWasPressed() { return take(true, Button.LEFT_TRIGGER); }
  public boolean leftTriggerWasReleased() { return take(false, Button.LEFT_TRIGGER); }
  public boolean rightTriggerWasPressed() { return take(true, Button.RIGHT_TRIGGER); }
  public boolean rightTriggerWasReleased() { return take(false, Button.RIGHT_TRIGGER); }
}
