using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Toolbelt.Diagnostics
{
    public class XProcess : IDisposable
    {
        private XProcessOptions Options { get; }

        private TaskCompletionSource<int> ExitCodeTaskSource { get; } = new TaskCompletionSource<int>();

        private List<XProcessOutputFragment> OutputBuffer { get; } = new List<XProcessOutputFragment>();

        private Channel<XProcessOutputFragment> OutputChannel { get; }

        private CancellationTokenSource CancellerByExit { get; } = new CancellationTokenSource();

        public Process Process { get; }

        public int? ExitCode => ExitCodeTaskSource.Task.IsCompletedSuccessfully ? ExitCodeTaskSource.Task.Result : null;

        public string Output => GetOutputString(_ => true);

        public string StdOutput => GetOutputString(f => f.Type == XProcessOutputType.StdOut);

        public string StdError => GetOutputString(f => f.Type == XProcessOutputType.StdErr);


        public static XProcess Start(string? filename, string? arguments = "", string? workingDirectory = "", Action<XProcessOptions>? configure = null)
        {
            var options = new XProcessOptions();
            configure?.Invoke(options);
            return new XProcess(filename, arguments, workingDirectory, options);
        }

        public static XProcess Start(ProcessStartInfo startInfo, Action<XProcessOptions>? configure = null)
        {
            var options = new XProcessOptions();
            configure?.Invoke(options);
            return new XProcess(startInfo, options);
        }

        public async Task<XProcess> WaitForExitAsync()
        {
            await ExitCodeTaskSource.Task;
            return this;
        }

        public XProcess(string? filename, string? arguments, string? workingDirectory, XProcessOptions options)
            : this(new ProcessStartInfo
            {
                FileName = filename,
                Arguments = arguments,
                WorkingDirectory = workingDirectory
            }, options)
        {
        }

        public XProcess(ProcessStartInfo startInfo, XProcessOptions options)
        {
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            this.Options = options;

            this.OutputChannel = Channel.CreateUnbounded<XProcessOutputFragment>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = true,
                AllowSynchronousContinuations = true
            });

            this.Process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            this.Process.Exited += Process_Exited;
            this.Process.OutputDataReceived += Process_OutputDataReceived;
            this.Process.ErrorDataReceived += Process_ErrorDataReceived;

            this.Process.Start();
            this.Process.BeginOutputReadLine();
            this.Process.BeginErrorReadLine();
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            OnOutputReceived(XProcessOutputType.StdOut, e.Data);
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            OnOutputReceived(XProcessOutputType.StdErr, e.Data);
        }

        private void OnOutputReceived(XProcessOutputType outputType, string? data)
        {
            if (data == null) return;

            var fragment = new XProcessOutputFragment(outputType, data);
            lock (this.OutputBuffer) this.OutputBuffer.Add(fragment);
            this.OutputChannel.Writer.TryWrite(fragment);
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            var process = (Process)sender;
            this.ExitCodeTaskSource.TrySetResult(process.ExitCode);
            lock (this.CancellerByExit) if (!this.CancellerByExit.IsCancellationRequested) this.CancellerByExit.Cancel();
        }

        private string GetOutputString(Func<XProcessOutputFragment, bool> predicate)
        {
            var current = default(XProcessOutputFragment[]);
            lock (this.OutputBuffer) current = this.OutputBuffer.ToArray();
            return string.Join("\n", current.Where(f => predicate(f)).Select(f => f.Data));
        }

        public async IAsyncEnumerable<string> GetOutputAsyncStream()
        {
            await foreach (var fragment in GetOutputFragmentAsyncStream())
            {
                yield return fragment.Data;
            }
        }

        public async IAsyncEnumerable<string> GetStdOutAsyncStream()
        {
            await foreach (var fragment in GetOutputFragmentAsyncStream())
            {
                if (fragment.Type == XProcessOutputType.StdOut) yield return fragment.Data;
            }
        }

        public async IAsyncEnumerable<string> GetStdErrAsyncStream()
        {
            await foreach (var fragment in GetOutputFragmentAsyncStream())
            {
                if (fragment.Type == XProcessOutputType.StdErr) yield return fragment.Data;
            }
        }

        public async IAsyncEnumerable<XProcessOutputFragment> GetOutputFragmentAsyncStream()
        {
            var reader = OutputChannel.Reader;
            var cancellerToken = this.CancellerByExit.Token;
            for (; ; )
            {
                var fragment = default(XProcessOutputFragment);
                try
                {
                    fragment = await reader.ReadAsync(cancellerToken);
                }
                catch (OperationCanceledException) { yield break; }
                yield return fragment;
            }
        }

        public string GetAndClearBufferedOutput()
        {
            return string.Join("\n", GetAndClearBufferedFragments().Select(f => f.Data));
        }

        public string GetAndClearBufferedStdOut()
        {
            return string.Join("\n", GetAndClearBufferedFragments(f => f.Type == XProcessOutputType.StdOut).Select(f => f.Data));
        }

        public string GetAndClearBufferedStdErr()
        {
            return string.Join("\n", GetAndClearBufferedFragments(f => f.Type == XProcessOutputType.StdErr).Select(f => f.Data));
        }

        public IEnumerable<XProcessOutputFragment> GetAndClearBufferedFragments(Func<XProcessOutputFragment, bool>? predicate = null)
        {
            lock (this.OutputBuffer)
            {
                predicate ??= _ => true;
                var fragments = this.OutputBuffer.Where(predicate).ToArray();
                foreach (var fragment in fragments)
                {
                    this.OutputBuffer.Remove(fragment);
                }
                return fragments;
            }
        }


        public void Dispose()
        {
            this.Process.Exited -= Process_Exited;
            this.Process.OutputDataReceived -= Process_OutputDataReceived;
            this.Process.ErrorDataReceived -= Process_ErrorDataReceived;

            if (this.Options.TerminateWhenDisposing) this.Process.Kill();
            this.Process.Dispose();
            lock (this.CancellerByExit) if (!this.CancellerByExit.IsCancellationRequested) this.CancellerByExit.Cancel();
        }
    }
}
