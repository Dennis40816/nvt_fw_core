// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Dialogs;
using Xunit;
using Path = Avalonia.Controls.Shapes.Path;

namespace Nvt.Core.Avalonia.Tests.Dialogs;

/// <summary>Characterizes NFH's frozen confirmation dialog behavior in the existing headless host.</summary>
public sealed class ConfirmDialogTests
{
    private static readonly Uri ButtonStylesUri = new("avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml");

    /// <summary>Supplied text remains selectable, including empty and multiline Unicode content.</summary>
    [AvaloniaTheory]
    [InlineData("Confirm change", "Continue with this change?", "Continue", "Cancel")]
    [InlineData("", "", "", "")]
    [InlineData("確認變更", "第一行\nSecond line", "確認", "取消")]
    public void SuppliedTextAppears(string title, string message, string confirmText, string cancelText)
    {
        var dialog = new ConfirmDialog(title, message, confirmText, cancelText);
        try
        {
            dialog.Show();
            Assert.Equal(title, dialog.FindControl<SelectableTextBlock>("TitleText")!.Text);
            Assert.Equal(message, dialog.FindControl<SelectableTextBlock>("MessageText")!.Text);
            Assert.Equal(confirmText, dialog.FindControl<SelectableTextBlock>("ConfirmButtonText")!.Text);
            Assert.Equal(cancelText, dialog.FindControl<SelectableTextBlock>("CancelButtonText")!.Text);
            Assert.Equal(TextWrapping.Wrap, dialog.FindControl<SelectableTextBlock>("MessageText")!.TextWrapping);
        }
        finally { dialog.Close(); }
    }

    /// <summary>Button click handlers complete the actual modal task with the source boolean results.</summary>
    [AvaloniaTheory]
    [InlineData("ConfirmButton", true, false)]
    [InlineData("CancelButton", false, false)]
    [InlineData("ConfirmButton", true, true)]
    [InlineData("CancelButton", false, true)]
    public async Task ButtonClickReturnsModalResult(string buttonName, bool expected, bool emphasizeCancel)
    {
        var owner = new Window();
        var dialog = new ConfirmDialog("Confirm", "Proceed?", "Yes", "No", emphasizeCancel);
        try
        {
            owner.Show();
            Task<bool> result = dialog.ShowDialog<bool>(owner);
            Assert.False(result.IsCompleted);
            Assert.Same(owner, dialog.Owner);
            dialog.FindControl<Button>(buttonName)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(result.IsCompleted);
            Assert.Equal(expected, await result);
            Assert.False(dialog.IsVisible);
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    /// <summary>Closing without a button returns null to a nullable caller and false to a Boolean caller.</summary>
    [AvaloniaFact]
    public async Task ClosingWithoutAButtonReturnsDefaultResult()
    {
        var owner = new Window();
        var nullableDialog = new ConfirmDialog("Unsaved", "Save first?", "Save", "Discard", emphasizeCancel: true);
        var plainDialog = new ConfirmDialog("Confirm", "Proceed?", "Yes", "No");
        try
        {
            owner.Show();
            Task<bool?> nullableResult = nullableDialog.ShowDialog<bool?>(owner);
            nullableDialog.Close();
            Assert.Null(await nullableResult);

            Task<bool> plainResult = plainDialog.ShowDialog<bool>(owner);
            plainDialog.Close();
            Assert.False(await plainResult);
        }
        finally
        {
            nullableDialog.Close();
            plainDialog.Close();
            owner.Close();
        }
    }

    /// <summary>The emphasized cancel icon follows the label's effective foreground at rest, on hover and with a host value.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelIconFollowsButtonForeground(bool dark)
    {
        var dialog = new ConfirmDialog("Confirm", "Proceed?", "Yes", "No", emphasizeCancel: true)
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        dialog.Classes.Add("reducedMotion");
        dialog.Styles.Add(new StyleInclude(ButtonStylesUri) { Source = ButtonStylesUri });
        try
        {
            dialog.Show();
            Button cancel = dialog.FindControl<Button>("CancelButton")!;
            Path icon = dialog.FindControl<Path>("CancelIcon")!;
            SelectableTextBlock label = dialog.FindControl<SelectableTextBlock>("CancelButtonText")!;
            AssertColor(icon.Stroke, "NfcDangerTextBrush", dialog.ActualThemeVariant);
            AssertSameColor(label.Foreground, icon.Stroke);

            Point center = cancel.TranslatePoint(new Point(cancel.Bounds.Width / 2, cancel.Bounds.Height / 2), dialog)!.Value;
            dialog.MouseMove(center);
            AssertColor(icon.Stroke, "NfcDangerTextBrush", dialog.ActualThemeVariant);
            AssertSameColor(label.Foreground, icon.Stroke);

            dialog.MouseMove(new Point(1, 1));
            cancel.Foreground = Brushes.Green;
            AssertSameColor(Brushes.Green, icon.Stroke);
            AssertSameColor(label.Foreground, icon.Stroke);
        }
        finally { dialog.Close(); }
    }

    private static void AssertColor(IBrush? actual, string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out object? expected));
        AssertSameColor(Assert.IsAssignableFrom<IBrush>(expected), actual);
    }

