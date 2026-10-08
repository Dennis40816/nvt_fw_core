// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nvt.Core.Persistence;

/// <summary>JSON codec for local state in a host-supplied storage directory.</summary>
public static class LocalJsonDocument
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private static readonly Encoding Utf32Le = new UTF32Encoding(false, false);
    private static readonly Encoding Utf32Be = new UTF32Encoding(true, false);

    private static readonly JsonSerializerOptions Options = CreateFrozenOptions();

    /// <summary>Creates independent mutable serializer options with the local-state defaults.</summary>
    /// <returns>A fresh copy with indented output and null properties omitted.</returns>
    /// <remarks>Customizing the copy does not affect deserialization or later copies. The copy carries no type resolver until it is used.</remarks>
    public static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static JsonSerializerOptions CreateFrozenOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Combines the host-supplied local-state directory and file name.</summary>
    public static string GetPath(string localStateDirectory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localStateDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Path.Combine(localStateDirectory, fileName);
    }

    /// <summary>Reads JSON from a seekable stream, including BOM-marked UTF-16 and UTF-32.</summary>
    /// <remarks>The stream remains open. Encoding detection rewinds to an absolute position.</remarks>
    public static async ValueTask<T?> DeserializeAsync<T>(
        Stream stream,
        CancellationToken cancellationToken)
    {
        Encoding? sourceEncoding = PositionAfterLegacyByteOrderMark(stream);
        if (sourceEncoding is null)
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken)
                .ConfigureAwait(false);
        }

        using Stream utf8Stream = Encoding.CreateTranscodingStream(
            stream,
            sourceEncoding,
            Utf8,
            leaveOpen: true);
        return await JsonSerializer.DeserializeAsync<T>(utf8Stream, Options, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Encoding? PositionAfterLegacyByteOrderMark(Stream stream)
    {
        Span<byte> prefix = stackalloc byte[4];
        int read = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        bool utf32Le = read >= 4 && prefix[0] == 0xFF && prefix[1] == 0xFE && prefix[2] == 0 && prefix[3] == 0;
        bool utf32Be = read >= 4 && prefix[0] == 0 && prefix[1] == 0 && prefix[2] == 0xFE && prefix[3] == 0xFF;
        if (utf32Le || utf32Be)
        {
            stream.Position = 4;
            return utf32Le ? Utf32Le : Utf32Be;
        }

        bool utf16Le = read >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE;
        bool utf16Be = read >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF;
        stream.Position = utf16Le || utf16Be ? 2 : 0;
        return utf16Le ? Encoding.Unicode : utf16Be ? Encoding.BigEndianUnicode : null;
    }
}
