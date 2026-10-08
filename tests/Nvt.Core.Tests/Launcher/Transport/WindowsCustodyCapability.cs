// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

internal static class WindowsCustodyCapability
{
    internal static void RequireFile(string directory)
    {
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "custody-capability.lock");
        File.WriteAllText(path, string.Empty);
        WindowsStableCustodyResult result = WindowsStablePathCustody.TryAcquireFile(path,
            cancellationToken: TestContext.Current.CancellationToken);
        using WindowsStablePathCustody? custody = result.Custody;
        if (result.Issue is WindowsStableCustodyIssue.AccessDenied or WindowsStableCustodyIssue.Unavailable)
        {
            Assert.Skip($"Windows no-follow file custody with retained ancestor handles is unavailable ({result.Issue}).");
        }
        Assert.True(result.IsAcquired, $"The custody capability fixture failed: {result.Issue}.");
        Assert.True(custody!.RevalidateClosedTree());
    }
}
