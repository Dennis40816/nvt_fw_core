// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Security;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Transport;

/// <summary>Carries one exact Root Bootstrap identity through the managed process chain.</summary>
internal sealed class InheritedManagedBootstrapIdentityContext
{
    private readonly IdentityProtocol _protocol;

    internal InheritedManagedBootstrapIdentityContext(ProductDescriptor descriptor, int maximumSerializedCharacters)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSerializedCharacters);
        _protocol = new(descriptor, maximumSerializedCharacters);
    }

    internal int MaximumSerializedCharacters => _protocol.MaximumSerializedCharacters;

    internal void Apply(ProcessStartInfo startInfo, ManagedImmutableBootstrapIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(identity);
        if (!string.Equals(identity.FileName, _protocol.Descriptor.BootstrapExecutableFileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Inherited identity must name the Root Bootstrap.", nameof(identity));
        }
        string serialized = string.Create(
            CultureInfo.InvariantCulture,
            $"1|{identity.FileName}|{identity.Length}|{identity.Sha256}");
        if (serialized.Length > _protocol.MaximumSerializedCharacters)
        {
            throw new ArgumentException("Inherited Bootstrap identity is oversized.", nameof(identity));
        }
        startInfo.Environment[_protocol.Descriptor.ProtocolNames.BootstrapIdentity] = serialized;
    }

    internal ManagedImmutableBootstrapIdentity? CaptureAndClear() =>
        CaptureAndClear(Environment.GetEnvironmentVariable, Environment.SetEnvironmentVariable);

    internal ManagedImmutableBootstrapIdentity? CaptureAndClear(
        Func<string, string?> read,
        Action<string, string?> clear)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(clear);
        string? serialized;
        try
        {
            serialized = read(_protocol.Descriptor.ProtocolNames.BootstrapIdentity);
            clear(_protocol.Descriptor.ProtocolNames.BootstrapIdentity, null);
        }
        catch (Exception exception) when (exception is ArgumentException or SecurityException)
        {
            return null;
        }
        if (serialized is null || serialized.Length == 0 ||
            serialized.Length > _protocol.MaximumSerializedCharacters)
        {
            return null;
        }
        string[] parts = serialized.Split('|');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], "1", StringComparison.Ordinal) ||
            !string.Equals(parts[1], _protocol.Descriptor.BootstrapExecutableFileName, StringComparison.Ordinal) ||
            !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long length))
        {
            return null;
        }
        try
        {
            return ManagedImmutableBootstrapIdentity.Create(_protocol.Descriptor, parts[1], length, parts[3]);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private sealed record IdentityProtocol(ProductDescriptor Descriptor, int MaximumSerializedCharacters);
}
