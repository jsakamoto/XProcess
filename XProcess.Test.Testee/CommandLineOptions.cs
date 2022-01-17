﻿namespace Toolbelt.Diagnostics.Test
{
    public class CommandLineOptions
    {
        public int ExitCode { get; set; }

        public bool InfiniteCounter { get; set; }

        public bool NeverExitUntilEnterAnyKey { get; set; }

        public OutputMode OutputMode { get; set; } = OutputMode.MixBoth;
    }
}
