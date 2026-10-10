// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using Xunit;

namespace Nvt.Core.TestSupport.Tests;

/// <summary>Protects the intentionally small test-support public contract.</summary>
public sealed class PublicApiTests
{
    /// <summary>Only the two planned types and their declared API are exported; disposal implements the two interfaces.</summary>
    [Fact]
    public void PublicSurfaceMatchesThePlan()
    {
        Type clock = typeof(ManualTimeProvider);
        Type workspace = typeof(TestWorkspace);
        Type[] expectedTypes = [clock, workspace];
        Assert.Equal(expectedTypes, clock.Assembly.GetExportedTypes().OrderBy(type => type.Name, StringComparer.Ordinal));
        Assert.True(clock.IsSealed);
        Assert.True(workspace.IsSealed);
        Assert.Equal(typeof(TimeProvider), clock.BaseType);
        Assert.Equal(typeof(DateTimeOffset), Assert.Single(Assert.Single(clock.GetConstructors()).GetParameters()).ParameterType);
        Assert.Empty(workspace.GetConstructors());
        string[] clockMethods = ["Advance"];
        string[] workspaceMethods = ["Create", "Dispose", "DisposeAsync", "GetPath", "get_RootPath"];
        Assert.Equal(clockMethods, NewPublicMethods(clock));
        Assert.Equal(workspaceMethods, NewPublicMethods(workspace));
        Assert.True(typeof(IDisposable).IsAssignableFrom(workspace));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(workspace));
    }

    private static IEnumerable<string> NewPublicMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.GetBaseDefinition().DeclaringType == type)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal);
}
