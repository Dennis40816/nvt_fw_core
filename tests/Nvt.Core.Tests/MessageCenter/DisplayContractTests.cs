// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.MessageCenter;
using Xunit;

namespace Nvt.Core.Tests.MessageCenter;

/// <summary>Characterizes the frozen passive display contract with synthetic host entries and text.</summary>
public sealed class DisplayContractTests
{
    /// <summary>Each defined severity sets exactly its own flag; undefined values set none.</summary>
    [Theory]
    [InlineData(MessageActivitySeverity.Information, true, false, false, false)]
    [InlineData(MessageActivitySeverity.Success, false, true, false, false)]
    [InlineData(MessageActivitySeverity.Warning, false, false, true, false)]
    [InlineData(MessageActivitySeverity.Error, false, false, false, true)]
    [InlineData((MessageActivitySeverity)(-1), false, false, false, false)]
    [InlineData((MessageActivitySeverity)4, false, false, false, false)]
    public void SeverityFlagsMatchTheSuppliedSeverity(
        MessageActivitySeverity severity, bool information, bool success, bool warning, bool error)
    {
        MessageCenterActivityItem row = CreateRow("sample", severity);

        Assert.Equal(information, row.IsInformation);
        Assert.Equal(success, row.IsSuccess);
        Assert.Equal(warning, row.IsWarning);
        Assert.Equal(error, row.IsError);
    }

    /// <summary>Accessible text preserves the complete field order, empty fields, and punctuation.</summary>
    [Theory]
    [InlineData("time", "title", "detail", "category", "status", "time. title. category. detail. status.")]
    [InlineData("", "", "", "", "", ". . . . .")]
    [InlineData("", "title", "", "category", "", ". title. category. . .")]
    [InlineData(" time ", "title.", "detail\nnext", "category", "status!", " time . title.. category. detail\nnext. status!.")]
    public void AccessibleTextPreservesAllHostFields(
        string time, string title, string detail, string category, string status, string expected)
    {
        var row = new MessageCenterActivityItem(time, title, detail, category, status, MessageActivitySeverity.Information);

        Assert.Equal(time, row.Time);
        Assert.Equal(title, row.Title);
        Assert.Equal(detail, row.Detail);
        Assert.Equal(category, row.Category);
        Assert.Equal(status, row.Status);
        Assert.Equal(expected, row.AccessibleText);
    }

    /// <summary>A typed binding alias inherits the exact row behavior without overriding it.</summary>
    [Fact]
    public void BindingAliasInheritsRowDisplayBehavior()
    {
        MessageCenterActivityItem row = CreateRow("sample", MessageActivitySeverity.Warning);
        var alias = new BindingAlias(row);

        Assert.Equal(row.AccessibleText, alias.AccessibleText);
        Assert.Equal(row.IsInformation, alias.IsInformation);
        Assert.Equal(row.IsSuccess, alias.IsSuccess);
        Assert.Equal(row.IsWarning, alias.IsWarning);
        Assert.Equal(row.IsError, alias.IsError);
    }

    /// <summary>All filter/disclosure combinations project exactly the retained rows in sequence order.</summary>
    [Theory]
    [InlineData(MessageActivityFilter.Important, false, "error|warning|success|information")]
    [InlineData(MessageActivityFilter.Important, true, "debug-error|debug-warning|debug-success|debug-information|error|warning|success|information")]
    [InlineData(MessageActivityFilter.Warnings, false, "warning")]
    [InlineData(MessageActivityFilter.Warnings, true, "debug-warning|warning")]
    [InlineData(MessageActivityFilter.Errors, false, "error")]
    [InlineData(MessageActivityFilter.Errors, true, "debug-error|error")]
    public void FiltersProjectOnlyRetainedRowsInExactOrder(MessageActivityFilter filter, bool includeDebug, string expected)
    {
        var projections = new List<string>();
        MessageCenterActivity[] activities = CreateActivities(projections.Add);
        var provider = new SyntheticProvider(activities, 2);

        IReadOnlyList<MessageCenterActivityItem> rows = MessageCenterActivityFilter.Apply(provider.CaptureActivity(), filter, includeDebug);

        Assert.Equal(expected, string.Join("|", rows.Select(static row => row.Title)));
        Assert.Equal(expected, string.Join("|", projections));
        Assert.Equal(2, provider.ActiveDiagnosticCount);
        Assert.Equal(8, provider.ActivityCount);
        Assert.All(rows, row => Assert.Equal(
            activities.Single(activity => activity.Sequence == SequenceForTitle(row.Title)).Severity,
            row.Severity));
    }

