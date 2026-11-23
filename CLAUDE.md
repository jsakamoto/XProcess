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
- Output is received in two ways, split by `#if NET11_0_OR_GREATER`. On .NET 11 and later, `ReadOutputAsync` reads both streams with `Process.ReadAllLinesAsync()`, then waits for the exit. On older targets, the `OutputDataReceived`/`ErrorDataReceived` events deliver the lines. The test project targets both `net10.0` and `net11.0` so that both ways are tested.
- The async streams end when the channel is completed and empty. Complete the exit-code task and the channel writer only after the output is fully drained, or callers of `WaitForExitAsync()` and the streams can miss trailing output. On .NET 11 this holds because `ReadOutputAsync` completes them after `ReadAllLinesAsync()` ends. On older targets, `Process_Exited` starts a thread pool task that calls the parameterless `Process.WaitForExit()` first, which waits until redirected output is fully drained. The handler must not wait by itself, because `Process.Dispose()` waits for the `Exited` handler to return. `Dispose()` follows the same rule, but waits only up to `XProcessOptions.DrainTimeoutWhenDisposing` (5000 ms by default), because a child process that inherits the output pipe can keep the output from ending.
- No caller code may run inline on the output reading flow, because `Dispose()` waits for that flow (`WaitForExit()` on older targets, `ReadOutputTask` on .NET 11), and calling it there deadlocks. So the channel uses `AllowSynchronousContinuations = false`, and the exit-code task uses `TaskCreationOptions.RunContinuationsAsynchronously`.
- `WaitForOutputAsync()` never re-tests a line that an earlier call has already tested. Each line gets a sequence number (`XProcessOutputFragment.Sequence`), and `LastJudgedSequence` holds the last one tested. A call first tests the predicate against the buffered lines after `LastJudgedSequence` joined with `\n`, then tests each new line from the stream. Only the second step moves `LastJudgedSequence`, because a line that fails as part of the joined text can still match alone (tested by `WaitForOutputAsync_AfterExited_Test`). It does not clear the buffer. `OnOutputReceived` writes the buffer and the channel under one lock, so both hold the lines in sequence order; otherwise a line could be skipped as already tested. Idle timeout is implemented by `XProcessIdleTimeoutCanceller`, a timer reset on every received line.
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
