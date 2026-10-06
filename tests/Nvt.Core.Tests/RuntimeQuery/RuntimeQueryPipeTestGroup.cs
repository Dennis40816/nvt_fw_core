// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Runs all runtime query pipe tests sequentially, without other collections in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RuntimeQueryPipeTestGroup
{
    /// <summary>The shared collection name.</summary>
    public const string Name = "RuntimeQuery pipes";
}