    /// <summary>Stable descending sorting accepts the entire signed sequence domain and retains tie order.</summary>
    [Fact]
    public void ShuffledSequencesAndTiesPreserveStableOrderAtSignedBoundaries()
    {
        var projections = new List<string>();
        MessageCenterActivity[] activities =
        [
            CreateActivity(0, MessageActivityImportance.Important, MessageActivitySeverity.Information, "zero", projections.Add),
            CreateActivity(long.MaxValue, MessageActivityImportance.Important, MessageActivitySeverity.Success, "max-first", projections.Add),
            CreateActivity(long.MinValue, MessageActivityImportance.Important, MessageActivitySeverity.Error, "min", projections.Add),
            CreateActivity(-1, MessageActivityImportance.Important, MessageActivitySeverity.Warning, "negative", projections.Add),
            CreateActivity(long.MaxValue - 1, MessageActivityImportance.Important, MessageActivitySeverity.Information, "below-max", projections.Add),
            CreateActivity(long.MaxValue, MessageActivityImportance.Important, MessageActivitySeverity.Warning, "max-second", projections.Add),
            CreateActivity(long.MinValue + 1, MessageActivityImportance.Important, MessageActivitySeverity.Information, "above-min", projections.Add),
        ];

        IReadOnlyList<MessageCenterActivityItem> rows = MessageCenterActivityFilter.Apply(activities, MessageActivityFilter.Important, false);

        const string expected = "max-first|max-second|below-max|zero|negative|above-min|min";
        Assert.Equal(expected, string.Join("|", rows.Select(static row => row.Title)));
        Assert.Equal(expected, string.Join("|", projections));
    }

    /// <summary>The inventory's exact warning/error order survives pane changes without changing history.</summary>
    [Fact]
    public void InventoryFiltersPreserveRowsAndHostHistory()
    {
        string warning = "warning-" + new string('W', 110);
        string error = "error-" + new string('E', 110);
        MessageCenterActivity[] activities =
        [
            CreateActivity(1, MessageActivityImportance.Important, MessageActivitySeverity.Warning, warning),
            CreateActivity(2, MessageActivityImportance.Important, MessageActivitySeverity.Error, error),
            CreateActivity(3, MessageActivityImportance.Debug, MessageActivitySeverity.Warning, "debug-warning"),
            CreateActivity(4, MessageActivityImportance.Debug, MessageActivitySeverity.Error, "debug-error"),
        ];
        var provider = new SyntheticProvider(activities, 0);
        IReadOnlyList<MessageCenterActivity> captured = provider.CaptureActivity();

        MessageCenterActivityItem warningRow = Assert.Single(MessageCenterActivityFilter.Apply(captured, MessageActivityFilter.Warnings, false));
        Assert.Equal(warning, warningRow.Detail);
        Assert.True(warningRow.IsWarning);
        Assert.Equal("debug-warning|" + warning, string.Join("|", MessageCenterActivityFilter.Apply(captured, MessageActivityFilter.Warnings, true).Select(static row => row.Detail)));
        IReadOnlyList<MessageCenterActivityItem> errors = MessageCenterActivityFilter.Apply(captured, MessageActivityFilter.Errors, true);
        Assert.Equal("debug-error|" + error, string.Join("|", errors.Select(static row => row.Detail)));
        Assert.All(errors, static row => Assert.True(row.IsError));
        Assert.Equal(error, Assert.Single(MessageCenterActivityFilter.Apply(captured, MessageActivityFilter.Errors, false)).Detail);

        var session = new MessageCenterSession();
        session.SelectActivity(false);
        session.SelectActivity(true);
        Assert.Equal(captured, provider.CaptureActivity());
        Assert.Equal(error, Assert.Single(MessageCenterActivityFilter.Apply(captured, MessageActivityFilter.Errors, false)).Detail);
    }

    /// <summary>Every filter/disclosure combination accepts an empty source without invoking projection.</summary>
    [Theory]
    [InlineData(MessageActivityFilter.Important, false)]
    [InlineData(MessageActivityFilter.Important, true)]
    [InlineData(MessageActivityFilter.Warnings, false)]
    [InlineData(MessageActivityFilter.Warnings, true)]
    [InlineData(MessageActivityFilter.Errors, false)]
    [InlineData(MessageActivityFilter.Errors, true)]
    public void EmptySourcesProduceEmptyResults(MessageActivityFilter filter, bool includeDebug)
    {
        Assert.Empty(MessageCenterActivityFilter.Apply(Array.Empty<MessageCenterActivity>(), filter, includeDebug));
    }

