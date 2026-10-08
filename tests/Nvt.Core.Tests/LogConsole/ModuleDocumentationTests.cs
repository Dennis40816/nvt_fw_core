// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Stable store identity across single and batch admission.</summary>
public sealed class ModuleDocumentationTests
{
    /// <summary>Store identities equal their member sequences.</summary>
    [Fact]
    public void LogEntryDocumentsIdentityEqualsSequence()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "first");
        store.AddBatch(store.Generation, [LogStoreTests.Write("second")]);
        using var snapshot = LogStoreTests.Capture(store);
        Assert.All(snapshot.Entries, value => Assert.Equal(value.EntryId, value.Sequence));
    }

}
