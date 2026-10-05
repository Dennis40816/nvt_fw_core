// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Dialogs;
using Xunit;
using Path = Avalonia.Controls.Shapes.Path;

namespace Nvt.Core.Avalonia.Tests.Dialogs;

/// <summary>Ports NFH's warning-content evidence and characterizes the extracted window lifecycle.</summary>
public sealed class WarningDialogTests
{
    /// <summary>Supplied title and message remain selectable, including empty and multiline content.</summary>
    [AvaloniaTheory]
    [InlineData("Warning", "Please check this change.")]
    [InlineData("", "")]
    [InlineData("警告", "第一行\nSecond line")]
    public void SuppliedTextAppears(string title, string message)
    {
        var dialog = new WarningDialog(title, message);
        try
        {
            dialog.Show();
            Assert.Equal(title, dialog.FindControl<SelectableTextBlock>("TitleText")!.Text);
            Assert.Equal(message, dialog.FindControl<SelectableTextBlock>("MessageText")!.Text);
            Assert.Equal(TextWrapping.Wrap, dialog.FindControl<SelectableTextBlock>("MessageText")!.TextWrapping);
        }
        finally { dialog.Close(); }
    }

    /// <summary>Ports only the title and message assertions from NFH's missing-DXF warning case.</summary>
    [AvaloniaFact]
    public void MissingDxfWarningRetainsSourceContent()
    {
        var dialog = new WarningDialog("DXF not loaded",
            "Export DXF image requires an imported DXF.\nPlease load a DXF file first.");
        Assert.Equal("DXF not loaded", dialog.FindControl<SelectableTextBlock>("TitleText")!.Text);
        Assert.Contains("requires an imported DXF", dialog.FindControl<SelectableTextBlock>("MessageText")!.Text,
            StringComparison.Ordinal);
    }

    /// <summary>OK closes the window and completes the owner's actual modal task without a result.</summary>
    [AvaloniaFact]
    public async Task OkClosesModalDialog()
    {
        var owner = new Window();
        var dialog = new WarningDialog("Warning", "Please check this change.");
        bool closed = false;
        dialog.Closed += (_, _) => closed = true;
        try
        {
            owner.Show();
            Task result = dialog.ShowDialog(owner);
            Assert.False(result.IsCompleted);
            Assert.Same(owner, dialog.Owner);
            Button ok = dialog.FindControl<Button>("OkButton")!;
            Assert.Equal("OK", ok.Content);
            ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(result.IsCompleted);
            await result;
            Assert.True(closed);
            Assert.False(dialog.IsVisible);
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    /// <summary>The XAML-loader constructor retains empty content, OK, and the frozen window settings.</summary>
    [AvaloniaFact]
    public void ParameterlessConstructorPreservesWindowSettings()
    {
        var dialog = new WarningDialog();
        Assert.True(string.IsNullOrEmpty(dialog.FindControl<SelectableTextBlock>("TitleText")!.Text));
        Assert.True(string.IsNullOrEmpty(dialog.FindControl<SelectableTextBlock>("MessageText")!.Text));
        Assert.Equal("OK", dialog.FindControl<Button>("OkButton")!.Content);
        Assert.Equal(360, dialog.Width);
        Assert.Equal(170, dialog.Height);
        Assert.False(dialog.CanResize);
        Assert.Equal(WindowStartupLocation.CenterOwner, dialog.WindowStartupLocation);
        Assert.Equal(new Thickness(16), Assert.IsType<Border>(dialog.Content).Padding);
        Assert.Equal(15, dialog.FindControl<SelectableTextBlock>("TitleText")!.FontSize);
        Assert.Equal(new Thickness(0, 8, 0, 0), dialog.FindControl<SelectableTextBlock>("MessageText")!.Margin);
    }

    /// <summary>The literal warning vector uses the mapped warning brush in either theme.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarningVectorUsesMappedBrush(bool dark)
    {
        var dialog = new WarningDialog("Warning", "Please check this change.")
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        try
        {
            dialog.Show();
            Grid grid = Assert.IsType<Grid>(Assert.IsType<Border>(dialog.Content).Child);
            StackPanel titleRow = Assert.IsType<StackPanel>(grid.Children[0]);
            Path icon = Assert.IsType<Path>(titleRow.Children[0]);
            Assert.Equal(8, titleRow.Spacing);
            Assert.Equal(20, icon.Width);
            Assert.Equal(20, icon.Height);
            Assert.Equal(Stretch.Uniform, icon.Stretch);
            Assert.NotNull(icon.Data);
            Assert.True(icon.Data.Bounds.Width > 0);
            Assert.True(Application.Current!.TryGetResource("NfcWarningAccentBrush", dialog.ActualThemeVariant, out object? warning));
            Assert.Same(warning, icon.Stroke);
            Assert.True(Application.Current!.TryGetResource("NfcSurfaceBrush", dialog.ActualThemeVariant, out object? surface));
            Assert.Same(surface, dialog.Background);
        }
        finally { dialog.Close(); }
    }
}
