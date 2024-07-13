namespace Toolbelt.Diagnostics.Test;

public class CommandLineOptions
{
    public int ExitCode { get; set; }

    public bool InfiniteCounter { get; set; }

    public int CounterDelay { get; set; }

    public bool NeverExitUntilEnterAnyKey { get; set; }

    public bool SpawnChildProcess { get; set; }

    public OutputMode OutputMode { get; set; } = OutputMode.MixBoth;

    public string? ShowEnvVar { get; set; }
}
