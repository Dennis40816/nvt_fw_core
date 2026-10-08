// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Characterizes exact launcher identity admission and the frozen READY field grammar.</summary>
public sealed class LauncherReadyProtocolTests
{
    /// <summary>READY binds the owning admission digest, both manifest and executable digests, and the app admission.</summary>
    [Fact]
    public void ExactIdentityRoundTrips()
    {
        ManagedLauncherIdentity launcher = TransportFixture.Launcher();
        string digest = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(launcher.OwnerAdmissionIdentity))).ToLowerInvariant();
        string expected = $"READY-LAUNCHER:1:1.2.3:{digest}:{new string('a', 64)}:{new string('b', 64)}";
        var admission = new ManagedVersionAdmission(TransportFixture.Version, "fixture|界", new string('c', 64));
        Assert.Equal(expected, LauncherReadyProtocol.CreateExpectedPrefix(launcher));
        Assert.True(LauncherReadyProtocol.IsExpectedPrefix(expected));
        string text = LauncherReadyProtocol.Create(launcher, admission);
        Assert.Equal(expected + ":1.2.3:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("fixture|界")) +
            ":" + new string('c', 64), text);
        Assert.True(LauncherReadyProtocol.TryParse(text, expected, out ManagedVersionAdmission? actual));
        Assert.Equal(admission, actual);
    }

    /// <summary>Each exact prefix field participates in identity admission.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void DifferentExpectedIdentityIsRejected(int field)
    {
        string expected = LauncherReadyProtocol.CreateExpectedPrefix(TransportFixture.Launcher());
        string[] fields = expected.Split(':');
        fields[field] += "0";
        string text = string.Join(':', fields) + ":1.2.3:YQ==:" + new string('c', 64);
        Assert.False(LauncherReadyProtocol.TryParse(text, expected, out ManagedVersionAdmission? admission));
        Assert.Null(admission);
    }

    /// <summary>Admission text is nonblank and at most 2,048 decoded characters.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2047, true)]
    [InlineData(2048, true)]
    [InlineData(2049, false)]
    public void AdmissionCharacterBoundaryIsExact(int characters, bool accepted)
    {
        string expected = LauncherReadyProtocol.CreateExpectedPrefix(TransportFixture.Launcher());
        string identity = new('界', characters);
        string text = expected + ":1.2.3:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(identity)) +
            ":" + new string('c', 64);
        Assert.Equal(accepted, LauncherReadyProtocol.TryParse(text, expected, out ManagedVersionAdmission? admission));
        Assert.Equal(accepted ? identity : null, admission?.AdmissionIdentity);
    }

    /// <summary>Digest lengths are exactly 64 and lowercase.</summary>
    [Theory]
    [InlineData(63, 'a', false)]
    [InlineData(64, 'a', true)]
    [InlineData(65, 'a', false)]
    [InlineData(64, 'A', false)]
    [InlineData(64, 'g', false)]
    public void ManifestDigestBoundaryIsExact(int length, char character, bool accepted)
    {
        string expected = LauncherReadyProtocol.CreateExpectedPrefix(TransportFixture.Launcher());
        Assert.Equal(accepted, LauncherReadyProtocol.TryParse(
            expected + ":1.2.3:YQ==:" + new string(character, length), expected, out _));
    }

    /// <summary>Wire parsing retains its original field-count, version, Base64, and blank-identity predicates.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(":1.2.3:YQ==")]
    [InlineData(":1.2.3:YQ==:hash")]
    [InlineData(":01.2.3:YQ==:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(":1.2.3:!:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(":1.2.3:IA==:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(":1.2.3:YQ==:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:extra")]
    public void InvalidReadyFieldsFailClosed(string? suffix)
    {
        string expected = LauncherReadyProtocol.CreateExpectedPrefix(TransportFixture.Launcher());
        string? text = suffix is null ? null : expected + suffix;
        Assert.False(LauncherReadyProtocol.TryParse(text, expected, out ManagedVersionAdmission? admission));
        Assert.Null(admission);
    }

    /// <summary>The Base64 identity decoder retains the source replacement fallback independently of strict pipe decoding.</summary>
    [Fact]
    public void Base64IdentityRetainsReplacementFallback()
    {
        string expected = LauncherReadyProtocol.CreateExpectedPrefix(TransportFixture.Launcher());
        Assert.True(LauncherReadyProtocol.TryParse(
            expected + ":1.2.3:/w==:" + new string('c', 64), expected, out ManagedVersionAdmission? admission));
        Assert.Equal("\uFFFD", admission!.AdmissionIdentity);
    }

    /// <summary>Expected prefixes accept only protocol one and six exact fields.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("READY-LAUNCHER:0:1.2.3")]
    [InlineData("ready-launcher:1:1.2.3")]
    public void InvalidExpectedPrefixIsRejected(string? prefix)
    {
        Assert.False(LauncherReadyProtocol.IsExpectedPrefix(prefix));
    }

    /// <summary>Every fixed digest in a prefix has the same exact length and lowercase grammar.</summary>
    [Theory]
    [InlineData(3, 63, false)]
    [InlineData(3, 64, true)]
    [InlineData(3, 65, false)]
    [InlineData(4, 63, false)]
    [InlineData(4, 64, true)]
    [InlineData(4, 65, false)]
    [InlineData(5, 63, false)]
    [InlineData(5, 64, true)]
    [InlineData(5, 65, false)]
    public void ExpectedPrefixDigestBoundariesAreExact(int field, int length, bool accepted)
    {
        string[] fields = LauncherReadyProtocol.CreateExpectedPrefix(TransportFixture.Launcher()).Split(':');
        fields[field] = new string('a', length);
        Assert.Equal(accepted, LauncherReadyProtocol.IsExpectedPrefix(string.Join(':', fields)));
    }

    /// <summary>Prefix construction is invariant under the caller's current culture.</summary>
    [Fact]
    public void PrefixUsesFrozenFormatting()
    {
        using var culture = new CultureScope();
        string prefix = LauncherReadyProtocol.CreateExpectedPrefix(TransportFixture.Launcher());
        Assert.StartsWith("READY-LAUNCHER:1:1.2.3:", prefix, StringComparison.Ordinal);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
        internal CultureScope() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
