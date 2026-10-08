// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Documented identity and behavior-oriented test names.</summary>
public sealed class ModuleDocumentationTests
{
    /// <summary>Store identities and member sequences share an explicit documented invariant.</summary>
    [Fact]
    public void LogEntryDocumentsIdentityEqualsSequence()
    {
        var xml = XDocument.Load(Path.ChangeExtension(typeof(LogEntry).Assembly.Location, ".xml"));
        var entry = xml.Descendants("member").Single(member => (string?)member.Attribute("name") == "T:Nvt.Core.LogConsole.LogEntry");
        Assert.Contains("EntryId equals Sequence", entry.Value, StringComparison.Ordinal);
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
