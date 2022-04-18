namespace Toolbelt.Diagnostics;

/// <summary>
/// Represent how to terminate the process or not.
/// </summary>
public enum XProcessTerminate
{
    /// <summary>
    /// Don't terminate the process.
    /// </summary>
    No,

    /// <summary>
    /// Terminate the process. (witout child processes)
    /// </summary>
    Yes,

    /// <summary>
    /// Terminate the entire process tree.<br/>
    /// (This option is only available on .NET 5.0 or later.)
    /// </summary>
    EntireProcessTree
}
