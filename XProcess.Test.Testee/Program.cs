using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CommandLineSwitchParser;

namespace Toolbelt.Diagnostics.Test
{
    class Program
    {
        private static TextWriter CurrentWriter { get; set; } = Console.Out;

        private static CommandLineOptions Options { get; set; } = new CommandLineOptions();

        private static void WriteLine(string text)
        {
            CurrentWriter.WriteLine(text);
            CurrentWriter.Flush();
            if (Options.OutputMode == OutputMode.MixBoth)
            {
                CurrentWriter = CurrentWriter == Console.Out ? Console.Error : Console.Out;
            }
        }

        static int Main(string[] args)
        {
            Options = CommandLineSwitch.Parse<CommandLineOptions>(ref args);

            CurrentWriter = Options.OutputMode == OutputMode.StdErr ? Console.Error : Console.Out;

            if (Options.SpawnChildProcess)
            {
                var childProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "testee.dll -n", // Never exit until enter any key
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                });
                Console.WriteLine($"Child Proecess Id: {childProcess?.Id}");
            }

            if (Options.InfiniteCounter)
            {
                InfiniteCounter();
            }
            else
            {
                HelloWorld();
            }

            if (Options.NeverExitUntilEnterAnyKey)
            {
                Console.WriteLine("Press any keys to exit.");
                Console.ReadKey(intercept: true);
            }

            return Options.ExitCode;
        }

        private static void HelloWorld()
        {
            WriteLine("Hello,");
            Thread.Sleep(100);

            WriteLine("everyone.");
            Thread.Sleep(100);

            WriteLine("Nice to");
            Thread.Sleep(100);

            WriteLine("meet you.");
            Thread.Sleep(100);
        }

        private static void InfiniteCounter()
        {
            for (var c = 0; c < 10000; c++)
            {
                WriteLine(c.ToString());
                Thread.Sleep(100);
            }
        }
    }
}
