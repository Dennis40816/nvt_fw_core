// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Verification;

// Turns the byte counts of bounded reads into completed and total bytes for one verification pass.
// Without a sink it returns null, so the reads stay exactly as they are without progress.
internal static class VerifiedByteProgress
{
    internal static Action<int>? Start(Action<long, long>? progress, IReadOnlyList<PackageFile> files)
    {
        if (progress is null)
        {
            return null;
        }
        long total = 0;
        foreach (PackageFile file in files)
        {
            total = checked(total + file.Size);
        }
        return Start(progress, total);
    }

    // Reports zero of a positive total first, then the running count after each read.
    internal static Action<int>? Start(Action<long, long>? progress, long total)
    {
        if (progress is null)
        {
            return null;
        }
        if (total > 0)
        {
            progress(0, total);
        }
        long completed = 0;
        return bytes =>
        {
            completed = checked(completed + bytes);
            progress(completed, total);
        };
    }
}
