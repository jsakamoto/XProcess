using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Toolbelt.Diagnostics
{
    public class XProcessOptions
    {
        [Obsolete("use the 'WhenDisposing' option instead."), EditorBrowsable(EditorBrowsableState.Never)]
        public bool TerminateWhenDisposing
        {
            get => this.WhenDisposing != XProcessTerminate.No;
            set
            {
                if (value == false)
                    this.WhenDisposing = XProcessTerminate.No;
                else if (this.WhenDisposing == XProcessTerminate.No)
                    this.WhenDisposing = XProcessTerminate.Yes;
            }
        }

        /// <summary>
        /// Get or set how to terminate the process or terminate its entire process tree or nothing when the process object is disposing.
        /// </summary>
        public XProcessTerminate WhenDisposing { get; set; } = XProcessTerminate.Yes;

        /// <summary>
        /// The environment variables that apply to this process and its child processes.
        /// </summary>
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    }
}
