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

        // Keep the callers' code after WaitForExitAsync() off the output reading flow, so that their Dispose() call does not wait for itself.
        // Holds null when Dispose() completes it before the process exits.
        private TaskCompletionSource<int?> ExitCodeTaskSource { get; } = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);

        private List<XProcessOutputFragment> OutputBuffer { get; } = new List<XProcessOutputFragment>();

        private Channel<XProcessOutputFragment> OutputChannel { get; }

        private long LastReceivedSequence { get; set; }

        private long LastJudgedSequence { get; set; }

        private bool Disposed { get; set; }

#if NET11_0_OR_GREATER
        private CancellationTokenSource ReadOutputCancellation { get; } = new CancellationTokenSource();

        private Task ReadOutputTask { get; }
#endif

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
            foreach (var envVar in options.EnvironmentVariables) startInfo.Environment[envVar.Key] = envVar.Value;

            this.Options = options;

            this.OutputChannel = Channel.CreateUnbounded<XProcessOutputFragment>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = true,
                // Keep the stream consumers' code off the Process class's output reader threads.
                AllowSynchronousContinuations = false
            });

            this.Process = new Process
            {
                StartInfo = startInfo
            };
#if NET11_0_OR_GREATER
            this.Process.Start();
            this.ReadOutputTask = this.ReadOutputAsync();
        }

        private async Task ReadOutputAsync()
        {
            try
            {
                var cancellationToken = this.ReadOutputCancellation.Token;
                await foreach (var line in this.Process.ReadAllLinesAsync(cancellationToken).ConfigureAwait(false))
                {
                    this.OnOutputReceived(line.StandardError ? XProcessOutputType.StdErr : XProcessOutputType.StdOut, line.Content);
                }
                await this.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                this.ExitCodeTaskSource.TrySetResult(this.Process.ExitCode);
            }
            catch (OperationCanceledException) { }
            // An error caused by the cancellation from Dispose() is not an error for the callers.
            catch (Exception ex) when (!this.ReadOutputCancellation.IsCancellationRequested)
            {
                this.ExitCodeTaskSource.TrySetException(ex);
            }
            finally
            {
                this.OutputChannel.Writer.TryComplete();
            }
        }
#else
            this.Process.Exited += this.Process_Exited;
            this.Process.OutputDataReceived += this.Process_OutputDataReceived;
            this.Process.ErrorDataReceived += this.Process_ErrorDataReceived;

            this.Process.Start();
            this.Process.BeginOutputReadLine();
            this.Process.BeginErrorReadLine();
            this.Process.EnableRaisingEvents = true;
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            this.OnOutputReceived(XProcessOutputType.StdOut, e.Data);
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            this.OnOutputReceived(XProcessOutputType.StdErr, e.Data);
        }

        private void Process_Exited(object? sender, EventArgs e)
        {
            // Process.Dispose() waits for this handler to return, so wait for the output to end on another thread.
            var process = (Process)sender!;
            Task.Run(() =>
            {
                try
                {
                    process.WaitForExit();
                    this.ExitCodeTaskSource.TrySetResult(process.ExitCode);
                }
                catch { } // Dispose() may have disposed the process after its drain timeout.
                this.OutputChannel.Writer.TryComplete();
            });
        }
