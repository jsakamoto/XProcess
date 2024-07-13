using System.Threading;

namespace Toolbelt.Diagnostics
{
    /// <summary>
    /// Represents the options for waiting task to be cancelled.
    /// </summary>
    public class XProcessWaitOptions
    {
        /// <summary>
        /// Gets or sets the timeout milliseconds for waiting the process to be completed.<br/>
        /// When the process is not completed in this time, the waiting task will be cancelled.<br/>
        /// When this property is set to zero, the waiting task will not be cancelled by this timeout.
        /// </summary>
        public int ProcessTimeout { get; set; } = 0;

        /// <summary>
        /// Gets or sets the timeout milliseconds for the process to be any responded.<br/>
        /// When the process is not responded in this time, the waiting task will be cancelled.<br/>
        /// When this property is set to zero, the waiting task will not be cancelled by this timeout.
        /// </summary>
        public int IdleTimeout { get; set; } = 0;

        /// <summary>
        /// Gets or sets the cancellation token to cancel the waiting task.
        /// </summary>
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    }
}
