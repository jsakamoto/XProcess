# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

XProcess (NuGet package `XProcess`, assembly `Toolbelt.Diagnostics.XProcess`, namespace `Toolbelt.Diagnostics`) launches an external process and exposes its stdout/stderr as `IAsyncEnumerable<string>`, plus helpers such as `WaitForExitAsync()` and `WaitForOutputAsync()`.

## Commands

`global.json` selects Microsoft.Testing.Platform as the test runner, so `dotnet test` needs `--project` (VSTest-style arguments do not work).

```sh
dotnet build                                                    # build all projects
dotnet test --project XProcess.Test                             # run all tests (CI uses --output detailed)
dotnet test --project XProcess.Test --filter "Name=ExitCode_Test"   # run a single test
dotnet build XProcess -c Release                                # build and pack the .nupkg into _dist/
```

CI (`.github/workflows/unit-tests.yml`) runs the tests on ubuntu-latest with .NET 10 and .NET 11 on every push to `master`.

## Architecture

All behavior lives in `XProcess/XProcess.cs`; the other files in `XProcess/` are option, enum, and data types.

- Every output line is stored twice: in `OutputBuffer` (a locked `List`, backing `Output`/`StdOutput`/`StdError` and the `GetAndClearBuffered*` methods) and in an unbounded `Channel` (backing all `Get*AsyncStream` methods). Clearing the buffer does not affect the channel. The channel allows multiple readers, so concurrent streams compete for lines rather than each seeing every line.
- The async streams end when the channel is completed and empty. `Process_Exited` calls the parameterless `Process.WaitForExit()` first, which waits until redirected output is fully drained, and only then completes the exit-code task and the channel writer. Keep this ordering, or callers of `WaitForExitAsync()` and the streams can miss trailing output. `Dispose()` follows the same rule.
- The channel uses `AllowSynchronousContinuations = false` on purpose. With `true`, a consumer's `await foreach` body runs on the `Process` class's output reader thread, and calling `Dispose()` there deadlocks because `WaitForExit()` waits for that same thread.
- `WaitForOutputAsync()` first tests the predicate against all buffered output joined with `\n` (and clears the buffer), then tests each new line from the stream. Idle timeout is implemented by `XProcessIdleTimeoutCanceller`, a timer reset on every received line.
- `Dispose()` kills the process according to `XProcessOptions.WhenDisposing`. Killing the entire process tree uses `#if NET5_0_OR_GREATER`, because the library also targets `netstandard2.1`.
- The library targets `netstandard2.1` and `net6.0` through `net11.0`, with nullable warnings treated as errors. Use only APIs available on `netstandard2.1`, or guard them with `#if`. The `System.Threading.Channels` package is referenced only for `netstandard2.1`, because the other targets ship it in the shared framework.

## Tests

- `XProcess.Test` (NUnit + ChainingAssertion, run through MTP) does not mock processes. It launches the real helper app `XProcess.Test.Testee` as `dotnet testee.dll ...` with the test output folder as the working directory; the project reference copies `testee.dll` there.
- The testee's behavior is controlled by command-line switches parsed by CommandlineSwitchParser from `XProcess.Test.Testee/CommandLineOptions.cs` (for example `--exitcode 123`, `--burstcount 100`, `--infinitecounter`, `-n` to wait for a key press). To test a new scenario, add a property there and handle it in `Program.cs`.
- In the default `MixBoth` output mode the testee alternates stdout and stderr on every line, and many assertions depend on that exact split (for example `StdOutput` is `"Hello,\nNice to"`).
- `XProcess.Test/WarmUp.cs` starts one throwaway process before all tests. On Linux, under NUnit + Microsoft.Testing.Platform, the first async read of a redirected pipe in the test host fails with a spurious `SocketException` (99), and `Process` silently treats it as EOF, so one whole stream of that process is lost (dotnet/runtime#131915). Do not remove it while that issue is open.
- Many tests are timing-based (sleeps, timeouts, repeated runs to catch output-draining races), so a flaky failure may point to a real race in `XProcess.cs`.

## Releasing

- Bump `<Version>` in `XProcess/XProcess.csproj` and add a new entry at the top of `RELEASE-NOTES.txt`.
- At pack time, an MSBuild target in the csproj copies the topmost `v.x.y.z` section of `RELEASE-NOTES.txt` into the package release notes, so keep the `v.` heading format.
