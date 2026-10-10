// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;

namespace Nvt.Core.TestSupport;

/// <summary>Reads and writes synthetic fixture files for a test, through streams.</summary>
/// <remarks>
/// The <c>File.ReadAll*</c> and <c>File.WriteAll*</c> shortcuts are banned in every project, because production code
/// must use the protected writer. A test that needs a plain fixture file uses these helpers instead of repeating the
/// stream code. Use them only on paths inside a <see cref="TestWorkspace"/>.
/// </remarks>
public static class TestFiles
{
    /// <summary>Creates or replaces a file with the bytes.</summary>
    /// <param name="path">The file path. The parent folder must exist.</param>
    /// <param name="bytes">The content.</param>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }

    /// <summary>Reads every line of a UTF-8 text file.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The lines without their line terminators.</returns>
    public static async Task<string[]> ReadLinesAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var lines = new List<string>();
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            lines.Add(line);
        }
        return [.. lines];
    }
}
