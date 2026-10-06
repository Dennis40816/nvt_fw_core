// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Nvt.Core.Avalonia.Primitives;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Primitives;

/// <summary>Characterizes selectable text bindings and the frozen tooltip application lifecycle.</summary>
public sealed class InfoLabelTests
{
    /// <summary>Text and tip are empty and ancestor propagation is disabled by default.</summary>
    [AvaloniaFact]
    public void PropertiesHaveFrozenDefaults()
    {
        var label = new InfoLabel();
        Assert.Equal(string.Empty, label.Text);
        Assert.Equal(string.Empty, label.Tip);
        Assert.Null(label.TipTargetClass);
    }

    /// <summary>The text remains selectable, wrapped and bound to the label in both themes.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectableWrappedTextTracksTextProperty(bool dark)
    {
        var label = new InfoLabel { Text = "First label", Tip = "First tip" };
        var host = CreateHost(label, dark);
        try
        {
            var text = Assert.IsType<SelectableTextBlock>(label.Content);
            Assert.Equal("First label", text.Text);
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            Assert.Equal(FontWeight.SemiBold, text.FontWeight);
            Assert.Equal(HorizontalAlignment.Stretch, label.HorizontalAlignment);
            Assert.True(Application.Current!.TryGetResource("NfcTextStrongBrush", host.ActualThemeVariant,
                out object? expected));
            Assert.Same(expected, text.Foreground);
            Assert.Equal("First tip", ToolTip.GetTip(label));
            label.Text = "Updated label";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Updated label", text.Text);
            label.Text = string.Empty;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(string.Empty, text.Text);

            var foreground = new SolidColorBrush(Colors.Red);
            host.Resources["NfcTextStrongBrush"] = foreground;
            Assert.Same(foreground, text.Foreground);
        }
        finally { host.Close(); }
    }

    /// <summary>A detached label receives a nonblank tooltip as soon as Tip changes.</summary>
    [AvaloniaFact]
    public void TipChangesApplyToDetachedLabel()
    {
        var label = new InfoLabel { TipTargetClass = "tipTarget", Tip = "First tip" };
        Assert.Equal("First tip", ToolTip.GetTip(label));
        label.Tip = "Second tip";
        Assert.Equal("Second tip", ToolTip.GetTip(label));
    }

    /// <summary>Null and empty target classes keep tips on the label and leave ancestor tips untouched.</summary>
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    public void UnsetTargetClassOnlyAppliesToLabel(string? targetClass)
    {
        var label = new InfoLabel { TipTargetClass = targetClass, Tip = "First tip" };
        var parent = new Border { Child = label, Classes = { "tipTarget" } };
        ToolTip.SetTip(parent, "Existing ancestor tip");
        var host = CreateHost(parent);
        try
        {
            Assert.Equal("First tip", ToolTip.GetTip(label));
            Assert.Equal("Existing ancestor tip", ToolTip.GetTip(parent));
            label.Tip = "Second tip";
            Assert.Equal("Second tip", ToolTip.GetTip(label));
            Assert.Equal("Existing ancestor tip", ToolTip.GetTip(parent));
        }
        finally { host.Close(); }
    }

    /// <summary>Attachment searches through nonmatching ancestors and updates only the closest matching control.</summary>
    [AvaloniaFact]
    public void AttachAndTipChangesUseNearestMatchingAncestor()
    {
        var label = new InfoLabel { TipTargetClass = "tipTarget", Tip = "First tip" };
        label.Classes.Add("tipTarget");
        var intermediate = new Grid { Children = { label }, Classes = { "differentClass" } };
        var nearest = new Border { Child = intermediate, Classes = { "tipTarget" } };
        var outer = new Border { Child = nearest, Classes = { "tipTarget" } };
        ToolTip.SetTip(nearest, "Existing nearest tip");
        ToolTip.SetTip(outer, "Existing outer tip");
        var host = CreateHost(outer);
        try
        {
            Assert.Equal("First tip", ToolTip.GetTip(label));
            Assert.Equal("First tip", ToolTip.GetTip(nearest));
            Assert.Equal("Existing outer tip", ToolTip.GetTip(outer));
            Assert.Null(ToolTip.GetTip(intermediate));
            label.Tip = "Second tip";
            Assert.Equal("Second tip", ToolTip.GetTip(label));
            Assert.Equal("Second tip", ToolTip.GetTip(nearest));
            Assert.Equal("Existing outer tip", ToolTip.GetTip(outer));
        }
        finally { host.Close(); }
    }

