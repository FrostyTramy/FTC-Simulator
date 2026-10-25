# Development setup (Windows 10/11 x64)

Run `./scripts/check_tools.ps1` at any time; it prints one line per tool and exits 1 if
something required is missing.

| Tool | Version | Status on the dev laptop (2026-10-24) |
|---|---|---|
| Git | any recent | 2.52.0 ✅ |
| Godot .NET | 4.7.2 stable (mono) | in `Godot_v4.7.2-stable_mono_win64/` ⚠️ see "Godot does not start" |
| .NET SDK | 8 or newer (10 LTS installed) | 10.0.402 ✅ |
| JDK | 17 (javac) | Oracle 17.0.12 in `C:\Program Files\Java\jdk-17` ✅ |
| Python | 3.11+ x64 (3.13 used) | 3.13.14 ✅, project `.venv` ✅ |
| Node | 18+ (only for the Godot MCP server) | 24.15.0 ✅ |

## 1. Python environment (needed from Phase 1)
From the repo root in PowerShell:
```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install --upgrade pip
.\.venv\Scripts\python.exe -m pip install -r tools\cad_pipeline\requirements.txt
```
The scripts use `.venv` automatically once it exists.

## 2. Godot .NET
- Download "Windows .NET x86_64" from https://godotengine.org/download/windows/ and keep the
  `GodotSharp` folder next to the exe.
- Scripts look for `Godot_v4*mono*_console.exe` in the repo root, `D:\Tools\Godot` or
  `%LOCALAPPDATA%\Programs\Godot`. Anywhere else: set `GODOT_BIN` to the console exe.

### Godot does not start (open)
On 2026-10-24 `--version` answered once, then every launch of both the .NET and the standard
build stayed stuck at process start (1 thread, 2 MB, never loads its DLLs), also outside the
assistant's sandbox. Windows Defender real-time protection is off and Smart App Control is off.
Symantec Endpoint Protection is installed and active and is the likely cause (Q-001).
Please try: double-click `Godot_v4.7.2-stable_mono_win64.exe` and see whether it opens or a
dialog appears; if it hangs, reboot and try again.

## 3. JDK 17
Already installed. `java` on PATH is JDK 26, so scripts never use PATH java: they use
`JAVA17_HOME`, else `JAVA_HOME` if it is 17, else `C:\Program Files\Java\jdk-17*` (also Eclipse
Adoptium and Microsoft folders). For the release build we bundle Eclipse Temurin 17, because
Oracle JDK may not be redistributed.

## 4. Generated tools (Gradle, JDT LS, AprilTag)
```powershell
./scripts/setup_tools.ps1
```
Downloads Gradle 8.14.3 and Eclipse JDT LS 1.62.0 (SHA-256 checked) and copies the AprilTag 3 native library with its
runtime DLLs from the pinned `pupil-apriltags` wheel in `.venv`, all into `generated/tools`. JDT LS runs on Java 21+
(the `java` on PATH, here JDK 26); team code still compiles with JDK 17.

## 5. Godot MCP server (for the AI assistant only)
Run once in Claude Code, inside this folder, then restart Claude Code:
```
! claude mcp add godot -s local -e GODOT_PATH=D:/Code/FTC-Simulator/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe -- cmd /c npx -y @coding-solo/godot-mcp
```

## Tests
```powershell
./scripts/test_all.ps1    # inventory check + every test suite that exists so far
./scripts/test_unit.ps1   # unit tests only
```
`test_scenarios.ps1`, `test_compat.ps1` and `bench.ps1` are added in the phases that create
those tests (4, 6 and 5).
