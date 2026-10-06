// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

/// <summary>Verifies product-independent named-app starts and installed facts for shortcut callers.</summary>
public sealed class InstalledApplicationCoordinatorTests
{
    /// <summary>Two distinct synthetic apps keep their identity, executable, source and presentation while sharing supervision.</summary>
    [Theory]
    [InlineData("Synthetic Editor", "SyntheticEditor.exe")]
    [InlineData("Synthetic Inspector", "bin/SyntheticInspector.exe")]
    public async Task NamedInstalledAppsExposeFactsAndUseContainedReadyPath(string productId, string executable)
    {
        ProductDescriptor product = Product(productId, executable);
        string root = Path.Combine(Path.GetTempPath(), "synthetic-app-" + Guid.NewGuid().ToString("N"));
        string updateSource = Path.Combine(Path.GetTempPath(), "synthetic-source-" + Guid.NewGuid().ToString("N"));
        ManagedAppVersion version = ManagedAppVersion.Parse("2.3.4");
        var admission = new ManagedVersionAdmission(version, $"{productId}|{version}", new string('a', 64));
        VersionManagerState initial = VersionManagerState.Create(updateSource, version, version, [admission], null, null, false,
            managedRootIdentity: root);
        using var store = new CoordinationStateStore(initial);
        var repository = new TraceApplicationRepository(store, product);
        var process = new TraceApplicationProcess(store);
        var presentation = new PresentationPort(new(productId + " Display", Path.Combine(root, "StableEntry.exe"), Path.Combine(root, "brand.ico")));
        var coordinator = new InstalledApplicationCoordinator(product, root, store, repository, process, presentation);

        InstalledApplicationInfo info = Assert.IsType<InstalledApplicationInfo>(
            await coordinator.ReadInstalledApplicationAsync(TestContext.Current.CancellationToken));
        Assert.Equal(productId, info.ProductId);
        Assert.Equal(version, info.InstalledVersion);
        Assert.Equal(Path.Combine(root, version.ToString(), executable), info.ExecutablePath);
        Assert.Equal(presentation.Value.DisplayName, info.DisplayName);
        Assert.Equal(presentation.Value.LaunchEntryPoint, info.LaunchEntryPoint);
        Assert.Equal(presentation.Value.IconPath, info.IconPath);
        Assert.Equal(product, presentation.Product);
        Assert.Equal(root, presentation.Root);
        Assert.Equal(admission, presentation.Admission);
        Assert.Equal(0, store.WriteLeaseCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Empty(process.Starts);
        Assert.Equal(info, await coordinator.ReadInstalledApplicationAsync(version, TestContext.Current.CancellationToken));

        ManagedLauncherResult started = await coordinator.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ManagedLauncherOutcome.Ready, started.Outcome);
        Assert.Equal(version, Assert.Single(process.Starts));
        Assert.Equal(CoordinationStateCodec.Encode(initial), CoordinationStateCodec.Encode(store.State));
        Assert.All(repository.Leases, lease => Assert.True(lease.Disposed));
        Assert.False(Directory.Exists(root));
    }

    /// <summary>Unadmitted versions and unavailable inventory expose no shortcut facts or inferred fallback.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableInstalledFactsFailClosedWithoutStarting(bool inventoryUnavailable)
    {
        ProductDescriptor product = Product("Synthetic Tool", "SyntheticTool.exe");
        string root = Path.GetFullPath("synthetic-installed-root");
        ManagedAppVersion version = ManagedAppVersion.Parse("1.0.0");
        var admission = new ManagedVersionAdmission(version, "synthetic", new string('a', 64));
        using var store = new CoordinationStateStore(VersionManagerState.Create(null, version, version, [admission], null, null, false,
            managedRootIdentity: root));
        var repository = new TraceApplicationRepository(store, product) { InventoryUnavailable = inventoryUnavailable };
        var process = new TraceApplicationProcess(store);
        var presentation = new PresentationPort(new("Synthetic", "entry", "icon"));
        var coordinator = new InstalledApplicationCoordinator(product, root, store, repository, process, presentation);
        Assert.Null(inventoryUnavailable
            ? await coordinator.ReadInstalledApplicationAsync(TestContext.Current.CancellationToken)
            : await coordinator.ReadInstalledApplicationAsync(ManagedAppVersion.Parse("9.9.9"), TestContext.Current.CancellationToken));
        Assert.Empty(repository.Leases);
        Assert.Empty(process.Starts);
        Assert.Equal(0, presentation.Calls);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, store.WriteLeaseCount);
    }

    /// <summary>Product presentation is mandatory and its errors propagate while executable custody is released.</summary>
    [Fact]
    public async Task PresentationPolicyFailurePropagatesWithoutFallbackOrLeakedCustody()
    {
        ProductDescriptor product = Product("Synthetic Tool", "SyntheticTool.exe");
        string root = Path.GetFullPath("synthetic-installed-root");
        ManagedAppVersion version = ManagedAppVersion.Parse("1.0.0");
        var admission = new ManagedVersionAdmission(version, "synthetic", new string('a', 64));
        using var store = new CoordinationStateStore(VersionManagerState.Create(null, version, version, [admission], null, null, false,
            managedRootIdentity: root));
        var repository = new TraceApplicationRepository(store, product);
        var process = new TraceApplicationProcess(store);
        Assert.Throws<ArgumentNullException>(() => new InstalledApplicationCoordinator(product, root, store, repository, process, null!));
        var coordinator = new InstalledApplicationCoordinator(product, root, store, repository, process, new RejectPresentation());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ReadInstalledApplicationAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.True(Assert.Single(repository.Leases).Disposed);
        Assert.Empty(process.Starts);
        Assert.Equal(0, store.WriteLeaseCount);
    }

    private static ProductDescriptor Product(string id, string executable) => new(id, "win-x64", "synthetic.registry", executable,
        "launcher/SyntheticLauncher.exe", "StableEntry.exe", version => $"Synthetic-{version}", CoordinationFixture.Product.ProtocolNames);
    private sealed class PresentationPort(InstalledApplicationPresentation value) : IInstalledApplicationPresentation
    {
        internal InstalledApplicationPresentation Value { get; } = value;
        internal int Calls { get; private set; }
        internal ProductDescriptor? Product { get; private set; }
        internal string? Root { get; private set; }
        internal ManagedVersionAdmission? Admission { get; private set; }
        public ValueTask<InstalledApplicationPresentation> ReadAsync(ProductDescriptor product, string installRoot,
            ManagedVersionAdmission admission, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++; Product = product; Root = installRoot; Admission = admission;
            return ValueTask.FromResult(Value);
        }
    }
    private sealed class RejectPresentation : IInstalledApplicationPresentation
    {
        public ValueTask<InstalledApplicationPresentation> ReadAsync(ProductDescriptor product, string installRoot,
            ManagedVersionAdmission admission, CancellationToken cancellationToken) => throw new InvalidOperationException("Synthetic policy rejected metadata.");
    }
}
