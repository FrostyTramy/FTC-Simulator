package org.firstinspires.ftc.teamcode;

import com.qualcomm.robotcore.eventloop.opmode.Autonomous;
import com.qualcomm.robotcore.eventloop.opmode.LinearOpMode;
import com.qualcomm.robotcore.hardware.CRServo;
import com.qualcomm.robotcore.hardware.DcMotor;
import com.qualcomm.robotcore.hardware.DcMotorEx;
import com.qualcomm.robotcore.hardware.DcMotorSimple;
import com.qualcomm.robotcore.hardware.PIDFCoefficients;
import com.qualcomm.robotcore.util.ElapsedTime;

/*
 * Simple example Autonomous for the goBILDA StarterBot with mecanum wheels (BIOBUZZ).
 * Written for the BIOBUZZ simulator project (decision D-005); uses only the standard FTC SDK API, so
 * it builds unchanged in Android Studio.
 *
 * Plan (30 s AUTO, manual §10.4): spin up the launcher exactly like the goBILDA TeleOp (1250 ticks/s,
 * PIDF 40/0/0/12.5), feed the 4 pre-loaded POLLEN with the windmill and intake, then use the drive
 * encoders to leave the perimeter wall (LEAVE, §10.5.4) and strafe towards the LOADING ZONE (PARK).
 * Place the robot touching its alliance wall, aimed at the HIVE, with the LOADING ZONE to its right
 * (red alliance; mirror STRAFE_INCHES for blue). Distances are starting points: tune them.
 */
@Autonomous(name = "Mec BioBuzz StarterBot Auto", group = "StarterBot", preselectTeleOp = "Mec BioBuzz StarterBot Teleop")
public class BioBuzzStarterbotAutoMecanum extends LinearOpMode {
    // Same launcher settings as BioBuzzStarterbotTeleopMecanum.
    static final double LAUNCHER_TARGET_VELOCITY = 1250;
    static final double LAUNCHER_MIN_VELOCITY = 1200;
    static final double FEED_SECONDS = 4.0;
    static final double SPIN_UP_TIMEOUT_SECONDS = 3.0;

    // goBILDA 5203 19.2:1 (537.7 counts/rev), 1:1 miter gears, 104 mm mecanum wheels.
    static final double COUNTS_PER_MOTOR_REV = 537.7;
    static final double WHEEL_DIAMETER_INCHES = 104.0 / 25.4;
    static final double COUNTS_PER_INCH = COUNTS_PER_MOTOR_REV / (WHEEL_DIAMETER_INCHES * Math.PI);

    static final double LEAVE_INCHES = 6.0;
    static final double STRAFE_INCHES = 24.0;
    static final double DRIVE_POWER = 0.5;

    private DcMotor leftFrontDrive;
    private DcMotor rightFrontDrive;
    private DcMotor leftBackDrive;
    private DcMotor rightBackDrive;
    private DcMotor intake;
    private DcMotorEx launcher;
    private CRServo windmillServo;
    private CRServo leftIntakeServo;
    private CRServo rightIntakeServo;
    private final ElapsedTime timer = new ElapsedTime();

