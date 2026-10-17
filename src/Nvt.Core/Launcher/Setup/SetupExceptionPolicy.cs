// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Setup;

internal static class SetupExceptionPolicy
{
    internal static bool IsNonFatal(Exception exception) =>
        exception is not (OutOfMemoryException or AccessViolationException);

    internal static void ReportDiagnostics(Exception exception, Action<Exception>? observerDiagnostics)
    {
        try
        {
            observerDiagnostics?.Invoke(exception);
        }
        // Diagnostics are non-authoritative observers; fatal exceptions still propagate.
        catch (Exception diagnosticException) when (IsNonFatal(diagnosticException))
        {
        }
    }
}
