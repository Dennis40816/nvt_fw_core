// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.

using Nvt.Core.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Contracts;

public sealed class ProductDescriptorTests
{
    [Fact]
    public void DescriptorPreservesEveryExplicitValueAndUsesTheExactVersion()
    {
        ProductDescriptor descriptor = ContractFixture.Descriptor;
        Assert.Equal(ContractFixture.ProductId, descriptor.ProductId);
        Assert.Equal(ContractFixture.RuntimeIdentifier, descriptor.RuntimeIdentifier);
        Assert.Equal("fixture-registry", descriptor.RegistryId);
        Assert.Equal(ContractFixture.ApplicationPath, descriptor.ApplicationExecutableRelativePath);
        Assert.Equal(ContractFixture.LauncherPath, descriptor.LauncherExecutableRelativePath);
        Assert.Equal(ContractFixture.BootstrapFileName, descriptor.BootstrapExecutableFileName);
        Assert.Same(ContractFixture.ProtocolNames, descriptor.ProtocolNames);
        Assert.Equal("FixtureProduct-v1.0.1-test-runtime", descriptor.GetArchiveRootName(ContractFixture.App101));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void DescriptorRejectsBlankProductRuntimeAndRegistry(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => ContractFixture.CreateDescriptor(productId: value!));
        Assert.ThrowsAny<ArgumentException>(() => ContractFixture.CreateDescriptor(runtimeIdentifier: value!));
        Assert.ThrowsAny<ArgumentException>(() => ContractFixture.CreateDescriptor(registryId: value!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/absolute.exe")]
    [InlineData("C:/absolute.exe")]
    [InlineData("../escape.exe")]
    [InlineData("dir/../escape.exe")]
    [InlineData("dir/./file.exe")]
    [InlineData("dir//file.exe")]
    [InlineData("dir\\file.exe")]
    [InlineData("dir/")]
    [InlineData("dir /file.exe")]
    [InlineData("dir./file.exe")]
    [InlineData("file.exe ")]
    [InlineData("file.exe.")]
    [InlineData("file.exe:stream")]
    [InlineData("bad\u0001.exe")]
    [InlineData("bad?.exe")]
    [InlineData("bad*.exe")]
    [InlineData("bad|.exe")]
    [InlineData("bad<.exe")]
    [InlineData("bad>.exe")]
    [InlineData("bad\".exe")]
    [InlineData("CON.exe")]
    [InlineData("dir/nUl.exe")]
    [InlineData("LPT9.exe")]
    [InlineData("COM1.bin/file.exe")]
    [InlineData("CONIN$/file.exe")]
    public void DescriptorRejectsUnsafeExecutablePaths(string? value)
    {
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateDescriptor(applicationPath: value!));
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateDescriptor(launcherPath: value!));
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateDescriptor(bootstrapFileName: value!));
    }

    [Theory]
    [InlineData('\u007f')]
    [InlineData('\u0085')]
    [InlineData('\u009f')]
    public void DescriptorPreservesFrozenDelAndC1Characters(char character)
    {
        string applicationPath = $"app/Fixture{character}.exe";
        string launcherPath = $"launcher/Fixture{character}.Launcher.exe";
        string bootstrapFileName = $"Fixture{character}.Bootstrap.exe";
        string archiveRootName = $"Fixture{character}-v1.0.0";
        ProductDescriptor descriptor = ContractFixture.CreateDescriptor(
            applicationPath: applicationPath, launcherPath: launcherPath,
            bootstrapFileName: bootstrapFileName, archiveRootName: _ => archiveRootName);

        Assert.Equal(applicationPath, descriptor.ApplicationExecutableRelativePath);
        Assert.Equal(launcherPath, descriptor.LauncherExecutableRelativePath);
        Assert.Equal(bootstrapFileName, descriptor.BootstrapExecutableFileName);
        Assert.Equal(archiveRootName, descriptor.GetArchiveRootName(ContractFixture.App100));
    }