#endif

        private void OnOutputReceived(XProcessOutputType outputType, string? data)
        {
            if (data == null) return;

            var fragment = new XProcessOutputFragment(outputType, data);
            // Write both under one lock, so the buffer and the channel hold the lines in sequence order.
            lock (this.OutputBuffer)
            {
                fragment.Sequence = ++this.LastReceivedSequence;
                this.OutputBuffer.Add(fragment);
                this.OutputChannel.Writer.TryWrite(fragment);
            }
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
            for (; ; )
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                if (reader.TryRead(out var fragment))
                {
                    yield return fragment;
                    continue;
                }

                // The channel is completed only after the process exits and its output is fully drained.
                try
                {
                    if (!await reader.WaitToReadAsync(cancellationToken)) yield break;
                }
                catch (OperationCanceledException) { yield break; }
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
                // Remove in one pass, because List.Remove() for each fragment takes O(n^2) under the lock.
                var fragmentSet = new HashSet<XProcessOutputFragment>(fragments);
                this.OutputBuffer.RemoveAll(fragmentSet.Contains);
                return fragments;
            }
        }

        /// <summary>
        /// Wait until the predicate function applied to the process output returns true, or it's timeout.
        /// </summary>
        /// <param name="predicate">The predicate function applied to the process output.</param>
        /// <param name="millsecondsTimeout">Timeout milliseconds.</param>
        /// <returns>If the predicate function returns true before timeout, the return value is true. otherwise, it is false.</returns>
        public ValueTask<bool> WaitForOutputAsync(Func<string, bool> predicate, int millsecondsTimeout)
        {
            return this.WaitForOutputAsync(predicate, option => option.ProcessTimeout = millsecondsTimeout);
        }

        /// <summary>
        /// Wait until the predicate function applied to the process output returns true, or the cancellation token is canceled.
        /// </summary>
        /// <param name="predicate">The predicate function applied to the process output.</param>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns>If the predicate function returns true before the cancellation token is canceled, the return value is true.</returns>
        public ValueTask<bool> WaitForOutputAsync(Func<string, bool> predicate, CancellationToken cancellationToken)
        {
            return this.WaitForOutputAsync(predicate, option => option.CancellationToken = cancellationToken);
        }

        /// <summary>
        /// Wait until the predicate function applied to the process output returns true, or it's timeout or canceled according to the wait option configuration.
        /// </summary>
        /// <param name="predicate">The predicate function applied to the process output.</param>
        /// <param name="configure">The callback to configure the wait options.</param>
        /// <returns>If the predicate function returns true before the cancellation token is canceled or any timeouts, the return value is true.</returns>
        public async ValueTask<bool> WaitForOutputAsync(Func<string, bool> predicate, Action<XProcessWaitOptions> configure)
        {
            var options = new XProcessWaitOptions();
            configure(options);

            using var idleTimeoutCanceller = new XProcessIdleTimeoutCanceller(options.IdleTimeout);
            using var processTimeoutSource = options.ProcessTimeout > 0 ? new CancellationTokenSource(options.ProcessTimeout) : null;
            using var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(idleTimeoutCanceller.Token, processTimeoutSource?.Token ?? CancellationToken.None, options.CancellationToken);
            var cancellationToken = linkedCancellationSource.Token;

            var unjudgedFragments = default(XProcessOutputFragment[]);
            lock (this.OutputBuffer) unjudgedFragments = this.OutputBuffer.Where(f => f.Sequence > this.LastJudgedSequence).ToArray();
            // Judging the joined text does not mark its lines as judged, so the loop below still judges each of them.
            if (predicate(string.Join("\n", unjudgedFragments.Select(f => f.Data)))) return true;

            await foreach (var fragment in this.GetOutputFragmentAsyncStream().WithCancellation(cancellationToken))
            {
                idleTimeoutCanceller.Ping();
                if (cancellationToken.IsCancellationRequested) break;
                if (fragment.Sequence <= this.LastJudgedSequence) continue;
                this.LastJudgedSequence = fragment.Sequence;
                if (predicate(fragment.Data)) return true;
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

#if NET11_0_OR_GREATER
            // Read the output to the end only if the process has already exited.
            if (!this.Process.HasExited) this.ReadOutputCancellation.Cancel();
            try
            {
                // A child process that holds the output pipe can keep the output from ending.
                if (!this.ReadOutputTask.Wait(this.Options.DrainTimeoutWhenDisposing))
                {
                    this.ReadOutputCancellation.Cancel();
                    this.ReadOutputTask.Wait();
                }
            }
            catch { }
#else
            this.Process.Exited -= this.Process_Exited;
            // The parameterless WaitForExit() is the one that waits for the output to end, so run it on another thread to bound the wait.
            if (this.Process.HasExited) Task.Run(this.Process.WaitForExit).Wait(this.Options.DrainTimeoutWhenDisposing);
            this.Process.OutputDataReceived -= this.Process_OutputDataReceived;
            this.Process.ErrorDataReceived -= this.Process_ErrorDataReceived;
#endif

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
                    // Kill() does not wait for the exit, so wait a moment to get the real exit code.
                    this.Process.WaitForExit(1000);
                }
                catch { }
            }
            this.ExitCodeTaskSource.TrySetResult(this.Process.HasExited ? this.Process.ExitCode : null);
            this.OutputChannel.Writer.TryComplete();
            this.Process.Dispose();
#if NET11_0_OR_GREATER
            this.ReadOutputCancellation.Dispose();
#endif
        }
    }
}