    /// <summary>A configured class that has no exact ancestor match still leaves the label tooltip available.</summary>
    [AvaloniaFact]
    public void MissingTargetClassLeavesAncestorUnchanged()
    {
        var label = new InfoLabel { TipTargetClass = "tipTarget", Tip = "First tip" };
        var parent = new Border { Child = label, Classes = { "tipTargetSuffix" } };
        ToolTip.SetTip(parent, "Existing ancestor tip");
        var host = CreateHost(parent);
        try
        {
            label.Tip = "Second tip";
            Assert.Equal("Second tip", ToolTip.GetTip(label));
            Assert.Equal("Existing ancestor tip", ToolTip.GetTip(parent));
        }
        finally { host.Close(); }
    }

    /// <summary>Changing the target class alone waits until the next Tip change to apply the tip.</summary>
    [AvaloniaFact]
    public void TargetClassChangeWaitsForTipChange()
    {
        var label = new InfoLabel { Tip = "First tip" };
        var parent = new Border { Child = label, Classes = { "tipTarget" } };
        ToolTip.SetTip(parent, "Existing ancestor tip");
        var host = CreateHost(parent);
        try
        {
            label.TipTargetClass = "tipTarget";
            Assert.Equal("Existing ancestor tip", ToolTip.GetTip(parent));
            label.Tip = "Second tip";
            Assert.Equal("Second tip", ToolTip.GetTip(parent));
            label.TipTargetClass = null;
            label.Tip = "Third tip";
            Assert.Equal("Third tip", ToolTip.GetTip(label));
            Assert.Equal("Second tip", ToolTip.GetTip(parent));
        }
        finally { host.Close(); }
    }

    /// <summary>A blank tip reaches the label through its binding but is not copied to the ancestor.</summary>
    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void BlankTipChangesFollowOnLabelOnly(string blankTip)
    {
        var label = new InfoLabel { TipTargetClass = "tipTarget", Tip = "Applied tip" };
        var parent = new Border { Child = label, Classes = { "tipTarget" } };
        var host = CreateHost(parent);
        try
        {
            label.Tip = blankTip;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(blankTip, ToolTip.GetTip(label));
            Assert.Equal("Applied tip", ToolTip.GetTip(parent));
        }
        finally { host.Close(); }
    }

    /// <summary>Attachment with a blank tip leaves existing label and ancestor tooltips untouched.</summary>
    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTipOnAttachLeavesExistingTipsUnchanged(string blankTip)
    {
        var label = new InfoLabel { TipTargetClass = "tipTarget", Tip = blankTip };
        ToolTip.SetTip(label, "Existing label tip");
        var parent = new Border { Child = label, Classes = { "tipTarget" } };
        ToolTip.SetTip(parent, "Existing ancestor tip");
        var host = CreateHost(parent);
        try
        {
            Assert.Equal("Existing label tip", ToolTip.GetTip(label));
            Assert.Equal("Existing ancestor tip", ToolTip.GetTip(parent));
        }
        finally { host.Close(); }
    }

    /// <summary>Reattachment applies the current tip to the new ancestor without clearing the previous ancestor.</summary>
    [AvaloniaFact]
    public void ReattachAppliesTipToNewMatchingAncestor()
    {
        var label = new InfoLabel { TipTargetClass = "tipTarget", Tip = "Applied tip" };
        var first = new Border { Child = label, Classes = { "tipTarget" } };
        var second = new Border { Classes = { "tipTarget" } };
        var host = CreateHost(new StackPanel { Children = { first, second } });
        try
        {
            Assert.Equal("Applied tip", ToolTip.GetTip(first));
            first.Child = null;
            second.Child = label;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Applied tip", ToolTip.GetTip(second));
            Assert.Equal("Applied tip", ToolTip.GetTip(first));
        }
        finally { host.Close(); }
    }

    private static Window CreateHost(Control content, bool dark = false)
    {
        var uri = new Uri("avares://Nvt.Core.Avalonia/Primitives/PrimitivesStyles.axaml");
        var host = new Window
        {
            Content = content,
            Width = 320,
            Height = 200,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        host.Styles.Add(new StyleInclude(uri) { Source = uri });
        host.Show();
        Dispatcher.UIThread.RunJobs();
        return host;
    }
}
