// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Nvt.Core.LogConsole;
using Nvt.Core.Time;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Explicit app resources, cultures, display zones, and frozen pause time.</summary>
public sealed class TimePresentationTests
{
    /// <summary>Projection and the pure formatter share the approved template and explicit number culture.</summary>
    /// <param name="cultureName">The explicit app culture.</param>
    /// <param name="expected">The complete relative timestamp.</param>
    [Theory]
    [InlineData("", "2.3 s ago")]
    [InlineData("de-DE", "2,3 s ago")]
    public void RelativeSecondsUseExplicitCultureAndApprovedTemplate(string cultureName, string expected)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var now = DateTimeOffset.UnixEpoch.AddMilliseconds(2300);
        using var store = LogStoreTests.CreateStore(clock: new DelegateTimeProvider(() => now));
        store.Add(LogLevel.Info, "app", "message", DateTimeOffset.UnixEpoch);
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter { TimeMode = ConsoleTimeMode.Relative },
            new ConsoleViewState(), new ConsoleProjectionOptions { RelativeTimeTemplate = "{0} s ago", Culture = culture });
        Assert.Equal(expected, Assert.Single(projection.Rows).TimeText);
        Assert.Equal(expected, ConsoleTimeFormatter.Format(DateTimeOffset.UnixEpoch, now,
            ConsoleTimeMode.Relative, "{0} s ago", culture));
    }

    /// <summary>The app's resource template controls text and Core ignores the ambient thread culture.</summary>
    [Fact]
    public void RelativeTimeUsesAppTemplateAndIgnoresThreadCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("before 2.3 seconds", ConsoleTimeFormatter.Format(DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddMilliseconds(2300), ConsoleTimeMode.Relative, "before {0} seconds", CultureInfo.InvariantCulture));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    /// <summary>A later captured snapshot leaves paused relative text at the explicit pause instant.</summary>
    [Fact]
    public void PausedRelativeTimeUsesFrozenBaseWithAppCulture()
    {
        var now = DateTimeOffset.UnixEpoch.AddMilliseconds(2300);
        using var store = LogStoreTests.CreateStore(clock: new DelegateTimeProvider(() => now));
        store.Add(LogLevel.Info, "app", "message", DateTimeOffset.UnixEpoch);
        var options = new ConsoleProjectionOptions { Culture = CultureInfo.GetCultureInfo("de-DE") };
        var filter = new ConsoleFilter { TimeMode = ConsoleTimeMode.Relative };
        using var first = LogStoreTests.Capture(store);
        using var initial = ConsoleProjector.Project(first, filter, new ConsoleViewState(), options);
        var paused = new ConsoleViewState().Pause(initial, initial.Rows[0].Id);
        now = now.AddSeconds(30);
        using var latest = LogStoreTests.Capture(store);
        using var frozen = ConsoleProjector.Project(latest, filter, paused, options);
        using var following = ConsoleProjector.Project(latest, filter, paused.Resume(), options);
        Assert.Equal(initial.TimeBase, frozen.TimeBase);
        Assert.Equal("2,3 s ago", Assert.Single(frozen.Rows).TimeText);
        Assert.Equal("32,3 s ago", Assert.Single(following.Rows).TimeText);
    }

    /// <summary>Absolute and hidden modes preserve their defaults and do not interpret the relative resource.</summary>
    /// <param name="mode">The unchanged display mode.</param>
    /// <param name="expected">The default time text.</param>
    [Theory]
    [InlineData(ConsoleTimeMode.Absolute, "04:05:06.789")]
    [InlineData(ConsoleTimeMode.Hidden, "")]
    public void HiddenAndAbsoluteModesKeepDefaultOutput(ConsoleTimeMode mode, string expected)
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddHours(4).AddMinutes(5).AddMilliseconds(6789);
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "message", timestamp);
        using var snapshot = LogStoreTests.Capture(store);
        using var original = ConsoleProjector.Project(snapshot, new ConsoleFilter { TimeMode = mode }, new ConsoleViewState());
        using var explicitInputs = ConsoleProjector.Project(snapshot, new ConsoleFilter { TimeMode = mode }, new ConsoleViewState(),
            new ConsoleProjectionOptions { Culture = CultureInfo.GetCultureInfo("de-DE"), RelativeTimeTemplate = "{" });
        Assert.Equal(expected, Assert.Single(original.Rows).TimeText);
        Assert.Equal(expected, Assert.Single(explicitInputs.Rows).TimeText);
    }

    /// <summary>The explicit screen zone changes clock time while copy and stream export retain UTC.</summary>
    /// <param name="offsetHours">The supplied display-zone offset.</param>
    /// <param name="expected">The screen clock time.</param>
    [Theory]
    [InlineData(0, "04:05:06.789")]
    [InlineData(8, "12:05:06.789")]
    public async Task AbsoluteDisplayUsesExplicitZoneAndExportStaysUtc(int offsetHours, string expected)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("app-zone", TimeSpan.FromHours(offsetHours), "App zone", "App zone");
        var timestamp = DateTimeOffset.UnixEpoch.AddHours(4).AddMinutes(5).AddMilliseconds(6789);
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "message", timestamp.ToOffset(TimeSpan.FromHours(-3)));
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState(),
            new ConsoleProjectionOptions { AbsoluteTimeZone = zone });
        Assert.Equal(expected, Assert.Single(projection.Rows).TimeText);
        Assert.Equal(expected, ConsoleTimeFormatter.Format(timestamp, timestamp, ConsoleTimeMode.Absolute,
            "{0} s ago", CultureInfo.InvariantCulture, zone));
        const string exported = "04:05:06.789 [Info] [app] message";
        Assert.Equal(exported, ConsoleExportFormatter.FormatVisible(projection));
        using var destination = new MemoryStream();
        await ConsoleExportFormatter.WriteLogAsync(destination, projection, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(exported, System.Text.Encoding.UTF8.GetString(destination.ToArray()));
    }
}