    [Fact]
    public void DescriptorRejectsEveryFrozenC0Character()
    {
        for (char character = '\u0000'; character <= '\u001f'; character++)
        {
            Assert.Throws<ArgumentException>(() => ContractFixture.CreateDescriptor(
                applicationPath: $"app/Fixture{character}.exe"));
            Assert.Throws<ArgumentException>(() => ContractFixture.CreateDescriptor(
                launcherPath: $"launcher/Fixture{character}.Launcher.exe"));
            Assert.Throws<ArgumentException>(() => ContractFixture.CreateDescriptor(
                bootstrapFileName: $"Fixture{character}.Bootstrap.exe"));
            ProductDescriptor descriptor = ContractFixture.CreateDescriptor(
                archiveRootName: _ => $"Fixture{character}-v1.0.0");
            Assert.Throws<ArgumentException>(() => descriptor.GetArchiveRootName(ContractFixture.App100));
        }
    }

    [Fact]
    public void ExecutablePathsKeepTheFrozenFiveHundredTwelveCharacterLimit()
    {
        string atLimit = new string('a', 508) + ".exe";
        string overLimit = new string('a', 509) + ".exe";

        Assert.Equal(atLimit, ContractFixture.CreateDescriptor(applicationPath: atLimit).ApplicationExecutableRelativePath);
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateDescriptor(applicationPath: overLimit));
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateDescriptor(launcherPath: overLimit));
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateDescriptor(bootstrapFileName: overLimit));
        ProductDescriptor descriptor = ContractFixture.CreateDescriptor(archiveRootName: _ => overLimit);
        Assert.Throws<ArgumentException>(() => descriptor.GetArchiveRootName(ContractFixture.App100));
    }

    [Fact]
    public void BootstrapNameMustBeASingleFileWhileExecutablePathsMayBeNested()
    {
        ProductDescriptor descriptor = ContractFixture.CreateDescriptor(
            applicationPath: "app/FixtureProduct.exe", launcherPath: "bin/FixtureProduct.Launcher.exe");
        Assert.Equal("app/FixtureProduct.exe", descriptor.ApplicationExecutableRelativePath);
        Assert.Equal("bin/FixtureProduct.Launcher.exe", descriptor.LauncherExecutableRelativePath);
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateDescriptor(
            bootstrapFileName: "bin/FixtureProduct.Bootstrap.exe"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("/root")]
    [InlineData("C:root")]
    [InlineData("parent/root")]
    [InlineData("parent\\root")]
    [InlineData("root ")]
    [InlineData("root.")]
    [InlineData("NUL")]
    [InlineData("COM9.data")]
    [InlineData("root|alias")]
    [InlineData("root\n")]
    public void DescriptorRejectsBlankOrUnsafeArchiveRoots(string? value)
    {
        ProductDescriptor descriptor = ContractFixture.CreateDescriptor(archiveRootName: _ => value!);
        Assert.Throws<ArgumentException>(() => descriptor.GetArchiveRootName(ContractFixture.App100));
    }

    [Fact]
    public void ArchiveRootIsValidatedForEveryCallbackResult()
    {
        ProductDescriptor descriptor = ContractFixture.CreateDescriptor(archiveRootName: version =>
            version == ContractFixture.App100 ? "FixtureProduct-v1.0.0-test-runtime" : "../escape");
        Assert.Equal("FixtureProduct-v1.0.0-test-runtime", descriptor.GetArchiveRootName(ContractFixture.App100));
        Assert.Throws<ArgumentException>(() => descriptor.GetArchiveRootName(ContractFixture.App101));
    }

    [Fact]
    public void DescriptorRequiresArchiveRootCallbackAndProtocolNames()
    {
        Assert.Throws<ArgumentNullException>(() => new ProductDescriptor(
            ContractFixture.ProductId, ContractFixture.RuntimeIdentifier, "fixture-registry",
            ContractFixture.ApplicationPath, ContractFixture.LauncherPath, ContractFixture.BootstrapFileName,
            null!, ContractFixture.ProtocolNames));
        Assert.Throws<ArgumentNullException>(() => new ProductDescriptor(
            ContractFixture.ProductId, ContractFixture.RuntimeIdentifier, "fixture-registry",
            ContractFixture.ApplicationPath, ContractFixture.LauncherPath, ContractFixture.BootstrapFileName,
            static _ => "FixtureProduct", null!));
    }
}
