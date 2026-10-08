// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Microsoft.Win32.SafeHandles;
using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Characterizes the fixed native buffer result boundary before decoding any returned path.</summary>
public sealed class ManagedLifetimeNativePathTests
{
    /// <summary>One below the buffer length fits; an exact or larger required length cannot be accepted.</summary>
    [Theory]
    [InlineData(32767u, true)]
    [InlineData(32768u, false)]
    [InlineData(32769u, false)]
    [InlineData(0u, false)]
    [InlineData(uint.MaxValue, false)]
    public void NativeReturnedCharacterBoundaryIsExact(uint returnedLength, bool expected)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("This test requires Windows absolute-path normalization."); }
        const string prefix = @"\\?\C:\";
        string nativePath = prefix + new string('x', 32_767 - prefix.Length);
        using var handle = new SafeFileHandle(IntPtr.Zero, ownsHandle: false);
        bool result = ManagedLifetimeNativePath.IsExactLeaseHandle(handle, nativePath[4..], (_, buffer, flags) =>
        {
            Assert.Equal(32_768, buffer.Length);
            Assert.Equal(0u, flags);
            nativePath.AsSpan().CopyTo(buffer);
            return returnedLength;
        });
        Assert.Equal(expected, result);
    }

    /// <summary>The extended prefix is stripped once, and a valid-length different path remains rejected.</summary>
    [Fact]
    public void BoundedNativePathStillRequiresExactNormalizedIdentity()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("This test requires Windows absolute-path normalization."); }
        using var handle = new SafeFileHandle(IntPtr.Zero, ownsHandle: false);
        const string nativePath = @"\\?\C:\fixture\state.json.application-lifetime.v1.lock";
        uint ReadPath(SafeFileHandle _, Span<char> buffer, uint flags)
        {
            Assert.Equal(0u, flags);
            nativePath.AsSpan().CopyTo(buffer);
            return (uint)nativePath.Length;
        }
        Assert.True(ManagedLifetimeNativePath.IsExactLeaseHandle(handle, nativePath[4..].ToUpperInvariant(), ReadPath));
        Assert.False(ManagedLifetimeNativePath.IsExactLeaseHandle(handle, @"C:\fixture\other.lock", ReadPath));
    }
}
