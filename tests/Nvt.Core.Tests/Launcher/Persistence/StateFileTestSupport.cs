// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Buffers.Binary;
using System.Text;

namespace Nvt.Core.Tests.Launcher.Persistence;

internal sealed class StateFileWorkspace : IDisposable
{
    internal string Root { get; } = Directory.CreateTempSubdirectory("core-state-").FullName;
    internal string PathFor(string name = "state.bin") => Path.Combine(Root, name);

    public void Dispose()
    {
        string resolved = Path.GetFullPath(Root);
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(temp, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The state test folder must remain inside the temporary directory.");
        }
        long deadline = Environment.TickCount64 + 450;
        while (Directory.Exists(resolved))
        {
            try
            {
                Directory.Delete(resolved, recursive: true);
                break;
            }
            catch (IOException) when (OperatingSystem.IsWindows() && Environment.TickCount64 < deadline)
            {
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows() && Environment.TickCount64 < deadline)
            {
            }
            long remaining = deadline - Environment.TickCount64;
            if (remaining > 0)
            {
                Thread.Sleep((int)Math.Min(50, remaining));
            }
        }
    }
}

// A closed synthetic binary grammar demonstrates that authority decoding stays in the adapter.
internal static class SyntheticStateCodec
{
    internal static byte[] Encode(string source, bool reviewDue)
    {
        byte[] content = Encoding.UTF8.GetBytes(source);
        byte[] result = new byte[content.Length + 6];
        result[0] = 0xA7;
        result[1] = reviewDue ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(2), content.Length);
        content.CopyTo(result, 6);
        return result;
    }

    internal static (string Source, bool ReviewDue)? Decode(byte[]? bytes)
    {
        if (bytes is not { Length: >= 6 } || bytes[0] != 0xA7 || bytes[1] > 1 ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(2)) != bytes.Length - 6)
        {
            return null;
        }
        try
        {
            return (new UTF8Encoding(false, true).GetString(bytes.AsSpan(6)), bytes[1] == 1);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
