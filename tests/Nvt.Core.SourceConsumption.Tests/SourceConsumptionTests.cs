// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using System.Text.Json;
using Nvt.Core.Threading;
using Xunit;

namespace Nvt.Core.SourceConsumption.Tests;

/// <summary>Verifies standalone internal compilation and the canonical source manifest.</summary>
public sealed class SourceConsumptionTests
{
    /// <summary>The symbol compiles the linked type as internal without a Core runtime dependency.</summary>
    [Fact]
    public async Task LinkedRunnerIsInternalAndStandalone()
    {
        var runnerType = typeof(UiEventRunner);
        Assert.True(runnerType.IsNotPublic);
        Assert.True(runnerType.IsSealed);
        Assert.Equal("Nvt.Core.Threading.UiEventRunner", runnerType.FullName);
        Assert.Same(typeof(SourceConsumptionTests).Assembly, runnerType.Assembly);
        Assert.DoesNotContain(runnerType.Assembly.GetReferencedAssemblies(), name => name.Name == "Nvt.Core");
        var reports = new List<Exception>();
        var expected = new InvalidOperationException("source operation failed");
        var runner = new UiEventRunner((_, error) => reports.Add(error), (_, _) => { });

        await runner.RunAsync("Source", _ => throw expected, TestContext.Current.CancellationToken);

        Assert.Same(expected, Assert.Single(reports));
    }

    /// <summary>The manifest describes the exact LF source bytes linked into this project.</summary>
    [Fact]
    public void ManifestMatchesCanonicalSource()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "canonical");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        var entry = Assert.Single(root.GetProperty("files").EnumerateArray());
        Assert.Equal("src/Nvt.Core/Threading/UiEventRunner.cs", entry.GetProperty("path").GetString());
        Assert.Equal("NVT_CORE_SOURCE_CONSUMPTION", entry.GetProperty("compileSymbol").GetString());
        var bytes = File.ReadAllBytes(Path.Combine(directory, "UiEventRunner.cs"));

        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal(bytes.Length, entry.GetProperty("byteLength").GetInt32());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), entry.GetProperty("sha256").GetString());
    }
}
