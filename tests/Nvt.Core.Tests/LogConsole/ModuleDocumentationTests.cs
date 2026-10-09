// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.RegularExpressions;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Stable store identity across single and batch admission.</summary>
public sealed class ModuleDocumentationTests
{
    /// <summary>Store identities equal their member sequences.</summary>
    [Fact]
    public void StoreEntryIdentityEqualsSequence()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "first");
        store.AddBatch(store.Generation, [LogStoreTests.Write("second")]);
        using var snapshot = LogStoreTests.Capture(store);
        Assert.All(snapshot.Entries, value => Assert.Equal(value.EntryId, value.Sequence));
    }
    /// <summary>Regression classes communicate behavior without internal process names.</summary>
    [Fact]
    public void RegressionClassesUseBehaviorNames()
        => Assert.DoesNotContain(typeof(LogStoreTests).Assembly.GetTypes(), type => type.Namespace == typeof(LogStoreTests).Namespace && Regex.IsMatch(type.Name, @"^Fix[5-9]RegressionTests$"));
}
