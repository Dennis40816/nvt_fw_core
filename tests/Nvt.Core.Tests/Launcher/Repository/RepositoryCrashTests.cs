// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Tests.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Repository;

/// <summary>Stops a real repository process at deterministic staging and promotion gates.</summary>
public sealed class RepositoryCrashTests
{
    /// <summary>A hard stop preserves the exact physical prefix without inventing committed admission authority.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HardStopAfterVerifiedStagingOrPromotionPreservesObservedPrefix(bool afterPromotion)
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture();
        string marker = fixture.PathFor("installation-gate.txt");
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(ChildScript)));
        start.Environment["NVT_REPOSITORY_PROBE_CORE"] = typeof(FileSystemManagedVersionRepository).Assembly.Location;
        start.Environment["NVT_REPOSITORY_PROBE_TESTS"] = Assembly.GetExecutingAssembly().Location;
        start.Environment["NVT_REPOSITORY_PROBE_ROOT"] = Path.GetDirectoryName(fixture.ManagedRoot)!;
        start.Environment["NVT_REPOSITORY_PROBE_MARKER"] = marker;
        start.Environment["NVT_REPOSITORY_PROBE_STAGE"] = afterPromotion ? "1" : "0";
        Process? child;
        try
        {
            child = Process.Start(start);
        }
        catch (Win32Exception)
        {
            Assert.Skip("PowerShell 7 is required to load the managed repository crash probe.");
            throw;
        }
        using Process process = Assert.IsType<Process>(child);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            var elapsed = Stopwatch.StartNew();
            while (!File.Exists(marker) && !process.HasExited && elapsed.Elapsed < TimeSpan.FromSeconds(60))
            {
                await Task.Delay(25, TestContext.Current.CancellationToken);
            }
            if (process.HasExited)
            {
                Assert.Fail("Repository probe exited before its physical gate: " + await stdout + await stderr);
            }
            Assert.True(File.Exists(marker), "Repository probe did not reach its physical gate.");
            string observed = await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken);
            Assert.Equal(afterPromotion, Directory.Exists(fixture.VersionRoot));
            Assert.True(Directory.Exists(observed));
            Assert.Equal(fixture.Package.Files.Count + 3,
                Directory.EnumerateFiles(observed, "*", SearchOption.AllDirectories).Count());
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            await Task.WhenAll(stdout, stderr);
            var inventory = await fixture.Repository.InventoryAsync(fixture.ManagedRoot, [], null, null, null,
                TestContext.Current.CancellationToken);
            Assert.True(inventory.IsSuccess);
            if (afterPromotion)
            {
                var row = Assert.Single(inventory.Inventory!.Versions);
                Assert.Equal(ManagedVersionAdmissionState.Unadmitted, row.AdmissionState);
                Assert.Equal(ManagedVersionIntegrity.Healthy, row.Integrity);
            }
            else
            {
                Assert.Empty(inventory.Inventory!.Versions);
                Assert.True(Directory.Exists(observed));
            }
            var restarted = await fixture.InstallAsync();
            Assert.True(restarted.IsSuccess, restarted.Issue.ToString());
            Assert.Equal(afterPromotion, restarted.WasAlreadyInstalled);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    private const string ChildScript = """
        $ErrorActionPreference = 'Stop'
        [void][System.Reflection.Assembly]::LoadFrom($env:NVT_REPOSITORY_PROBE_CORE)
        $assembly = [System.Reflection.Assembly]::LoadFrom($env:NVT_REPOSITORY_PROBE_TESTS)
        $probe = $assembly.GetType('Nvt.Core.Tests.Launcher.Repository.InstallationCrashProbe', $true)
        $method = $probe.GetMethod('Run', [System.Reflection.BindingFlags]'Static, NonPublic')
        [void]$method.Invoke($null, [object[]]@($env:NVT_REPOSITORY_PROBE_ROOT, $env:NVT_REPOSITORY_PROBE_MARKER, ($env:NVT_REPOSITORY_PROBE_STAGE -eq '1')))
        """;
}

internal static class InstallationCrashProbe
{
    internal static void Run(string fixtureRoot, string marker, bool afterPromotion)
    {
        var package = PackageFixture.Create(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ContractFixture.ApplicationPath] = RepositoryFixture.PortableExecutable(),
            ["reference/nested/data.bin"] = [1, 2, 3, 4],
            ["README.txt"] = "readme"u8.ToArray(),
        });
        // The parent wrote the source package; ZIP entry times differ between runs, so hash the parent's bytes.
        string sourcePackage = Path.Combine(fixtureRoot, "source", Assert.IsType<string>(package.Candidate.PackagePath.Value));
        var candidate = package.CandidateFor(File.ReadAllBytes(sourcePackage));
        void Hold(string path)
        {
            // Publish the complete marker atomically so the parent never reads a partial file.
            string pending = marker + ".pending";
            File.WriteAllText(pending, path);
            File.Move(pending, marker);
            using var gate = new ManualResetEvent(false);
            gate.WaitOne();
        }
        var operations = new RepositoryOperations
        {
            BeforePackagePromotion = afterPromotion ? null : Hold,
            AfterPromotion = afterPromotion ? Hold : null,
        };
        var repository = new FileSystemManagedVersionRepository(ContractFixture.Descriptor, package.Policy,
            PackageFixture.FrozenLimits, new AdmissionCodec(), operations);
        var result = repository.InstallAsync(Path.Combine(fixtureRoot, "managed"), Path.Combine(fixtureRoot, "source"),
            candidate, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        throw new InvalidOperationException("Repository probe completed before its gate: " + result.Issue);
    }
}
