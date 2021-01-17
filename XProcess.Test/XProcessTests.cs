using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Toolbelt.Diagnostics.Test
{
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
            outputs.ToString().Is("Hello,\r\neveryone.\r\nNice to\r\nmeet you.\r\n");
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
            outputs.ToString().Is("Hello,\r\nNice to\r\n");
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
            outputs.ToString().Is("everyone.\r\nmeet you.\r\n");
        }

        [Test]
        public async Task GetAndClearBufferedOutput_Test()
        {
            using var process = XProcess.Start("dotnet", "testee.dll -i -o mixboth", baseDir);
            await process.GetOutputAsyncStream().GetAsyncEnumerator().MoveNextAsync();

            await Task.Delay(100);
            var outputLines1 = process.GetAndClearBufferedOutput().Split('\n');
            (outputLines1.Length > 0).IsTrue();

            await Task.Delay(100);
            var outputLines2 = process.Output.Split('\n');
            (outputLines2.Length > 0).IsTrue();

            var outputLines = outputLines1.Concat(outputLines2).ToArray();
            for (var i = 0; i < outputLines.Length; i++)
            {
                outputLines[i].Is(i.ToString());
            }
        }
    }
}