// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.Avalonia.Tests.Icons;

/// <summary>Serializes isolated icon sessions against the assembly's shared Avalonia host.</summary>
[CollectionDefinition(nameof(IconTestIsolation), DisableParallelization = true)]
public sealed class IconTestIsolation
{
}
