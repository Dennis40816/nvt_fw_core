// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Security;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Verifies bounded immutable Bootstrap identity propagation and capture-and-clear order.</summary>
public sealed class InheritedManagedBootstrapIdentityContextTests
{
    /// <summary>The exact descriptor-bound identity survives serialization without loss.</summary>
    [Fact]
    public void ExactIdentityRoundTripsThroughOneBoundedProcessContext()
    {
        var protocol = new InheritedManagedBootstrapIdentityContext(TransportFixture.Descriptor, 128);
        var start = new ProcessStartInfo { UseShellExecute = false };
        ManagedImmutableBootstrapIdentity identity = Identity(TransportFixture.Descriptor);
        protocol.Apply(start, identity);
        string serialized = Assert.IsType<string>(start.Environment[TransportFixture.Names.BootstrapIdentity]);
        var calls = new List<string>();
        ManagedImmutableBootstrapIdentity? captured = protocol.CaptureAndClear(
            key => { calls.Add(key); return serialized; },
            (key, value) => { calls.Add(key); Assert.Null(value); });
        Assert.Equal(identity, captured);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, key => Assert.Equal(TransportFixture.Names.BootstrapIdentity, key));
        Assert.InRange(serialized.Length, 1, 128);
    }

    /// <summary>A positive explicit serialization ceiling is mandatory.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void NonpositiveSerializationLimitIsRejected(int maximum)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InheritedManagedBootstrapIdentityContext(TransportFixture.Descriptor, maximum));
        Assert.Equal("maximumSerializedCharacters", exception.ParamName);
    }

    /// <summary>The frozen 128-character ceiling applies identically to writing and reading.</summary>
    [Theory]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    public void SerializationCharacterBoundaryIsExact(int characters)
    {
        // With a one-digit length and a 64-character digest the framing uses 69 characters.
        string filename = new string('b', characters - 69 - 4) + ".exe";
        ProductDescriptor descriptor = TransportFixture.CreateDescriptor(filename);
        var protocol = new InheritedManagedBootstrapIdentityContext(descriptor, 128);
        ManagedImmutableBootstrapIdentity identity = Identity(descriptor);
        var start = new ProcessStartInfo { UseShellExecute = false };
        string serialized = $"1|{filename}|1|{new string('a', 64)}";
        Assert.Equal(characters, serialized.Length);
        if (characters <= 128)
        {
            protocol.Apply(start, identity);
            Assert.Equal(serialized, start.Environment[TransportFixture.Names.BootstrapIdentity]);
        }
        else
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => protocol.Apply(start, identity));
            Assert.StartsWith("Inherited Bootstrap identity is oversized.", exception.Message, StringComparison.Ordinal);
        }
        bool cleared = false;
        ManagedImmutableBootstrapIdentity? captured = protocol.CaptureAndClear(
            _ => serialized, (_, value) => cleared = value is null);
        Assert.Equal(characters <= 128 ? identity : null, captured);
        Assert.True(cleared);
    }

    /// <summary>A one-character positive ceiling is usable and rejects a larger identity.</summary>
    [Fact]
    public void SmallestPositiveSerializationLimitIsAccepted()
    {
        var protocol = new InheritedManagedBootstrapIdentityContext(TransportFixture.Descriptor, 1);
        var start = new ProcessStartInfo();
        _ = Assert.Throws<ArgumentException>(() => protocol.Apply(start, Identity(TransportFixture.Descriptor)));
        Assert.Null(protocol.CaptureAndClear(_ => "x", static (_, _) => { }));
    }

    /// <summary>Missing or malformed context is always cleared before rejection.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2|Fixture.Bootstrap.exe|1|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1|Other.exe|1|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1|Fixture.Bootstrap.exe|0|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1|Fixture.Bootstrap.exe|-1|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1|Fixture.Bootstrap.exe|+1|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1|Fixture.Bootstrap.exe|1|AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("1|Fixture.Bootstrap.exe|1|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1|Fixture.Bootstrap.exe|1|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1|Fixture.Bootstrap.exe|200000001|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1|Fixture.Bootstrap.exe|1|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|extra")]
    public void MissingOrMalformedContextClearsAndReturnsNoAuthority(string? serialized)
    {
        var protocol = new InheritedManagedBootstrapIdentityContext(TransportFixture.Descriptor, 128);
        bool cleared = false;
        Assert.Null(protocol.CaptureAndClear(_ => serialized, (_, value) => cleared = value is null));
        Assert.True(cleared);
    }

    /// <summary>Executable length remains exactly the identity contract's 200,000,000-byte ceiling.</summary>
    [Theory]
    [InlineData(199999999)]
    [InlineData(200000000)]
    [InlineData(200000001)]
    public void ExecutableLengthBoundaryIsExact(long length)
    {
        var protocol = new InheritedManagedBootstrapIdentityContext(TransportFixture.Descriptor, 128);
        string serialized = string.Create(CultureInfo.InvariantCulture,
            $"1|Fixture.Bootstrap.exe|{length}|{new string('a', 64)}");
        ManagedImmutableBootstrapIdentity? identity = protocol.CaptureAndClear(_ => serialized, static (_, _) => { });
        Assert.Equal(length <= 200_000_000 ? length : null, identity?.Length);
    }

    /// <summary>Read and clear failures are fail-closed, and a failed read cannot be followed by clearing.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnvironmentFailuresReturnNoAuthority(bool failClear)
    {
        var protocol = new InheritedManagedBootstrapIdentityContext(TransportFixture.Descriptor, 128);
        var calls = new List<string>();
        Assert.Null(protocol.CaptureAndClear(
            _ =>
            {
                calls.Add("read");
                return failClear ? "x" : throw new SecurityException("Injected read failure.");
            },
            (_, _) => { calls.Add("clear"); throw new ArgumentException("Injected clear failure."); }));
        Assert.Equal(failClear ? 2 : 1, calls.Count);
        Assert.Equal("read", calls[0]);
    }

    /// <summary>A descriptor mismatch fails before writing any inherited authority.</summary>
    [Fact]
    public void DifferentBootstrapFilenameIsRejected()
    {
        var protocol = new InheritedManagedBootstrapIdentityContext(TransportFixture.Descriptor, 128);
        ProductDescriptor other = TransportFixture.CreateDescriptor("Other.exe");
        var start = new ProcessStartInfo();
        _ = Assert.Throws<ArgumentException>(() => protocol.Apply(start, Identity(other)));
        Assert.False(start.Environment.ContainsKey(TransportFixture.Names.BootstrapIdentity));
    }

    private static ManagedImmutableBootstrapIdentity Identity(ProductDescriptor descriptor) =>
        ManagedImmutableBootstrapIdentity.Create(descriptor, descriptor.BootstrapExecutableFileName, 1, new string('a', 64));
}
