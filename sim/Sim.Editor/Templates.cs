namespace Sim.Editor;

/// <summary>Kinds of new files the editor offers (spec 07 "Templates").</summary>
public enum TemplateKind
{
    TeleOp,
    Autonomous,
    LinearOpMode,
    Subsystem,
}

/// <summary>
/// New-file templates with the right package and annotations, written in the style of the FTC SDK samples (4-space
/// indentation, as FTC repositories use). The class name must be a Java identifier.
/// </summary>
public static class Templates
{
    public const string Package = "org.firstinspires.ftc.teamcode";

    /// <summary>Path of a new class in TeamCode/src/main/java (the teamcode package folder).</summary>
    public static string PathFor(string teamCodeJavaDir, string className) =>
        Path.Combine(teamCodeJavaDir, "org", "firstinspires", "ftc", "teamcode", className + ".java");

    public static string Create(TemplateKind kind, string className, string? displayName = null)
    {
        if (!IsJavaIdentifier(className))
        {
            throw new ArgumentException($"'{className}' is not a valid Java class name");
        }
        var name = (displayName ?? className).Replace("\"", "'", StringComparison.Ordinal);
        return kind switch
        {
            TemplateKind.TeleOp => $$"""
                package {{Package}};

                import com.qualcomm.robotcore.eventloop.opmode.OpMode;
                import com.qualcomm.robotcore.eventloop.opmode.TeleOp;

                @TeleOp(name = "{{name}}")
                public class {{className}} extends OpMode {

                    @Override
                    public void init() {
                        telemetry.addData("Status", "Initialized");
                    }

                    @Override
                    public void loop() {
                        telemetry.addData("Runtime", "%.1f s", getRuntime());
                    }
                }

                """,
            TemplateKind.Autonomous => $$"""
                package {{Package}};

                import com.qualcomm.robotcore.eventloop.opmode.Autonomous;
                import com.qualcomm.robotcore.eventloop.opmode.LinearOpMode;

                @Autonomous(name = "{{name}}")
                public class {{className}} extends LinearOpMode {

                    @Override
                    public void runOpMode() {
                        telemetry.addData("Status", "Initialized");
                        telemetry.update();
                        waitForStart();

                        while (opModeIsActive()) {
                            telemetry.addData("Runtime", "%.1f s", getRuntime());
                            telemetry.update();
                        }
                    }
                }

                """,
            TemplateKind.LinearOpMode => $$"""
                package {{Package}};

                import com.qualcomm.robotcore.eventloop.opmode.LinearOpMode;
                import com.qualcomm.robotcore.eventloop.opmode.TeleOp;

                @TeleOp(name = "{{name}}")
                public class {{className}} extends LinearOpMode {

                    @Override
                    public void runOpMode() {
                        telemetry.addData("Status", "Initialized");
                        telemetry.update();
                        waitForStart();

                        while (opModeIsActive()) {
                            telemetry.addData("Left stick y", gamepad1.left_stick_y);
                            telemetry.update();
                        }
                    }
                }

                """,
            TemplateKind.Subsystem => $$"""
                package {{Package}};

                import com.qualcomm.robotcore.hardware.HardwareMap;

                public class {{className}} {

                    public {{className}}(HardwareMap hardwareMap) {
                    }

                    public void update() {
                    }
                }

                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public static bool IsJavaIdentifier(string s) =>
        s.Length > 0 && (char.IsLetter(s[0]) || s[0] is '_' or '$') && s.All(c => char.IsLetterOrDigit(c) || c is '_' or '$')
        && !JavaKeywords.Contains(s);

    private static readonly HashSet<string> JavaKeywords =
    [
        "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue", "default",
        "do", "double", "else", "enum", "extends", "final", "finally", "float", "for", "goto", "if", "implements", "import",
        "instanceof", "int", "interface", "long", "native", "new", "package", "private", "protected", "public", "return",
        "short", "static", "strictfp", "super", "switch", "synchronized", "this", "throw", "throws", "transient", "try",
        "void", "volatile", "while", "true", "false", "null", "var", "record", "yield",
    ];
}
