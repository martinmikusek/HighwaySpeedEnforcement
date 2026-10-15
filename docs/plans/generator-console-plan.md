# Generator: interactive console, implementation plan

**Status:** Ready. Design: `docs/design/generator.md` section 11 (read all of `generator.md` first). Rules, commands and conventions: `CLAUDE.md`, and
`docs/plans/generator-implementation-plan.md` sections 3 and 4 (authority, deviation log, style, tests, definition of done, commit format) apply unchanged;
the deviation log for this work is a new section "Interactive console" in `docs/design/generator-implementation-notes.md`.
Work in your own worktree on branch `feature/generator-console` created from `origin/main`; commit per step; push after the last step; do not merge,
do not touch `src/Processor/` or `src/Client/`.

**Hard requirement:** with redirected input or output the generator behaves exactly as before. All existing tests, including the whole-generator E2E tests
(they run the real process with redirected output and parse JSON logs), stay green and unchanged.

## Steps

1. **`RuntimeControl` (Generator.Domain).** Thread-safe holder of: traffic paused, vehicles per tick, heartbeats enabled, disabled cameras (set of 1..10). Operations
   toggle pause, set or toggle heartbeats, double and halve vehicles (clamped 1..2000), toggle a camera; `Snapshot()` returns an immutable record; initial values come
   from the settings. Tests: each operation, clamping, camera range, concurrent calls (several threads toggling, final state consistent), the snapshot is detached.
2. **Simulation per-tick options.** `Simulation` must accept the options of each tick (an overload of `Tick` or a method called by the runner before `Tick`) so the
   runner passes the current `SimulationOptions` derived from the control snapshot. Paused traffic means: no spawn this tick, vehicles continue, idle cameras
   follow the heartbeat rule. Speeds are still drawn for every advance. A run that never changes the options must produce identical output to before (add a test
   comparing a run through the new path with the old behavior for the same seed). Tests: pause then resume (vehicles drain, nothing spawns while paused, spawning
   resumes), heartbeat toggle, vehicles-per-tick change, camera toggle mid-run (vehicles still reach camera 10, no measurement for the disabled camera, other cameras'
   speeds unchanged), unique plates across pause and resume.
3. **Runner integration.** `SimulationRunner` reads `RuntimeControl.Snapshot()` at the start of every tick, applies it, and logs each change (Information, "operator: ...", once per
   change, with the old and new value). Tests with `FakeTimeProvider` and fakes (extend the existing runner tests): a change between ticks takes effect at the next tick and not
   before; changes are logged; no change gives the same published messages as before.
4. **Key mapping and status line (Host, pure).** A mapping from `ConsoleKeyInfo` to commands (including `+` and `-` on the main keyboard and on the numeric keypad, the digit keys
   and `0` for camera 10, `P`, `H`, `Q`, case-insensitive) and a `StatusLineFormatter` producing the status line and the key-help line for a given width (truncate with an ellipsis,
   never exceed the width, no ANSI escapes inside the text; state shown: tick, traffic running or paused, vehicles per tick, heartbeats, cameras off, broker state (connected or reconnecting),
   messages sent, last tick duration, seed or "random"). Tests: every key, unknown keys ignored, widths 40, 80, 120, 200, camera list formatting (`none`, `7`, `3,7`), numbers with
   thousands separators in the invariant culture.
5. **`InteractiveConsole` (Host, the only class that uses `System.Console`).** Activation rule of the design; the setting `Generator:Console:Interactive` (Auto, true, false) with validation in
   the settings class and its tests. Enable ANSI processing on Windows (`SetConsoleMode` with ENABLE_VIRTUAL_TERMINAL_PROCESSING through P/Invoke; if it fails fall back to compact log lines and keys
   without the bar), set the scroll region so the bottom three rows are fixed, draw and redraw the bar (save and restore the cursor; one lock shared with the logger so lines never interleave), redraw
   at the end of each tick, on a key press and on a resize, and restore the terminal on exit (reset the scroll region, move the cursor below the bar, show the cursor). A key reader loop (poll
   `Console.KeyAvailable`, never block shutdown) writes to `RuntimeControl`; `Q` stops the host like Ctrl+C. Logging: when active, replace the JSON console provider by a compact one-line formatter
   (`HH:mm:ss level message`) that writes through the same lock; otherwise nothing changes. The runner exposes the numbers the bar needs (tick, messages sent, last tick duration, broker state)
   through a small read-only status object, added without changing the existing log output. The class takes its terminal through a small interface (`IConsoleTerminal`) so tests need no real
   terminal. Tests: the activation rule (redirected input or output means inactive; the three setting values), the logging switch (inactive keeps the JSON provider), the restore sequence emitted
   on dispose to a fake terminal, the bar is not drawn when the fake terminal has no ANSI support.
6. **Manual run and documentation.** Run the generator in a real terminal against `docker compose up -d --build`. If your session cannot show an interactive terminal, test through a pseudo-terminal
   or by feeding the key sequence and capturing the produced escape sequences into a file, read them, and say honestly in the notes what you could and could not observe. Update `README.md`
   (a short "Interactive console" subsection with the keys, the status bar and the `--Generator:Console:Interactive=false` switch) and the "Generator" section of `CLAUDE.md` (one line).
   Final checks: `dotnet build Yunex.HighwaySpeedEnforcement.slnx` (0 warnings), `dotnet format Yunex.HighwaySpeedEnforcement.slnx --verify-no-changes`,
   `dotnet test --solution Yunex.HighwaySpeedEnforcement.slnx` all green, and the generator E2E tests run twice.
   Final report: steps done, test counts, deviations, what could not be observed, branch and last commit, commands.
