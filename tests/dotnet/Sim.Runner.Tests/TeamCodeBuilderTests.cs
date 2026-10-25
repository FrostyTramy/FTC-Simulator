namespace RunnerTests;

/// <summary>Spec 07 build adapter: TeamCode → jar, compiler errors as clickable diagnostics.</summary>
public class TeamCodeBuilderTests
{
    [Fact]
    public void Javac_messages_become_diagnostics_with_columns()
    {
        var output = string.Join("\n",
            @"D:\work\TeamCode\src\main\java\org\firstinspires\ftc\teamcode\MyTeleOp.java:12: error: ';' expected",
            "        int x = 3",
            "                 ^",
            @"D:\work\TeamCode\src\main\java\org\firstinspires\ftc\teamcode\MyTeleOp.java:20: warning: [deprecation] old() in Foo has been deprecated",
            "1 error",
            "FAILURE: Build failed with an exception.");
        var d = TeamCodeBuilder.ParseDiagnostics(output);
        Assert.Equal(2, d.Count);
        Assert.Equal((12, 18, "error", "';' expected"), (d[0].Line, d[0].Column, d[0].Severity, d[0].Message));
        Assert.EndsWith("MyTeleOp.java", d[0].File);
        Assert.Equal(("warning", (int?)null), (d[1].Severity, d[1].Column));
    }

    [Fact]
    public void A_teamcode_folder_with_an_error_reports_it_and_a_fixed_one_builds()
    {
        var root = Path.Combine(Path.GetTempPath(), "ftcsim-build-test-" + Guid.NewGuid().ToString("N")[..8]);
        var package = Path.Combine(root, "src", "org", "firstinspires", "ftc", "teamcode");
        Directory.CreateDirectory(package);
        var file = Path.Combine(package, "Hello.java");
        const string good = """
            package org.firstinspires.ftc.teamcode;

            import com.qualcomm.robotcore.eventloop.opmode.LinearOpMode;
            import com.qualcomm.robotcore.eventloop.opmode.TeleOp;

            @TeleOp(name = "Hello")
            public class Hello extends LinearOpMode {
                @Override
                public void runOpMode() {
                    telemetry.addData("Status", "hello");
                    telemetry.update();
                    waitForStart();
                }
            }
            """;
        try
        {
            File.WriteAllText(file, good.Replace("telemetry.update();", "telemetry.update()"));
            var broken = TeamCodeBuilder.Build(Path.Combine(root, "src"), Path.Combine(root, "build"));
            Assert.False(broken.Success);
            var error = Assert.Single(broken.Errors);
            Assert.Equal(Path.GetFullPath(file), Path.GetFullPath(error.File));
            Assert.Equal(11, error.Line);

            File.WriteAllText(file, good);
            var fixedBuild = TeamCodeBuilder.Build(Path.Combine(root, "src"), Path.Combine(root, "build"));
            Assert.True(fixedBuild.Success, fixedBuild.Output);
            Assert.True(File.Exists(fixedBuild.JarPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
