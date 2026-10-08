// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;

namespace Nvt.Core.Launcher.Transport;

internal static class BoundedUtf8LineReader
{
    internal static async Task<string?> ReadAsync(
        Stream stream,
        int maximumCharacters,
        int bufferSize,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize,
            leaveOpen: true);
        var result = new StringBuilder();
        char[] character = new char[1];
        while (result.Length <= maximumCharacters)
        {
            int read = await reader.ReadAsync(character, cancellationToken).ConfigureAwait(false);
            if (read == 0 || character[0] == '\n')
            {
                return result.ToString().TrimEnd('\r');
            }
            _ = result.Append(character[0]);
        }
        return null;
    }
}