    @Override
    public void runOpMode() {
        leftFrontDrive = hardwareMap.get(DcMotor.class, "left_front_drive");
        rightFrontDrive = hardwareMap.get(DcMotor.class, "right_front_drive");
        leftBackDrive = hardwareMap.get(DcMotor.class, "left_back_drive");
        rightBackDrive = hardwareMap.get(DcMotor.class, "right_back_drive");
        intake = hardwareMap.get(DcMotor.class, "intake");
        launcher = hardwareMap.get(DcMotorEx.class, "launcher");
        windmillServo = hardwareMap.get(CRServo.class, "windmillServo");
        leftIntakeServo = hardwareMap.get(CRServo.class, "left_intake_servo");
        rightIntakeServo = hardwareMap.get(CRServo.class, "right_intake_servo");

        // Directions and modes as in the TeleOp.
        leftFrontDrive.setDirection(DcMotor.Direction.REVERSE);
        rightFrontDrive.setDirection(DcMotor.Direction.FORWARD);
        leftBackDrive.setDirection(DcMotor.Direction.REVERSE);
        rightBackDrive.setDirection(DcMotor.Direction.FORWARD);
        rightIntakeServo.setDirection(DcMotorSimple.Direction.REVERSE);
        windmillServo.setDirection(DcMotorSimple.Direction.REVERSE);
        for (DcMotor motor : new DcMotor[] {leftFrontDrive, rightFrontDrive, leftBackDrive, rightBackDrive, intake}) {
            motor.setZeroPowerBehavior(DcMotor.ZeroPowerBehavior.BRAKE);
        }
        for (DcMotor motor : new DcMotor[] {leftFrontDrive, rightFrontDrive, leftBackDrive, rightBackDrive}) {
            motor.setMode(DcMotor.RunMode.STOP_AND_RESET_ENCODER);
        }
        launcher.setMode(DcMotor.RunMode.RUN_USING_ENCODER);
        launcher.setPIDFCoefficients(DcMotor.RunMode.RUN_USING_ENCODER, new PIDFCoefficients(40, 0, 0, 12.5));

        telemetry.addData("Status", "Initialized");
        telemetry.update();
        waitForStart();

        launchPreloads();
        driveInches(LEAVE_INCHES, 0);   // LEAVE: off the perimeter wall
        driveInches(0, STRAFE_INCHES);  // towards the LOADING ZONE (PARK)

        telemetry.addData("Status", "Done");
        telemetry.update();
    }

    /** Spins up the launcher and feeds the pre-loaded POLLEN, like holding the right bumper in the TeleOp. */
    private void launchPreloads() {
        launcher.setVelocity(LAUNCHER_TARGET_VELOCITY);
        timer.reset();
        while (opModeIsActive() && launcher.getVelocity() < LAUNCHER_MIN_VELOCITY && timer.seconds() < SPIN_UP_TIMEOUT_SECONDS) {
            telemetry.addData("Launcher", "%.0f ticks/s", launcher.getVelocity());
            telemetry.update();
        }
        timer.reset();
        while (opModeIsActive() && timer.seconds() < FEED_SECONDS) {
            boolean atSpeed = launcher.getVelocity() > LAUNCHER_MIN_VELOCITY;
            windmillServo.setPower(atSpeed ? 1 : 0);
            intake.setPower(atSpeed ? 0.5 : 0);
            leftIntakeServo.setPower(atSpeed ? 1 : 0);
            rightIntakeServo.setPower(atSpeed ? 1 : 0);
            telemetry.addData("Launcher", "%.0f ticks/s", launcher.getVelocity());
            telemetry.update();
        }
        windmillServo.setPower(0);
        intake.setPower(0);
        leftIntakeServo.setPower(0);
        rightIntakeServo.setPower(0);
        launcher.setVelocity(0);
    }

    /** Moves with RUN_TO_POSITION: forward and right in inches (mecanum mixing as in the TeleOp). */
    private void driveInches(double forwardInches, double rightInches) {
        int forward = (int) Math.round(forwardInches * COUNTS_PER_INCH);
        int right = (int) Math.round(rightInches * COUNTS_PER_INCH);
        DcMotor[] motors = {leftFrontDrive, rightFrontDrive, leftBackDrive, rightBackDrive};
        int[] targets = {forward + right, forward - right, forward - right, forward + right};
        for (int i = 0; i < motors.length; i++) {
            motors[i].setTargetPosition(motors[i].getCurrentPosition() + targets[i]);
        }
        for (DcMotor motor : motors) {
            motor.setMode(DcMotor.RunMode.RUN_TO_POSITION);
            motor.setPower(DRIVE_POWER);
        }
        timer.reset();
        while (opModeIsActive() && timer.seconds() < 5
                && (leftFrontDrive.isBusy() || rightFrontDrive.isBusy() || leftBackDrive.isBusy() || rightBackDrive.isBusy())) {
            telemetry.addData("Drive", "LF %d  RF %d", leftFrontDrive.getCurrentPosition(), rightFrontDrive.getCurrentPosition());
            telemetry.update();
        }
        for (DcMotor motor : motors) {
            motor.setPower(0);
            motor.setMode(DcMotor.RunMode.RUN_USING_ENCODER);
        }
    }
}
