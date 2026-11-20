using NUnit.Framework;

namespace Toolbelt.Diagnostics.Test;

[SetUpFixture]
public class WarmUp
{
    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        // On Linux, the first redirected output read in an NUnit + MTP host loses a whole stream (dotnet/runtime#131915), so let a throwaway process take it.
        using var process = await XProcess.Start("dotnet", "testee.dll", AppDomain.CurrentDomain.BaseDirectory).WaitForExitAsync();
    }
}
