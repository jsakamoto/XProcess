# XProcess [![NuGet Package](https://img.shields.io/nuget/v/XProcess.svg)](https://www.nuget.org/packages/XProcess/)

This is a library for .NET that allows you to invoke an external process, and expose its output as an async stream in C# 8.0.

## Usage

```csharp
using Toolbelt.Diagnostics;
...
using XProcess process = await XProcess.Start("foo.exe").WaitForExitAsync();
if (process.ExitCode != 0) throw new Exception(process.Output);
```

```csharp
using Toolbelt.Diagnostics;
...
using var process = await XProcess.Start("foo.exe");
await foreach(string output in process.GetOutputAsyncStream())
{
  // do something.
}
// When reaching here, it means the process was exited.
```


## Release Notes

Release notes is [here.](https://github.com/jsakamoto/XProcess/blob/master/RELEASE-NOTES.txt)

## License

[Mozilla Public License ver.2.0](https://github.com/jsakamoto/XProcess/blob/master/LICENSE)

