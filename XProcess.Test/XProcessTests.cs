using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Toolbelt.Diagnostics.Test;

public class XProcessTests
{
    private static readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory;

    [Test]
    public async Task ExitCode_Test()
    {
        using var processA = await XProcess.Start("dotnet", "testee.dll", baseDir).WaitForExitAsync();
        processA.ExitCode.Is(0);

        using var processB = await XProcess.Start("dotnet", "testee.dll --exitcode 123", baseDir).WaitForExitAsync();
        processB.ExitCode.Is(123);
    }

    [Test]
    public void ExitCode_of_running_process_is_null_Test()
    {
        using var process = XProcess.Start("dotnet", "testee.dll --infinitecounter", baseDir);
        process.ExitCode.IsNull();
    }

    [Test]
    public void Start_when_file_not_found_Test()
    {
        Assert.ThrowsAsync<Win32Exception>(async () =>
        {
            using var process = await XProcess.Start(Path.Combine(".", Guid.NewGuid().ToString("N")) + ".exe").WaitForExitAsync();
        });
    }

    [Test]
    public async Task Output_Test()
    {
        using var process = await XProcess.Start("dotnet", "testee.dll", baseDir).WaitForExitAsync();
        process.Output.Is("Hello,\neveryone.\nNice to\nmeet you.");
    }

    [Test]
    public async Task StdOutput_Test()
    {
        using var process = await XProcess.Start("dotnet", "testee.dll", baseDir).WaitForExitAsync();
        process.StdOutput.Is("Hello,\nNice to");
    }

    [Test]
    public async Task StdError_Test()
    {
        using var process = await XProcess.Start("dotnet", "testee.dll", baseDir).WaitForExitAsync();
        process.StdError.Is("everyone.\nmeet you.");
    }

    [Test]
    public async Task GetOutputAsyncStream_Test()
    {
        using var process = XProcess.Start("dotnet", "testee.dll", baseDir);
        var outputs = new StringBuilder();
        await foreach (var output in process.GetOutputAsyncStream())
        {
            outputs.AppendLine(output);
        }

        process.ExitCode.Is(0);
        var nl = Environment.NewLine;
        outputs.ToString().Is($"Hello,{nl}everyone.{nl}Nice to{nl}meet you.{nl}");
    }

    [Test]
    public async Task GetStdOutAsyncStream_Test()
    {
        using var process = XProcess.Start("dotnet", "testee.dll --exitcode 234", baseDir);
        var outputs = new StringBuilder();
        await foreach (var output in process.GetStdOutAsyncStream())
        {
            outputs.AppendLine(output);
        }

        process.ExitCode.Is(234);
        var nl = Environment.NewLine;
        outputs.ToString().Is($"Hello,{nl}Nice to{nl}");
    }

    [Test]
    public async Task GetStdErrtAsyncStream_Test()
    {
        using var process = XProcess.Start("dotnet", "testee.dll", baseDir);
        var outputs = new StringBuilder();
        await foreach (var output in process.GetStdErrAsyncStream())
        {
            outputs.AppendLine(output);
        }

        process.ExitCode.Is(0);
        var nl = Environment.NewLine;
        outputs.ToString().Is($"everyone.{nl}meet you.{nl}");
    }

    [Test]
    public async Task GetAndClearBufferedOutput_Test()
    {
        using var process = XProcess.Start("dotnet", "testee.dll -i -o mixboth", baseDir);
        await process.GetOutputAsyncStream().GetAsyncEnumerator().MoveNextAsync();

        await Task.Delay(500);
        var outputLines1 = process.GetAndClearBufferedOutput().Split('\n');
        (outputLines1.Length > 0).IsTrue();

        await Task.Delay(500);
        var outputLines2 = process.Output.Split('\n');
        (outputLines2.Length > 0).IsTrue();

        var outputLines = outputLines1.Concat(outputLines2).ToArray();
        for (var i = 0; i < outputLines.Length; i++)
        {
            outputLines[i].Is(i.ToString());
        }
    }

    [Test]
    public async Task WaitForOutputAsync_Test()
    {
        using var process = XProcess.Start("dotnet", "testee.dll -i", baseDir);
        var found = await process.WaitForOutputAsync(str => str == "20", millsecondsTimeout: 3000);
        found.IsTrue();
    }

    [Test]
    public async Task WaitForOutputAsync_AfterExited_Test()
    {
        using var process = XProcess.Start("dotnet", "testee.dll", baseDir);
        await process.WaitForExitAsync();
        var found = await process.WaitForOutputAsync(str => str == "Nice to", millsecondsTimeout: 30000);
        found.IsTrue();
    }

    [Test]
    public async Task WaitForOutputAsync_AfterExited_NotFound_Test()
    {
        using var process = XProcess.Start("dotnet", "testee.dll", baseDir);
        await process.WaitForExitAsync();
        var found = await process.WaitForOutputAsync(str => false, millsecondsTimeout: 30000);
        found.IsFalse();
    }

    [Test]
    public async Task WaitForOutputAsync_TimeOuted_Test()
    {
        using var process = XProcess.Start("dotnet", "testee.dll -n", baseDir);
        var found = await process.WaitForOutputAsync(str => false, millsecondsTimeout: 3000);
        found.IsFalse();
    }

