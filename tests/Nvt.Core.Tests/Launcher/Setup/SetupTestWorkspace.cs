// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.TestSupport;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Owns unique synthetic paths for one setup test.</summary>
public abstract class SetupTestWorkspace : IDisposable
{
    private readonly TestWorkspace _workspace = TestWorkspace.Create();

    /// <summary>Resolves a synthetic path within this test's temporary workspace.</summary>
    protected string Root(string name) => _workspace.GetPath(name);

    internal static void Cancel(CancellationTokenSource? cancellation) => cancellation?.Cancel();

    /// <inheritdoc />
    public void Dispose()
    {
        _workspace.Dispose();
        GC.SuppressFinalize(this);
    }
}
