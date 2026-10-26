package org.firstinspires.ftc.teamcode;

import com.qualcomm.robotcore.eventloop.opmode.OpMode;
import com.qualcomm.robotcore.eventloop.opmode.TeleOp;
import com.qualcomm.robotcore.hardware.DcMotor;
import com.qualcomm.robotcore.hardware.DcMotorEx;
import com.qualcomm.robotcore.hardware.DcMotorSimple;
import com.qualcomm.robotcore.hardware.PIDFCoefficients;
import com.qualcomm.robotcore.hardware.Servo;
import com.qualcomm.robotcore.util.Range;

/*
 * TeleOp for the FTC RoboDojo #23205 robot (BIOBUZZ).
 *
 * gamepad1:
 *   left stick         drive and strafe        right stick X   turn
 *   right trigger      intake + transfer in    left trigger    spit out (reverse)
 *   right bumper hold  spin the shooter up; once it is at speed the gate opens and the transfer feeds
 *   B                  toggle near / far shooter speed
 *   dpad left / right  turn the turret         Y               turret back to the front
 *   dpad up / down     hood up / down          X               flower bar up / down (down = resting on the floor)
 *
 * The turret turns 360 degrees between hard stops (it cannot spin through), so its target stays inside soft limits a
 * little short of the stops. Its encoder is a REV Through Bore Encoder (8192 counts per rev) on a 24T pinion that
 * meshes the 24T motor pinion: it turns as fast as the motor shaft but the OTHER way. Plugged into the turret motor's
 * port it counts down while the motor turns forward, so RUN_TO_POSITION would run away; this code closes the loop
 * itself with the encoder sign flipped.
 */
@TeleOp(name = "RoboDojo TeleOp", group = "RoboDojo")
public class RoboDojoTeleOp extends OpMode {

    // Turret: 24T motor pinion on the 112T ring; Through Bore Encoder (8192 counts/rev) turning with the motor shaft.
    static final double TURRET_RING_TEETH = 112;
    static final double TURRET_TICKS_PER_DEGREE = 8192.0 * (TURRET_RING_TEETH / 24.0) / 360.0;
    static final double TURRET_ENCODER_SIGN = -1;      // the encoder pinion counter-rotates
    static final double TURRET_SOFT_LIMIT_DEG = 170;   // hard stops at +-180 (the turret must unwind, it cannot spin through)
    static final double TURRET_SPEED_DEG_PER_S = 180;
    static final double TURRET_KP = 0.02;              // power per degree of error
    static final double TURRET_MAX_POWER = 0.6;

    // Shooter: 6000 RPM motor, 28 ticks per revolution (2800 ticks/s free speed).
    static final double SHOOTER_NEAR = 1500;
    static final double SHOOTER_FAR = 2100;
    static final double SHOOTER_READY_FRACTION = 0.95;

    static final double GATE_CLOSED = 0.5;
    static final double GATE_OPEN = 0.8;
    static final double HOOD_START = 0.0;    // starting position: hood fully back (steepest shot)
    static final double HOOD_MAX = 1.0;      // hood raised over the wheel: flattest shot
    static final double HOOD_STEP_PER_S = 0.5;
    static final double FLOWER_BAR_UP = 0.25;
    static final double FLOWER_BAR_DOWN = 1.0;   // past the floor: the bar rests on the tiles, as low as it goes

    private DcMotor leftFront, rightFront, leftBack, rightBack;
    private DcMotor intake, transfer;
    private DcMotorEx turret, shooter;
    private Servo gate, hood, flowerBar;

    private double turretTargetDeg = 0;
    private double hoodPosition = HOOD_START;
    private boolean far = false;
    private boolean flowerBarDown = false;
    private boolean lastB, lastX;
    private double lastTime;

    @Override
    public void init() {
        leftFront = hardwareMap.get(DcMotor.class, "left_front_drive");
        rightFront = hardwareMap.get(DcMotor.class, "right_front_drive");
        leftBack = hardwareMap.get(DcMotor.class, "left_back_drive");
        rightBack = hardwareMap.get(DcMotor.class, "right_back_drive");
        intake = hardwareMap.get(DcMotor.class, "intake");
        transfer = hardwareMap.get(DcMotor.class, "transfer");
        turret = hardwareMap.get(DcMotorEx.class, "turret");
        shooter = hardwareMap.get(DcMotorEx.class, "shooter");
        gate = hardwareMap.get(Servo.class, "gate");
        hood = hardwareMap.get(Servo.class, "hood");
        flowerBar = hardwareMap.get(Servo.class, "flower_bar");

        leftFront.setDirection(DcMotorSimple.Direction.REVERSE);
        leftBack.setDirection(DcMotorSimple.Direction.REVERSE);
        rightFront.setDirection(DcMotorSimple.Direction.FORWARD);
        rightBack.setDirection(DcMotorSimple.Direction.FORWARD);
        for (DcMotor m : new DcMotor[] {leftFront, rightFront, leftBack, rightBack, intake, transfer}) {
            m.setZeroPowerBehavior(DcMotor.ZeroPowerBehavior.BRAKE);
        }

        // The turret starts where it was switched on (pointing forward): zero the encoder there.
        turret.setMode(DcMotor.RunMode.STOP_AND_RESET_ENCODER);
        turret.setMode(DcMotor.RunMode.RUN_WITHOUT_ENCODER);
        turret.setZeroPowerBehavior(DcMotor.ZeroPowerBehavior.BRAKE);

        shooter.setMode(DcMotor.RunMode.RUN_USING_ENCODER);
        shooter.setPIDFCoefficients(DcMotor.RunMode.RUN_USING_ENCODER, new PIDFCoefficients(30, 0, 0, 13));

        gate.setPosition(GATE_CLOSED);
        hood.setPosition(hoodPosition);
        flowerBar.setPosition(FLOWER_BAR_UP);
        telemetry.addData("Status", "Initialized");
    }