    [Test]
    public async Task WaitForOutputAsync_IdleTimeout_Test()
    {
        // Given
        using var process = XProcess.Start("dotnet", "testee.dll --infinitecounter --counterdelay 10", baseDir);

        // When: Wait with the 200ms idle timeout, and the process will not respond in 200msec at some point, for 3sec.
        var waitTask = process.WaitForOutputAsync(str => false, option => option.IdleTimeout = 200).AsTask();
        var processTimeoutTask = Task.Delay(3000);
        await Task.WhenAny(waitTask, processTimeoutTask);

        // Then: the waiting task was canceled.
        waitTask.IsCompleted.IsTrue();
        waitTask.Result.IsFalse();
    }

    [Test]
    public async Task WaitForOutputAsync_DoNot_IdleTimeout_Test()
    {
        // Given
        using var process = XProcess.Start("dotnet", "testee.dll --infinitecounter", baseDir);

        // When: Wait with the 200ms idle timeout, but the process keeps responding each 100msec, for 3sec.
        var waitTask = process.WaitForOutputAsync(str => false, option => option.IdleTimeout = 200);
        await Task.Delay(3000);

        // Then: the process never completed and the waiting task wasn't canceled.
        waitTask.IsCompleted.IsFalse();
        process.ExitCode.IsNull();
    }

    private async ValueTask<(XProcess ParentProcess, Process ChildProcess)> StartTesteeWithChildProcessAsync(XProcessTerminate whenDisposing)
    {
        var parentProcess = XProcess.Start("dotnet", "testee.dll -n --spawnchildprocess", baseDir, options =>
        {
            options.WhenDisposing = whenDisposing;
        });

        var childProcessId = -1;
        var result = await parentProcess.WaitForOutputAsync(output =>
        {
            var m = Regex.Match(output, "Child Process Id: (?<pid>\\d+)");
            if (m.Success)
            {
                childProcessId = int.Parse(m.Groups["pid"].Value);
                return true;
            }
            return false;
        },
        millsecondsTimeout: 5000);
        result.IsTrue();

        var childProcess = Process.GetProcessById(childProcessId);
        if (childProcess == null) throw new Exception($"The child process (pid: {childProcessId}) was not found.");

        return (parentProcess, childProcess);
    }

    [Test, Parallelizable(ParallelScope.Self)]
    public async Task Terminate_when_Disposing_with_ChildProcess_Test()
    {
        // Given
        var processes = await this.StartTesteeWithChildProcessAsync(whenDisposing: XProcessTerminate.EntireProcessTree);
        using var parentProcess = processes.ParentProcess;
        using var childProcess = processes.ChildProcess;
        var parentProcessId = parentProcess.Process.Id;
        var childProcessId = childProcess.Id;

        // When
        parentProcess.Dispose();

        // Then
        try
        {
            Task.WaitAll([childProcess.WaitForExitAsync()], millisecondsTimeout: 5000);
            childProcess.HasExited.IsTrue();

            Assert.Throws<ArgumentException>(() => Process.GetProcessById(parentProcessId));
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(childProcessId));
        }
        finally { try { childProcess.Kill(); } catch { } }
    }

    [Test, Parallelizable(ParallelScope.Self)]
    public async Task Terminate_when_Disposing_without_ChildProcess_Test()
    {
        // Given
        var processes = await this.StartTesteeWithChildProcessAsync(whenDisposing: XProcessTerminate.Yes);
        using var parentProcess = processes.ParentProcess;
        using var childProcess = processes.ChildProcess;
        var parentProcessId = parentProcess.Process.Id;
        var childProcessId = childProcess.Id;

        // When
        parentProcess.Dispose();

        // Then
        try
        {
            await Task.Delay(5000);
            childProcess.HasExited.IsFalse();

            Assert.Throws<ArgumentException>(() => Process.GetProcessById(parentProcessId));
            Process.GetProcessById(childProcessId).IsNotNull();
        }
        finally { try { childProcess.Kill(); } catch { } }
    }

    [Test, Parallelizable(ParallelScope.Self)]
    public async Task DoNot_Terminate_when_Disposing_Test()
    {
        // Given
        var processes = await this.StartTesteeWithChildProcessAsync(whenDisposing: XProcessTerminate.No);
        using var parentProcess = processes.ParentProcess;
        using var childProcess = processes.ChildProcess;
        var parentProcessId = parentProcess.Process.Id;
        var childProcessId = childProcess.Id;

        // When
        parentProcess.Dispose();

        // Then
        try
        {
            await Task.Delay(5000);
            childProcess.HasExited.IsFalse();

            Process.GetProcessById(parentProcessId).IsNotNull();
            Process.GetProcessById(childProcessId).IsNotNull();
        }
        finally
        {
            try
            {
                childProcess.Kill();
                using var p = Process.GetProcessById(parentProcessId);
                p.Kill();
            }
            catch { }
        }
    }

    [Test]
    public async Task Options_EnvironmentVariables_Test()
    {
        var envVar = Guid.NewGuid().ToString("N");
        using var process = await XProcess.Start("dotnet", "testee.dll --showenvvar USERNAME", baseDir, options =>
        {
            options.EnvironmentVariables["USERNAME"] = envVar;
        }).WaitForExitAsync();
        process.Output.Is($"USERNAME: \"{envVar}\"");
    }
}