    /// <summary>Disclosure and severity exclusion produce empty results without invoking faulty projections.</summary>
    [Fact]
    public void HiddenRowsAreNeverProjected()
    {
        int projections = 0;
        MessageCenterActivityItem FailProjection()
        {
            projections++;
            throw new InvalidOperationException("Hidden projection");
        }
        MessageCenterActivity[] activities =
        [
            new(1, MessageActivityImportance.Debug, MessageActivitySeverity.Warning, FailProjection),
            new(2, MessageActivityImportance.Important, MessageActivitySeverity.Information, FailProjection),
        ];

        Assert.Empty(MessageCenterActivityFilter.Apply(activities, MessageActivityFilter.Warnings, false));
        Assert.Empty(MessageCenterActivityFilter.Apply(activities, MessageActivityFilter.Errors, true));
        Assert.Equal(0, projections);
    }

    /// <summary>Counts and metadata capture require no row formatting, even when projection would fail.</summary>
    [Fact]
    public void BadgeAndSummaryCountsAndCaptureNeedNoRowProjection()
    {
        int projections = 0;
        MessageCenterActivity[] activities = CreateActivities(_ =>
        {
            projections++;
            throw new InvalidOperationException("Projection is not needed for counts");
        });
        IMessageCenterProvider provider = new SyntheticProvider(activities, 2);

        Assert.Equal(2, provider.ActiveDiagnosticCount);
        Assert.Equal(8, provider.ActivityCount);
        Assert.Equal(activities, provider.CaptureActivity());
        Assert.Equal(0, projections);
    }

    /// <summary>Captures and materialized rows retain their values after the host changes its source history.</summary>
    [Fact]
    public void CapturesAndFilteredRowsSurviveLaterSourceMutation()
    {
        MessageCenterActivity[] activities = CreateActivities();
        var provider = new SyntheticProvider(activities, 2);
        IReadOnlyList<MessageCenterActivity> captured = provider.CaptureActivity();
        IReadOnlyList<MessageCenterActivityItem> rows = MessageCenterActivityFilter.Apply(captured, MessageActivityFilter.Warnings, true);

        provider.Entries.Clear();
        provider.Entries.Add(CreateActivity(99, MessageActivityImportance.Important, MessageActivitySeverity.Warning, "replacement"));

        Assert.Equal(1, provider.ActivityCount);
        Assert.Equal(activities, captured);
        Assert.Equal("debug-warning|warning", string.Join("|", rows.Select(static row => row.Title)));
        Assert.Equal("debug-warning|warning", string.Join("|", MessageCenterActivityFilter.Apply(captured, MessageActivityFilter.Warnings, true).Select(static row => row.Title)));
        Assert.Equal("replacement", Assert.Single(MessageCenterActivityFilter.Apply(provider.CaptureActivity(), MessageActivityFilter.Warnings, true)).Title);
    }

    /// <summary>The disclosure/reprojection test uses two distinct text sets supplied entirely by the host.</summary>
    [Fact]
    public void ActivityHistoryUsesTwoDisclosureLevelsAndReprojectsHostText()
    {
        string textSet = "first";
        MessageCenterActivity[] activities =
        [
            new(1, MessageActivityImportance.Important, MessageActivitySeverity.Information, () => CreateRow(textSet + " initial", MessageActivitySeverity.Information)),
            new(2, MessageActivityImportance.Debug, MessageActivitySeverity.Information, () => CreateRow(textSet + " navigation", MessageActivitySeverity.Information)),
            new(3, MessageActivityImportance.Important, MessageActivitySeverity.Success, () => CreateRow(textSet + " ready", MessageActivitySeverity.Success)),
        ];
        IReadOnlyList<MessageCenterActivityItem> initial = MessageCenterActivityFilter.Apply(activities, MessageActivityFilter.Important, false);
        Assert.DoesNotContain(initial, static row => row.Title == "first navigation title");
        Assert.Equal("first ready detail", Assert.Single(initial, static row => row.Title == "first ready title").Detail);
        IReadOnlyList<MessageCenterActivityItem> expanded = MessageCenterActivityFilter.Apply(activities, MessageActivityFilter.Important, true);
        Assert.Contains(expanded, static row => row.Title == "first navigation title");
        Assert.True(expanded.Count > initial.Count);

        textSet = "second";
        IReadOnlyList<MessageCenterActivityItem> reprojected = MessageCenterActivityFilter.Apply(activities, MessageActivityFilter.Important, true);

        Assert.Equal("second ready detail", Assert.Single(reprojected, static row => row.Title == "second ready title").Detail);
        Assert.Equal(CreateRow("second ready", MessageActivitySeverity.Success), reprojected[0]);
        Assert.Equal(CreateRow("second navigation", MessageActivitySeverity.Information), reprojected[1]);
        Assert.Equal(CreateRow("second initial", MessageActivitySeverity.Information), reprojected[2]);
        Assert.Equal(CreateRow("first ready", MessageActivitySeverity.Success), initial[0]);
    }

