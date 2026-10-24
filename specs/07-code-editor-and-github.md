# 07 — Built-in Code Editor, Build/Run, GitHub Sync

## Goal
Write code, press one button, watch the robot run it, fix, push to GitHub:
all inside the app, fast enough that it feels better than deploying to the
real robot.

## Layout
- Left: file tree (TeamCode focus, rest of repo collapsible), git status
  colors per file.
- Center: tabbed editor.
- Right or bottom (dockable/split): live sim view, Driver Station panel,
  telemetry, problems, build output, run log, git panel.
- Layouts: "Code", "Drive", "Split" presets; remembers layout per project.

## Editor features
Base: Godot `CodeEdit` (or a better embedded option if it hits limits;
propose with evidence before switching). Must have:
- Java syntax highlighting, line numbers, current-line highlight, minimap
  (optional), code folding, bracket matching/auto-close, auto-indent,
  multi-cursor (nice to have), find/replace in file and in project (regex),
  go to line, undo/redo history per file, unsaved markers, autosave option.
- Keybindings similar to VS Code / Android Studio (choose a default, make
  configurable): Ctrl+S save, Ctrl+Space complete, F12 go to definition,
  Shift+F6 / F2 rename, Ctrl+/ comment, Ctrl+Shift+F search, F5 run.
- Themes: dark (default) and light; font size; tab vs spaces (FTC repos use
  4 spaces).
- Templates: "New TeleOp", "New Autonomous", "New LinearOpMode",
  "New subsystem class", with correct package and annotations.
- Formatter (google-java-format or Eclipse formatter via JDT LS).
- File operations: new, rename, move, delete (with confirm), refactor-safe
  rename through LSP.

## Language server (smart features)
- Eclipse JDT Language Server as a child process, LSP over stdio.
- Project model: a generated Gradle (or Eclipse) project that puts
  TeamCode sources + shim jar + resolved pure-Java dependencies on the
  classpath, so completions and errors match what the sim build sees.
- Features: completion (with FTC SDK javadoc), signature help, hover,
  diagnostics (red/yellow squiggles + problems panel), go to definition (into
  shim sources), find references, rename, quick fixes (imports), document
  symbols / outline, organize imports.
- Start it lazily; show "indexing..." status; restart on crash.

## Build adapter (TeamCode → sim)
The real repo is an Android Gradle project; the sim needs plain JVM bytecode.
- `java/build-adapter`: a generated Gradle project that:
  - uses the user's `TeamCode/src/main/java` as source set (read-only),
  - replaces FTC SDK dependencies with `ftc-sdk-shim.jar`,
  - reads the user's `build.dependencies.gradle`/`build.gradle` to find
    third-party libraries (Road Runner, Pedro, FTCLib, Dashboard...) and
    resolves them from Maven; for Android AAR libraries, extract
    `classes.jar` and stub Android APIs they touch; report unsupported ones,
  - compiles with javac (Java version matching the FTC project's settings),
  - outputs a jar the runner loads.
- Gradle daemon kept warm; offline cache after the first build; incremental
  compilation. Target: ≤ 10 s from save to OpModes listed.
- The user's repo is never modified by the build (no generated files in it).
- Assets (`TeamCode/src/main/res`, e.g. TFLite/config files) copied if used.

## Run on robot
- F5 / "Run on robot": save all → build → (on success) restart JVM runner with
  the new jar → refresh OpMode list in DS → optionally auto-INIT the last
  used OpMode.
- "Restart OpMode" keeps the robot pose; "Reset field" resets everything.
- Problems panel: clickable errors (file:line), compile + runtime
  exceptions, stack traces with clickable frames into user code.
- Optional hot reload of method bodies (JVM HotSwap via debug agent) for
  quick tuning; if unreliable, skip and document.
- Debugger (later, nice to have): breakpoints via JDWP, pause sim time while
  stopped at a breakpoint, inspect variables.

## Git and GitHub
Implementation: git CLI (bundled portable Git if not installed) or
LibGit2Sharp — compare in DECISIONS.md, ask me before choosing.
- **Sign in**: GitHub OAuth device flow (shows code + opens github.com/login/
  device). Requires a GitHub OAuth App client ID; ask me to create it and
  give you the client ID (never put a client secret in the app).
  Token stored only in Windows Credential Manager; sign out removes it.
  Also support existing system git credentials.
- **Projects**: clone from URL or from a list of my repos/org repos; open an
  existing local folder; recent projects list.
- **Daily flow**: status (changed/new/deleted files), diff viewer (side by
  side or inline, syntax highlighted), stage/unstage files and hunks, commit
  with message (+ author from git config/GitHub profile), push, pull
  (fetch + merge or rebase, configurable), sync button.
- **Branches**: list, create, switch (with uncommitted changes handling:
  stash prompt), delete local; show ahead/behind counts.
- **Conflicts**: list conflicted files, 3-way view (ours/theirs/result) with
  accept buttons; mark resolved; abort merge.
- **History**: log view with graph (simple), view a commit's diff, revert a
  commit (with confirm).
- **Safety**: never force-push or discard changes without explicit
  confirmation; warn when pushing to `main/master` of a shared repo; warn on
  large files and secrets (e.g. tokens/keys in code) before commit.
- `.gitignore` respected; don't add sim-generated files to the user repo.

## Sandboxing user code
- Runner JVM started with restricted permissions: working dir inside the
  app's temp folder, no network by default (option to allow for Dashboard on
  localhost), file access limited to the project and a sim log folder,
  memory limit, CPU watchdog.
- Clear message when code tries to do something blocked.