    @Override
    public void start() {
        lastTime = getRuntime();
    }

    @Override
    public void loop() {
        double now = getRuntime();
        double dt = Math.min(now - lastTime, 0.1);
        lastTime = now;

        drive(-gamepad1.left_stick_y, gamepad1.left_stick_x, gamepad1.right_stick_x);

        // Turret: dpad moves the target (left = counter-clockwise), Y returns to the front.
        if (gamepad1.dpad_left) {
            turretTargetDeg += TURRET_SPEED_DEG_PER_S * dt;
        } else if (gamepad1.dpad_right) {
            turretTargetDeg -= TURRET_SPEED_DEG_PER_S * dt;
        }
        if (gamepad1.y) {
            turretTargetDeg = 0;
        }
        turretTargetDeg = Range.clip(turretTargetDeg, -TURRET_SOFT_LIMIT_DEG, TURRET_SOFT_LIMIT_DEG);
        double turretDeg = turretAngle();
        turret.setPower(Range.clip(TURRET_KP * (turretTargetDeg - turretDeg), -TURRET_MAX_POWER, TURRET_MAX_POWER));

        // Hood
        if (gamepad1.dpad_up) {
            hoodPosition += HOOD_STEP_PER_S * dt;
        } else if (gamepad1.dpad_down) {
            hoodPosition -= HOOD_STEP_PER_S * dt;
        }
        hoodPosition = Range.clip(hoodPosition, HOOD_START, HOOD_MAX);
        hood.setPosition(hoodPosition);

        // Toggles
        if (gamepad1.b && !lastB) {
            far = !far;
        }
        if (gamepad1.x && !lastX) {
            flowerBarDown = !flowerBarDown;
        }
        lastB = gamepad1.b;
        lastX = gamepad1.x;
        flowerBar.setPosition(flowerBarDown ? FLOWER_BAR_DOWN : FLOWER_BAR_UP);

        // Intake, transfer, shooter and gate
        double intakePower = gamepad1.right_trigger - gamepad1.left_trigger;
        double transferPower = intakePower;
        double target = far ? SHOOTER_FAR : SHOOTER_NEAR;
        boolean ready = false;
        if (gamepad1.right_bumper) {
            shooter.setVelocity(target);
            ready = shooter.getVelocity() >= SHOOTER_READY_FRACTION * target;
            if (ready) {
                transferPower = 1;
            }
        } else {
            shooter.setVelocity(0);
        }
        gate.setPosition(ready ? GATE_OPEN : GATE_CLOSED);
        intake.setPower(intakePower);
        transfer.setPower(transferPower);

        telemetry.addData("Shooter", "%.0f / %.0f ticks/s %s", shooter.getVelocity(), target, far ? "FAR" : "NEAR");
        telemetry.addData("Turret", "%.1f deg (target %.1f)", turretDeg, turretTargetDeg);
        telemetry.addData("Hood", "%.2f", hoodPosition);
        telemetry.addData("Flower bar", flowerBarDown ? "DOWN" : "UP");
    }

    /** Turret angle from the front, degrees, counter-clockwise positive. */
    double turretAngle() {
        return TURRET_ENCODER_SIGN * turret.getCurrentPosition() / TURRET_TICKS_PER_DEGREE;
    }

    @Override
    public void stop() {
        shooter.setVelocity(0);
    }

    /** Robot-centric mecanum drive: forward, strafe right, turn right. */
    void drive(double forward, double strafe, double rotate) {
        double lf = forward + strafe + rotate;
        double rf = forward - strafe - rotate;
        double lb = forward - strafe + rotate;
        double rb = forward + strafe - rotate;
        double max = Math.max(1.0, Math.max(Math.max(Math.abs(lf), Math.abs(rf)), Math.max(Math.abs(lb), Math.abs(rb))));
        leftFront.setPower(lf / max);
        rightFront.setPower(rf / max);
        leftBack.setPower(lb / max);
        rightBack.setPower(rb / max);
    }
}