    /// <summary>A retained projection fault propagates unchanged after earlier rows and prevents later projections.</summary>
    [Fact]
    public void ProjectionFaultPropagatesInSortedOrder()
    {
        var projections = new List<string>();
        var failure = new InvalidOperationException("Synthetic projection failure");
        MessageCenterActivity[] activities =
        [
            CreateActivity(0, MessageActivityImportance.Important, MessageActivitySeverity.Error, "later", projections.Add),
            new(1, MessageActivityImportance.Important, MessageActivitySeverity.Error, () =>
            {
                projections.Add("fault");
                throw failure;
            }),
            CreateActivity(2, MessageActivityImportance.Important, MessageActivitySeverity.Error, "first", projections.Add),
        ];

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
        {
            MessageCenterActivityFilter.Apply(activities, MessageActivityFilter.Errors, false);
        });

        Assert.Same(failure, thrown);
        Assert.Equal("first|fault", string.Join("|", projections));
    }

    /// <summary>Sorting consumes the source before projection; enumeration faults propagate without projecting rows.</summary>
    [Fact]
    public void EnumerationFaultPrecedesAllRowProjection()
    {
        var trace = new List<string>();
        var failure = new InvalidOperationException("Synthetic enumeration failure");
        IEnumerable<MessageCenterActivity> Enumerate()
        {
            trace.Add("enumerate");
            yield return CreateActivity(1, MessageActivityImportance.Important, MessageActivitySeverity.Information, "row", trace.Add);
            trace.Add("fault");
            throw failure;
        }

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
        {
            MessageCenterActivityFilter.Apply(Enumerate(), MessageActivityFilter.Important, false);
        });

        Assert.Same(failure, thrown);
        Assert.Equal("enumerate|fault", string.Join("|", trace));
    }

    /// <summary>Invalid filters are evaluated only when a row survives disclosure, preserving exception details.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void InvalidFilterIsCheckedOnlyAfterDisclosure(int filterValue)
    {
        var filter = (MessageActivityFilter)filterValue;
        MessageCenterActivity[] hidden = [new(1, MessageActivityImportance.Debug, MessageActivitySeverity.Error, null!)];

        Assert.Empty(MessageCenterActivityFilter.Apply(Array.Empty<MessageCenterActivity>(), filter, true));
        Assert.Empty(MessageCenterActivityFilter.Apply(hidden, filter, false));
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            MessageCenterActivityFilter.Apply(hidden, filter, true);
        });

        Assert.Null(thrown.ParamName);
        Assert.Null(thrown.ActualValue);
        // The expected message comes from the same parameterless constructor that NFC uses.
#pragma warning disable CA2208
        Assert.Equal(new ArgumentOutOfRangeException().Message, thrown.Message);