    private static void AssertSameColor(IBrush? expected, IBrush? actual) =>
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);

    /// <summary>Only an emphasized cancel button has the danger class and a visible vector icon.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelEmphasisControlsDangerClassAndIcon(bool emphasizeCancel)
    {
        var dialog = new ConfirmDialog("Confirm", "Proceed?", "Yes", "No", emphasizeCancel);
        try
        {
            dialog.Show();
            Button cancel = dialog.FindControl<Button>("CancelButton")!;
            Path icon = dialog.FindControl<Path>("CancelIcon")!;
            Assert.Equal(emphasizeCancel, cancel.Classes.Contains("actionDanger"));
            Assert.Equal(!emphasizeCancel, cancel.Classes.Contains("actionNeutral"));
            Assert.Contains("actionPrimary", dialog.FindControl<Button>("ConfirmButton")!.Classes);
            Assert.Equal(emphasizeCancel, icon.IsVisible);
            Assert.DoesNotContain("actionDanger", dialog.FindControl<Button>("ConfirmButton")!.Classes);
            Assert.True(dialog.TryFindResource("NfcCloseIconGeometry", out object? geometry));
            Assert.Same(geometry, icon.Data);
            if (emphasizeCancel)
            {
                Assert.Equal(24, icon.Width);
                Assert.Equal(24, icon.Height);
                Assert.Equal(Stretch.Uniform, icon.Stretch);
            }
        }
        finally { dialog.Close(); }
    }

    /// <summary>Each optional tooltip is independent; null leaves that button without a tooltip.</summary>
    [AvaloniaTheory]
    [InlineData(null, null)]
    [InlineData("Embed DXF into the project file.", "Cancel and keep external DXF reference.")]
    [InlineData("Confirm tip", null)]
    [InlineData(null, "Cancel tip")]
    [InlineData("", "")]
    public void OptionalTooltipsRemainCallerSupplied(string? confirmTip, string? cancelTip)
    {
        var dialog = new ConfirmDialog("Confirm", "Proceed?", "Yes", "No",
            confirmTip: confirmTip, cancelTip: cancelTip);
        try
        {
            Assert.Equal(confirmTip, ToolTip.GetTip(dialog.FindControl<Button>("ConfirmButton")!));
            Assert.Equal(cancelTip, ToolTip.GetTip(dialog.FindControl<Button>("CancelButton")!));
        }
        finally { dialog.Close(); }
    }

    /// <summary>The XAML-loader constructor retains empty text, no tips, and no cancel emphasis.</summary>
    [AvaloniaFact]
    public void ParameterlessConstructorLoadsUnconfiguredContent()
    {
        var dialog = new ConfirmDialog();
        try
        {
            Assert.True(string.IsNullOrEmpty(dialog.FindControl<SelectableTextBlock>("TitleText")!.Text));
            Assert.True(string.IsNullOrEmpty(dialog.FindControl<SelectableTextBlock>("MessageText")!.Text));
            Assert.True(string.IsNullOrEmpty(dialog.FindControl<SelectableTextBlock>("ConfirmButtonText")!.Text));
            Assert.True(string.IsNullOrEmpty(dialog.FindControl<SelectableTextBlock>("CancelButtonText")!.Text));
            Assert.Null(ToolTip.GetTip(dialog.FindControl<Button>("ConfirmButton")!));
            Assert.Null(ToolTip.GetTip(dialog.FindControl<Button>("CancelButton")!));
            Assert.DoesNotContain("actionDanger", dialog.FindControl<Button>("CancelButton")!.Classes);
            Assert.False(dialog.FindControl<Path>("CancelIcon")!.IsVisible);
        }
        finally { dialog.Close(); }
    }

    /// <summary>Window layout keeps NFH's values while action geometry uses the shared roles.</summary>
    [AvaloniaFact]
    public void WindowAndActionLayoutUseSharedRoles()
    {
        var dialog = new ConfirmDialog("Confirm", "Proceed?", "Yes", "No");
        try
        {
            dialog.Classes.Add("reducedMotion");
            dialog.Styles.Add(new StyleInclude(ButtonStylesUri) { Source = ButtonStylesUri });
            dialog.Show();
            Assert.Equal(360, dialog.Width);
            Assert.Equal(170, dialog.Height);
            Assert.False(dialog.CanResize);
            Assert.Equal(WindowStartupLocation.CenterOwner, dialog.WindowStartupLocation);
            Border border = Assert.IsType<Border>(dialog.Content);
            Assert.Equal(new Thickness(16), border.Padding);
            Grid grid = Assert.IsType<Grid>(border.Child);
            Assert.Collection(grid.RowDefinitions,
                row => Assert.Equal(GridLength.Auto, row.Height),
                row => Assert.Equal(new GridLength(1, GridUnitType.Star), row.Height),
                row => Assert.Equal(GridLength.Auto, row.Height));
            Assert.Equal(15, dialog.FindControl<SelectableTextBlock>("TitleText")!.FontSize);
            Assert.Equal(new Thickness(0, 8, 0, 0), dialog.FindControl<SelectableTextBlock>("MessageText")!.Margin);
            StackPanel actions = Assert.IsType<StackPanel>(grid.Children[2]);
            Assert.Equal(HorizontalAlignment.Right, actions.HorizontalAlignment);
            Assert.Equal(Orientation.Horizontal, actions.Orientation);
            Assert.Equal(8, actions.Spacing);
            Assert.Equal(new Thickness(0, 14, 0, 0), actions.Margin);
            Assert.All(actions.Children, child =>
            {
                Button button = Assert.IsType<Button>(child);
                Assert.Equal(32, button.MinHeight);
                Assert.Equal(32, button.Height);
                Assert.Equal(new Thickness(14, 0), button.Padding);
                Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
                Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
            });
            StackPanel cancelContent = Assert.IsType<StackPanel>(dialog.FindControl<Button>("CancelButton")!.Content);
            Assert.Equal(8, cancelContent.Spacing);
            Assert.Equal(HorizontalAlignment.Center, cancelContent.HorizontalAlignment);
            Assert.Equal(TextAlignment.Center, dialog.FindControl<SelectableTextBlock>("CancelButtonText")!.TextAlignment);
        }
        finally { dialog.Close(); }
    }

    /// <summary>Mapped brushes follow theme changes through dynamic resources.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MappedBrushesFollowThemeChanges(bool startDark)
    {
        var dialog = new ConfirmDialog("Confirm", "Proceed?", "Yes", "No", true)
        {
            RequestedThemeVariant = startDark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        try
        {
            dialog.Show();
            AssertBrushes();
            dialog.RequestedThemeVariant = startDark ? ThemeVariant.Light : ThemeVariant.Dark;
            AssertBrushes();
        }
        finally { dialog.Close(); }

        void AssertBrushes()
        {
            Assert.True(Application.Current!.TryGetResource("NfcSurfaceBrush", dialog.ActualThemeVariant, out object? surface));
            Assert.True(Application.Current!.TryGetResource("NfcTextStrongBrush", dialog.ActualThemeVariant, out object? title));
            Assert.True(Application.Current!.TryGetResource("NfcTextBrush", dialog.ActualThemeVariant, out object? message));
            Assert.Same(surface, dialog.Background);
            Assert.Same(title, dialog.FindControl<SelectableTextBlock>("TitleText")!.Foreground);
            Assert.Same(message, dialog.FindControl<SelectableTextBlock>("MessageText")!.Foreground);
        }
    }
}
