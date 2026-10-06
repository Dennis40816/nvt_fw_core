// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;

namespace Nvt.Core.Avalonia.Shell;

/// <summary>Admits deferred content and synchronously materializes a caller-supplied page template.</summary>
/// <remarks>
/// The caller owns page identities, hosts, templates, UI-thread access, and run-idle scheduling.
/// Calls are synchronous and property changes or template builds may reenter on the calling thread.
/// </remarks>
public static class PageHost
{
    /// <summary>Assigns content only when loading is requested and the host's content is null.</summary>
    /// <param name="host">The caller's content host; it is not accessed when loading is not requested.</param>
    /// <param name="shouldLoad">Whether to admit the supplied content.</param>
    /// <param name="content">The content to assign without explicitly building or changing the template.</param>
    /// <remarks>Existing non-null content is retained. Assignment exceptions propagate unchanged.</remarks>
    public static void EnsureContent(ContentControl host, bool shouldLoad, object content)
    {
        if (shouldLoad)
        {
            host.Content ??= content;
        }
    }

    /// <summary>Builds the host's template only when its content is null at entry.</summary>
    /// <param name="host">The caller's content host.</param>
    /// <param name="dataContext">The value passed to the template and assigned to a built control.</param>
    /// <remarks>
    /// A missing template or null build result falls back to <see cref="EnsureContent" /> and retains
    /// the template. A built control receives its DataContext first, then the host's ContentTemplate
    /// is cleared, then Content is assigned. Build and assignment exceptions propagate unchanged,
    /// without rollback. Successful builds do not recheck Content after synchronous reentry.
    /// </remarks>
    public static void MaterializeContent(ContentControl host, object dataContext)
    {
        if (host.Content is not null)
        {
            return;
        }

        Control? content = host.ContentTemplate?.Build(dataContext);
        if (content is null)
        {
            EnsureContent(host, shouldLoad: true, dataContext);
            return;
        }

        content.DataContext = dataContext;
        host.ContentTemplate = null;
        host.Content = content;
    }
}