#pragma warning restore CA2208
    }

    /// <summary>A null source fails in LINQ before an invalid filter can be evaluated.</summary>
    [Fact]
    public void NullSourceFailsBeforeFilterEvaluation()
    {
        ArgumentNullException thrown = Assert.Throws<ArgumentNullException>(() =>
        {
            MessageCenterActivityFilter.Apply(null!, (MessageActivityFilter)(-1), false);
        });

        Assert.Equal("source", thrown.ParamName);
    }

    /// <summary>Undefined importance is disclosed only by expansion, without adding enum validation.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void UndefinedImportanceUsesTheOriginalDisclosurePredicate(int importanceValue)
    {
        MessageCenterActivity[] activities = [CreateActivity(1, (MessageActivityImportance)importanceValue, MessageActivitySeverity.Warning, "row")];

        Assert.Empty(MessageCenterActivityFilter.Apply(activities, MessageActivityFilter.Important, false));
        Assert.Equal("row", Assert.Single(MessageCenterActivityFilter.Apply(activities, MessageActivityFilter.Warnings, true)).Title);
    }

    /// <summary>Undefined severity remains in Important and is excluded by the warning and error predicates.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void UndefinedSeverityUsesTheOriginalSeverityPredicates(int severityValue)
    {
        var severity = (MessageActivitySeverity)severityValue;
        MessageCenterActivity[] activities = [CreateActivity(1, MessageActivityImportance.Important, severity, "row")];

        Assert.Equal(severity, Assert.Single(MessageCenterActivityFilter.Apply(activities, MessageActivityFilter.Important, false)).Severity);
        Assert.Empty(MessageCenterActivityFilter.Apply(activities, MessageActivityFilter.Warnings, false));
        Assert.Empty(MessageCenterActivityFilter.Apply(activities, MessageActivityFilter.Errors, false));
    }

    /// <summary>Projection remains host-owned; Core adds no mismatch or null-delegate validation exception.</summary>
    [Fact]
    public void HostProjectionHasNoAdditionalValidation()
    {
        MessageCenterActivityItem row = CreateRow("host", MessageActivitySeverity.Error);
        MessageCenterActivity[] mismatched = [new(1, MessageActivityImportance.Important, MessageActivitySeverity.Warning, () => row)];
        MessageCenterActivity[] missing = [new(1, MessageActivityImportance.Debug, MessageActivitySeverity.Warning, null!)];

        Assert.Same(row, Assert.Single(MessageCenterActivityFilter.Apply(mismatched, MessageActivityFilter.Warnings, false)));
        Assert.Empty(MessageCenterActivityFilter.Apply(missing, MessageActivityFilter.Warnings, false));
        Assert.Throws<NullReferenceException>(() =>
        {
            MessageCenterActivityFilter.Apply(missing, MessageActivityFilter.Warnings, true);
        });
    }

    private static MessageCenterActivityItem CreateRow(string prefix, MessageActivitySeverity severity) =>
        new(prefix + " time", prefix + " title", prefix + " detail", prefix + " category", prefix + " status", severity);

    private static MessageCenterActivity CreateActivity(
        long sequence, MessageActivityImportance importance, MessageActivitySeverity severity, string title, Action<string>? projected = null) =>
        new(sequence, importance, severity, () =>
        {
            projected?.Invoke(title);
            return new MessageCenterActivityItem("time", title, title, "category", "status", severity);
        });

    private static MessageCenterActivity[] CreateActivities(Action<string>? projected = null) =>
    [
        CreateActivity(3, MessageActivityImportance.Important, MessageActivitySeverity.Warning, "warning", projected),
        CreateActivity(8, MessageActivityImportance.Debug, MessageActivitySeverity.Error, "debug-error", projected),
        CreateActivity(1, MessageActivityImportance.Important, MessageActivitySeverity.Information, "information", projected),
        CreateActivity(6, MessageActivityImportance.Debug, MessageActivitySeverity.Success, "debug-success", projected),
        CreateActivity(4, MessageActivityImportance.Important, MessageActivitySeverity.Error, "error", projected),
        CreateActivity(2, MessageActivityImportance.Important, MessageActivitySeverity.Success, "success", projected),
        CreateActivity(7, MessageActivityImportance.Debug, MessageActivitySeverity.Warning, "debug-warning", projected),
        CreateActivity(5, MessageActivityImportance.Debug, MessageActivitySeverity.Information, "debug-information", projected),
    ];

    private static long SequenceForTitle(string title) => title switch
    {
        "information" => 1,
        "success" => 2,
        "warning" => 3,
        "error" => 4,
        "debug-information" => 5,
        "debug-success" => 6,
        "debug-warning" => 7,
        "debug-error" => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(title)),
    };

    private sealed class SyntheticProvider(IEnumerable<MessageCenterActivity> activities, int activeDiagnosticCount) : IMessageCenterProvider
    {
        internal List<MessageCenterActivity> Entries { get; } = [.. activities];

        public int ActiveDiagnosticCount => activeDiagnosticCount;

        public int ActivityCount => Entries.Count;

        public IReadOnlyList<MessageCenterActivity> CaptureActivity() => Array.AsReadOnly(Entries.ToArray());
    }

    private sealed record BindingAlias : MessageCenterActivityItem
    {
        internal BindingAlias(MessageCenterActivityItem item)
            : base(item.Time, item.Title, item.Detail, item.Category, item.Status, item.Severity)
        {
        }
    }
}
