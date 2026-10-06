// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Progress;
using Nvt.Core.Progress;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Progress;

/// <summary>Preserves ProgressBar defaults, styles and caller-owned animation.</summary>
public sealed class ProgressIndicatorTests
{
    /// <summary>Known fractions change only the value, including when NFC keeps animation enabled.</summary>
    /// <param name="animate">The caller's animation setting.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FractionsAndNullUpdatesPreserveCallerSettings(bool animate)
    {
        var indicator = new ProgressIndicator
        {
            IsIndeterminate = animate,
            Maximum = 1,
            IsVisible = false,
            Foreground = Brushes.Purple,
        };
        foreach (double fraction in new double[] { 0, 0.42, 1 })
        {
            indicator.Progress = new ProgressUpdate(fraction, "Synthetic step");
            Assert.Equal(fraction, indicator.Value);
            indicator.Progress = new ProgressUpdate(null, "Unknown");
            Assert.Equal(fraction, indicator.Value);
            indicator.Progress = null;
            Assert.Equal(fraction, indicator.Value);
            Assert.Equal(animate, indicator.IsIndeterminate);
            Assert.Equal(1, indicator.Maximum);
            Assert.False(indicator.IsVisible);
            Assert.Same(Brushes.Purple, indicator.Foreground);
        }
    }

    /// <summary>Core leaves the range at ProgressBar's default until the caller supplies a range.</summary>
    [AvaloniaFact]
    public void DefaultMaximumMatchesProgressBar()
    {
        var indicator = new ProgressIndicator();
        Assert.Null(indicator.Progress);
        Assert.Equal(new ProgressBar().Maximum, indicator.Maximum);
        indicator.Progress = new ProgressUpdate(0.42, "Synthetic step");
        Assert.Equal(0.42, indicator.Value);
        Assert.Equal(new ProgressBar().Maximum, indicator.Maximum);
    }

    /// <summary>A selector for ProgressBar applies to ProgressIndicator through its style key.</summary>
    [AvaloniaFact]
    public void ProgressBarStyleAppliesToIndicator()
    {
        var indicator = new ProgressIndicator();
        var host = new Window { Content = indicator };
        host.Styles.Add(new Style(selector => selector.OfType<ProgressBar>())
        {
            Setters = { new Setter(ProgressBar.ForegroundProperty, Brushes.Purple) },
        });
        try
        {
            host.Show();
            Assert.Equal(typeof(ProgressBar), indicator.StyleKey);
            Assert.Same(Brushes.Purple, indicator.Foreground);
        }
        finally { host.Close(); }
    }
}