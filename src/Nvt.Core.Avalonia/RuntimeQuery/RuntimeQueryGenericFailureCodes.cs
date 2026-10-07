// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Avalonia.RuntimeQuery;

/// <summary>Wire error codes returned by the generic commands.</summary>
public static class RuntimeQueryGenericFailureCodes
{
    /// <summary>The required main window is unavailable.</summary>
    public const string NoMainWindow = "NO_MAIN_WINDOW";
    /// <summary>The tool needs a person to confirm; it opened no dialog. This differs from the router's --confirm guard.</summary>
    public const string UserConfirmationRequired = "USER_CONFIRMATION_REQUIRED";
    /// <summary>The tool rejected a page switch.</summary>
    public const string PageRejected = "PAGE_REJECTED";
    /// <summary>The supplied page is absent from the tool's list.</summary>
    public const string UnknownPage = "UNKNOWN_PAGE";
    /// <summary>A required command argument is invalid or missing.</summary>
    public const string InvalidArguments = "INVALID_ARGUMENTS";
    /// <summary>The capture destination exists and must not be replaced.</summary>
    public const string FileExists = "FILE_EXISTS";
    /// <summary>The main window has no visible size for the default capture.</summary>
    public const string CaptureUnavailable = "CAPTURE_UNAVAILABLE";

    /// <summary>The tool rejected closing.</summary>
    public const string ExitRejected = "EXIT_REJECTED";
}
