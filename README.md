# XProcess [![NuGet Package](https://img.shields.io/nuget/v/XProcess.svg)](https://www.nuget.org/packages/XProcess/) [![unit tests](https://github.com/jsakamoto/XProcess/actions/workflows/unit-tests.yml/badge.svg)](https://github.com/jsakamoto/XProcess/actions/workflows/unit-tests.yml)

This is a library for .NET that allows you to invoke an external process, and expose its output as an async stream in C# 8.0.

## Usage

```csharp
using Toolbelt.Diagnostics;
...
using var process = await XProcess.Start("foo.exe").WaitForExitAsync();
if (process.ExitCode != 0) throw new Exception(process.Output);
```

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

```csharp
using Toolbelt.Diagnostics;
...
using var process = XProcess.Start("foo.exe");
var found = await process.WaitForOutputAsync(str => str.Contains("Now listening on:"), millsecondsTimeout: 5000);
// If the "found" is false, it means the process had not outputed "Now listening on:" in 5 sec.
```

## Release Notes

Release notes is [here.](https://github.com/jsakamoto/XProcess/blob/master/RELEASE-NOTES.txt)

## License

[Mozilla Public License ver.2.0](https://github.com/jsakamoto/XProcess/blob/master/LICENSE)

