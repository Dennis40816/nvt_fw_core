// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Integration;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Explicit root-based compatibility behavior for tests not concerned with exact files.</summary>
internal interface IRootCatalogSourceTestDouble : IUpdateCatalogSource
{
    ValueTask<UpdateCatalogLoadResult> IUpdateCatalogSource.LoadCatalogAsync(
        string catalogPath,
        CancellationToken cancellationToken)
    {
        string sourceRoot = Path.GetDirectoryName(catalogPath) ??
            throw new ArgumentException("Test Catalog path has no parent.", nameof(catalogPath));
        return LoadAsync(sourceRoot, cancellationToken);
    }
}
