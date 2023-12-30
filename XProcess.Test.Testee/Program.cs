using System.Diagnostics;
using CommandLineSwitchParser;
using Toolbelt.Diagnostics.Test;

var options = CommandLineSwitch.Parse<CommandLineOptions>(ref args);

var currentWriter = options.OutputMode == OutputMode.StdErr ? Console.Error : Console.Out;

if (options.SpawnChildProcess)
{
    var childProcess = Process.Start(new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = "testee.dll -n", // Never exit until enter any key
        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
    });
    Console.WriteLine($"Child Proecess Id: {childProcess?.Id}");
}

if (options.InfiniteCounter)
{
    InfiniteCounter();
}
else
{
    HelloWorld();
}

if (options.NeverExitUntilEnterAnyKey)
{
    Console.WriteLine("Press any keys to exit.");
    await WaitForEnterAnyKeyAsync();
}

return options.ExitCode;

void WriteLine(string text)
{
    currentWriter.WriteLine(text);
    currentWriter.Flush();
    if (options.OutputMode == OutputMode.MixBoth)
    {
        currentWriter = currentWriter == Console.Out ? Console.Error : Console.Out;
    }
}

void HelloWorld()
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

void InfiniteCounter()
{
    for (var c = 0; c < 10000; c++)
    {
        WriteLine(c.ToString());
        Thread.Sleep(100);
    }
}

static Task WaitForEnterAnyKeyAsync()
{
    var tcs = new TaskCompletionSource();
    Task.Run(() =>
    {
        Console.ReadKey(intercept: true);
        tcs.SetResult();
    });
    return tcs.Task;
}
