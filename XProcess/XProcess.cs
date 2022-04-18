using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
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

        private bool Disposed { get; set; }

        public Process Process { get; }

        public int? ExitCode => this.ExitCodeTaskSource.Task.IsCompletedSuccessfully ? this.ExitCodeTaskSource.Task.Result : null;

        public string Output => this.GetOutputString(_ => true);

        public string StdOutput => this.GetOutputString(f => f.Type == XProcessOutputType.StdOut);

        public string StdError => this.GetOutputString(f => f.Type == XProcessOutputType.StdErr);


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
            await this.ExitCodeTaskSource.Task;
            return this;
        }

        public XProcess(string? filename, string? arguments, string? workingDirectory, XProcessOptions options)
            : this(new ProcessStartInfo
            {
                FileName = filename!,
                Arguments = arguments!,
                WorkingDirectory = workingDirectory!
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
            this.Process.Exited += this.Process_Exited;
            this.Process.OutputDataReceived += this.Process_OutputDataReceived;
            this.Process.ErrorDataReceived += this.Process_ErrorDataReceived;

            this.Process.Start();
            this.Process.BeginOutputReadLine();
            this.Process.BeginErrorReadLine();
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            this.OnOutputReceived(XProcessOutputType.StdOut, e.Data);
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            this.OnOutputReceived(XProcessOutputType.StdErr, e.Data);
        }

        private void OnOutputReceived(XProcessOutputType outputType, string? data)
        {
            if (data == null) return;

            var fragment = new XProcessOutputFragment(outputType, data);
            lock (this.OutputBuffer) this.OutputBuffer.Add(fragment);
            this.OutputChannel.Writer.TryWrite(fragment);
        }

        private void Process_Exited(object? sender, EventArgs e)
        {
            var process = (Process)sender!;
            this.ExitCodeTaskSource.TrySetResult(process.ExitCode);
            lock (this.CancellerByExit) if (!this.CancellerByExit.IsCancellationRequested) this.CancellerByExit.Cancel();
        }

        private string GetOutputString(Func<XProcessOutputFragment, bool> predicate)
        {
            var current = default(XProcessOutputFragment[]);
            lock (this.OutputBuffer) current = this.OutputBuffer.ToArray();
            return string.Join("\n", current.Where(f => predicate(f)).Select(f => f.Data));
        }

        public IAsyncEnumerable<string> GetOutputAsyncStream() => this.GetOutputAsyncStream(default);

        public async IAsyncEnumerable<string> GetOutputAsyncStream([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var fragment in this.GetOutputFragmentAsyncStream().WithCancellation(cancellationToken))
            {
                yield return fragment.Data;
            }
        }

        public IAsyncEnumerable<string> GetStdOutAsyncStream() => this.GetStdOutAsyncStream(default);

        public async IAsyncEnumerable<string> GetStdOutAsyncStream([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var fragment in this.GetOutputFragmentAsyncStream().WithCancellation(cancellationToken))
            {
                if (fragment.Type == XProcessOutputType.StdOut) yield return fragment.Data;
            }
        }

        public IAsyncEnumerable<string> GetStdErrAsyncStream() => this.GetStdErrAsyncStream(default);

        public async IAsyncEnumerable<string> GetStdErrAsyncStream([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var fragment in this.GetOutputFragmentAsyncStream().WithCancellation(cancellationToken))
            {
                if (fragment.Type == XProcessOutputType.StdErr) yield return fragment.Data;
            }
        }

        public IAsyncEnumerable<XProcessOutputFragment> GetOutputFragmentAsyncStream() => this.GetOutputFragmentAsyncStream(default);

        public async IAsyncEnumerable<XProcessOutputFragment> GetOutputFragmentAsyncStream([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var reader = this.OutputChannel.Reader;
            var ctoken = CancellationTokenSource.CreateLinkedTokenSource(this.CancellerByExit.Token, cancellationToken).Token;
            for (; ; )
            {
                var fragment = default(XProcessOutputFragment);
                try
                {
                    fragment = await reader.ReadAsync(ctoken);
                }
                catch (OperationCanceledException) { yield break; }
                yield return fragment;
            }
        }

        public string GetAndClearBufferedOutput()
        {
            return string.Join("\n", this.GetAndClearBufferedFragments().Select(f => f.Data));
        }

        public string GetAndClearBufferedStdOut()
        {
            return string.Join("\n", this.GetAndClearBufferedFragments(f => f.Type == XProcessOutputType.StdOut).Select(f => f.Data));
        }

        public string GetAndClearBufferedStdErr()
        {
            return string.Join("\n", this.GetAndClearBufferedFragments(f => f.Type == XProcessOutputType.StdErr).Select(f => f.Data));
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

        public async ValueTask<bool> WaitForOutputAsync(Func<string, bool> predicate, int millsecondsTimeout)
        {
            var cts = new CancellationTokenSource(millsecondsTimeout);
            return await this.WaitForOutputAsync(predicate, cts.Token);
        }

        public async ValueTask<bool> WaitForOutputAsync(Func<string, bool> predicate, CancellationToken cancellationToken)
        {
            var bufferedOutput = this.GetAndClearBufferedOutput();
            if (predicate(bufferedOutput)) return true;

            await foreach (var output in this.GetOutputAsyncStream().WithCancellation(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (predicate(output)) return true;
            }
            return false;
        }

        public void Dispose()
        {
            lock (this)
            {
                if (this.Disposed) return;
                this.Disposed = true;
            }

            this.Process.Exited -= this.Process_Exited;
            this.Process.OutputDataReceived -= this.Process_OutputDataReceived;
            this.Process.ErrorDataReceived -= this.Process_ErrorDataReceived;

            if (this.Options.WhenDisposing != XProcessTerminate.No)
            {
                try
                {
#if NET5_0_OR_GREATER
                    var entireProcessTree = this.Options.WhenDisposing == XProcessTerminate.EntireProcessTree;
                    this.Process.Kill(entireProcessTree);
#else
                    this.Process.Kill();
#endif
                }
                catch { }
            }
            this.ExitCodeTaskSource.TrySetResult(this.Process.HasExited ? this.Process.ExitCode : 0);
            lock (this.CancellerByExit) if (!this.CancellerByExit.IsCancellationRequested) this.CancellerByExit.Cancel();
            this.Process.Dispose();
        }
    }
}
