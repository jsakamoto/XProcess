# XProcess [![NuGet Package](https://img.shields.io/nuget/v/XProcess.svg)](https://www.nuget.org/packages/XProcess/) [![unit tests](https://github.com/jsakamoto/XProcess/actions/workflows/unit-tests.yml/badge.svg)](https://github.com/jsakamoto/XProcess/actions/workflows/unit-tests.yml)

This is a .NET library that allows you to invoke an external process and expose its output as an async stream in C# 8.0.

## Usage examples

### Launch a process, wait for it to exit, and refer to the exit code.

```csharp
using Toolbelt.Diagnostics;
...
using var process = await XProcess.Start("foo.exe").WaitForExitAsync();
if (process.ExitCode != 0) throw new Exception(process.Output);
```

### Launch a process and get console outputs as an async stream.

```csharp
using Toolbelt.Diagnostics;
...
using var process = XProcess.Start("foo.exe");
await foreach(string output in process.GetOutputAsyncStream())
{
  // do something.
}
// When reaching here, it means the process was exited.
```

### Launch a process and wait for a specific output within a particular time.

```csharp
using Toolbelt.Diagnostics;
...
using var process = XProcess.Start("foo.exe");
var found = await process.WaitForOutputAsync(str => str.Contains("Now listening on:"), option => {
  option.IdleTimeout = 1000;
  option.ProcessTimeout = 5000;
});
// When the "found" is false, it means the process 
// has not outputted "Now listening on:" in 5 seconds 
// or has not output any response over 1 second.
```

### Launch a process with specific environment variables.

```csharp
using Toolbelt.Diagnostics;
...
await XProcess.Start("foo.exe", options => {
  options.EnvironmentVariables["FOO"] = "BAR";
}).WaitForExitAsync();
```

## Release Notes

Release notes are [here.](https://github.com/jsakamoto/XProcess/blob/master/RELEASE-NOTES.txt)

## License

[Mozilla Public License ver.2.0](https://github.com/jsakamoto/XProcess/blob/master/LICENSE)

