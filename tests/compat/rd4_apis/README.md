# RD4 API compatibility project

Team code written for this repository (not the team's): it uses, the same way the RoboDojo #23205 RD4 code does
according to its build log (USER 2026-10-26), the APIs the simulator's SDK lacked: Panels (`@Configurable`,
`@IgnoreConfigurable`, `PanelsTelemetry`), FTC Dashboard `@Config`, `Limelight3A`, `AnalogInput`, `TypeConversion`,
`LynxI2cDeviceSynch` bus speed, a team-owned register-level Pinpoint driver, `RobotLog.ee` with an exception,
`android.text.TextUtils`, `android.os.Environment`, `AppUtil` and `OpModeManagerImpl` listeners (D-039).
Built and run by `tests/dotnet/Sim.Runner.Tests/Rd4ApiTests.cs`.
