using System;
using System.Threading;

namespace Toolbelt.Diagnostics
{
    internal class XProcessIdleTimeoutCanceller : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public CancellationToken Token => this._cancellationTokenSource.Token;

        private readonly System.Timers.Timer _timer = new();

        public XProcessIdleTimeoutCanceller(int idleTimeout)
        {
            if (idleTimeout > 0)
            {
                this._timer.AutoReset = false;
                this._timer.Interval = idleTimeout;
                this._timer.Elapsed += (sender, e) => this._cancellationTokenSource.Cancel();
                this._timer.Start();
            }
        }

        public void Ping()
        {
            if (this._cancellationTokenSource.IsCancellationRequested) return;
            this._timer.Stop();
            this._timer.Start();
        }

        public void Dispose()
        {
            this._timer.Stop();
            this._timer.Dispose();
            this._cancellationTokenSource.Dispose();
        }
    }
}
