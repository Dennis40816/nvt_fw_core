// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.ReportList;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.ReportList;

/// <summary>Exercises the compiled pager trees, accessibility and host-owned visibility.</summary>
public sealed class ReportPagerTemplateTests
{
    /// <summary>The public dictionary contains exactly the two keyed templates for the existing models.</summary>
    [AvaloniaFact]
    public void DictionaryContainsExactlyTheTwoModelTemplates()
    {
        ResourceInclude resources = PagerTemplateTestHost.LoadResources(false);
        Assert.Equal(2, resources.Loaded.Count);
        IDataTemplate paged = PagerTemplateTestHost.FindTemplate(resources, false, false);
        IDataTemplate windowed = PagerTemplateTestHost.FindTemplate(resources, true, false);
        var pagedModel = ReportPagedListViewModel.Create(ReportListTestData.OneRow, 1, ReportListTestData.English);
        var windowedModel = ReportWindowedListViewModel.Create(ReportListTestData.OneRow, 1, ReportListTestData.English);
        Assert.True(paged.Match(pagedModel));
        Assert.False(paged.Match(windowedModel));
        Assert.True(windowed.Match(windowedModel));
        Assert.False(windowed.Match(pagedModel));
        Assert.False(paged.Match(null));
        Assert.False(windowed.Match(null));
    }

    /// <summary>Ports the paged name, polite status and always-visible end-button assertions.</summary>
    /// <param name="chinese">Whether the host supplies Traditional Chinese labels.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PagedBindingsUpdateThroughTheBoundCommandAndKeepTheEndButtonVisible(bool chinese)
    {
        ReportListLabels labels = PagerTemplateTestHost.Labels(chinese);
        var page = ReportPagedListViewModel.Create(PagerTemplateTestHost.Rows(17), 8, labels);
        using PagerTemplateTestHost host = PagerTemplateTestHost.Create(page, false);
        Grid root = host.Root;
        Assert.Equal(2, root.Children.Count);
        TextBlock status = Assert.IsType<TextBlock>(root.Children[0]);
        Button more = Assert.IsType<Button>(root.Children[1]);
        Assert.Equal(new Thickness(0, 8, 0, 0), root.Margin);
        Assert.Equal(10, root.ColumnSpacing);
        Assert.Equal(GridUnitType.Star, root.ColumnDefinitions[0].Width.GridUnitType);
        Assert.Equal(GridUnitType.Auto, root.ColumnDefinitions[1].Width.GridUnitType);
        Assert.Equal(1, Grid.GetColumn(more));
        Assert.Equal(VerticalAlignment.Center, status.VerticalAlignment);
        Assert.Equal(TextWrapping.NoWrap, status.TextWrapping);
        Assert.Null(ToolTip.GetTip(status));
        Assert.Same(page.LoadMoreCommand, more.Command);
        Assert.Equal(8, page.VisibleCount);
        Assert.True(more.IsEffectivelyEnabled);
        AssertPagedControls(page, status, more);

        PagerTemplateTestHost.Execute(more);
        Assert.Equal(16, page.VisibleCount);
        Assert.True(more.IsEffectivelyEnabled);
        AssertPagedControls(page, status, more);

        PagerTemplateTestHost.Execute(more);
        Assert.Equal(17, page.VisibleCount);
        Assert.False(more.IsEffectivelyEnabled);
        Assert.True(more.IsVisible);
        Assert.True(more.IsEffectivelyVisible);
        Assert.Equal(labels.AllItemsLoaded, more.Content);
        AssertPagedControls(page, status, more);
    }

    /// <summary>Ports the windowed bindings and checks both disabled endpoints through the loaded controls.</summary>
    /// <param name="chinese">Whether the host supplies Traditional Chinese labels.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowedBindingsKeepWrappingTooltipNavigationAndDisabledEndpoints(bool chinese)
    {
        var page = ReportWindowedListViewModel.Create(
            PagerTemplateTestHost.Rows(130), 64, PagerTemplateTestHost.Labels(chinese));
        using PagerTemplateTestHost host = PagerTemplateTestHost.Create(page, true);
        Grid root = host.Root;
        Assert.Equal(2, root.Children.Count);
        TextBlock status = Assert.IsType<TextBlock>(root.Children[0]);
        Grid actions = Assert.IsType<Grid>(root.Children[1]);
        Button previous = Assert.IsType<Button>(actions.Children[0]);
        Button next = Assert.IsType<Button>(actions.Children[1]);
        Assert.Equal(default, root.Margin);
        Assert.Equal(2, root.RowDefinitions.Count);
        Assert.All(root.RowDefinitions, row => Assert.Equal(GridUnitType.Auto, row.Height.GridUnitType));
        Assert.Equal(8, root.RowSpacing);
        Assert.Equal(8, actions.ColumnSpacing);
        Assert.Equal(1, Grid.GetRow(actions));
        Assert.Equal(1, Grid.GetColumn(next));
        Assert.All(actions.ColumnDefinitions, column => Assert.Equal(new GridLength(1, GridUnitType.Star), column.Width));
        Assert.Equal(HorizontalAlignment.Center, status.HorizontalAlignment);
        Assert.Equal(TextWrapping.Wrap, status.TextWrapping);
        Assert.Same(page.PreviousPageCommand, previous.Command);
        Assert.Same(page.NextPageCommand, next.Command);
        AssertWindowedControls(page, status, previous, next);
        Assert.False(previous.IsEffectivelyEnabled);
        Assert.True(next.IsEffectivelyEnabled);

        PagerTemplateTestHost.Execute(next);
        Assert.Equal(1, page.PageIndex);
        Assert.Equal(64, page.VisibleCount);
        AssertWindowedControls(page, status, previous, next);
        Assert.True(previous.IsEffectivelyEnabled);
        Assert.True(next.IsEffectivelyEnabled);

        PagerTemplateTestHost.Execute(next);
        Assert.Equal(2, page.PageIndex);
        Assert.Equal(2, page.VisibleCount);
        AssertWindowedControls(page, status, previous, next);
        Assert.True(previous.IsEffectivelyEnabled);
        Assert.False(next.IsEffectivelyEnabled);

        PagerTemplateTestHost.Execute(previous);
        Assert.Equal(1, page.PageIndex);
        AssertWindowedControls(page, status, previous, next);
        page.ShowItemAt(0);
        PagerTemplateTestHost.Render(host.Window);
        Assert.Equal(0, page.PageIndex);
        AssertWindowedControls(page, status, previous, next);
    }

    /// <summary>Tests empty, exact-page and adjacent counts for each frozen host batch size.</summary>
    /// <param name="pageSize">The host's positive page size.</param>
    /// <param name="windowed">Whether to build the fixed-window template.</param>
    [AvaloniaTheory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(8, false)]
    [InlineData(8, true)]
    [InlineData(24, false)]
    [InlineData(24, true)]
    [InlineData(40, false)]
    [InlineData(40, true)]
    [InlineData(64, false)]
    [InlineData(64, true)]
    public void LoadedControlsFollowCountBoundaries(int pageSize, bool windowed)
    {
        int[] counts = [0, 1, pageSize - 1, pageSize, pageSize + 1, (2 * pageSize) - 1, 2 * pageSize, (2 * pageSize) + 1];
        foreach (int count in counts.Distinct())
        {
            object page = PagerTemplateTestHost.CreateModel(count, pageSize, windowed, false);
            using PagerTemplateTestHost host = PagerTemplateTestHost.Create(page, windowed);
            if (page is ReportWindowedListViewModel window)
            {
                TextBlock status = Assert.IsType<TextBlock>(host.Root.Children[0]);
                Grid actions = Assert.IsType<Grid>(host.Root.Children[1]);
                Button previous = Assert.IsType<Button>(actions.Children[0]);
                Button next = Assert.IsType<Button>(actions.Children[1]);
                Assert.Equal(Math.Min(count, pageSize), window.VisibleCount);
                AssertWindowedControls(window, status, previous, next);
                while (window.HasNextPage)
                {
                    PagerTemplateTestHost.Execute(next);
                    AssertWindowedControls(window, status, previous, next);
                }

                Assert.False(next.IsEffectivelyEnabled);
                Assert.True(next.IsVisible);
                Assert.Equal(count == 0 ? 0 : ((count - 1) % pageSize) + 1, window.VisibleCount);
            }
            else
            {
                var prefix = Assert.IsType<ReportPagedListViewModel>(page);
                TextBlock status = Assert.IsType<TextBlock>(host.Root.Children[0]);
                Button more = Assert.IsType<Button>(host.Root.Children[1]);
                Assert.Equal(Math.Min(count, pageSize), prefix.VisibleCount);
                AssertPagedControls(prefix, status, more);
                while (prefix.HasMoreItems)
                {
                    PagerTemplateTestHost.Execute(more);
                    AssertPagedControls(prefix, status, more);
                }

                Assert.Equal(count, prefix.VisibleCount);
                Assert.False(more.IsEffectivelyEnabled);
                Assert.True(more.IsVisible);
            }
        }
    }

    /// <summary>The source caller hides the windowed pager at zero or one page; the template adds no visibility policy.</summary>
    /// <param name="count">The count below, at or above the frozen 64-row boundary.</param>
    /// <param name="visible">Whether the caller exposes navigation.</param>
    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(63, false)]
    [InlineData(64, false)]
    [InlineData(65, true)]
    public void WindowedPagerVisibilityBelongsToTheHost(int count, bool visible)
    {
        var page = ReportWindowedListViewModel.Create(PagerTemplateTestHost.Rows(count), 64, ReportListTestData.English);
        using PagerTemplateTestHost host = PagerTemplateTestHost.Create(page, true);
        Assert.True(host.Root.IsVisible);
        host.Content.Bind(Visual.IsVisibleProperty, new Binding(nameof(page.HasMultiplePages)) { Source = page });
        PagerTemplateTestHost.Render(host.Window);
        Assert.Equal(visible, host.Content.IsVisible);
        Assert.Equal(visible, host.Root.IsEffectivelyVisible);
    }

    /// <summary>Deferred construction keeps the frozen empty status until the bound next/load command runs.</summary>
    /// <param name="windowed">Whether to build the fixed-window template.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredModelsLoadThroughTheCompiledCommandBinding(bool windowed)
    {
        object page = PagerTemplateTestHost.CreateModel(2, 1, windowed, false, false);
        using PagerTemplateTestHost host = PagerTemplateTestHost.Create(page, windowed);
        TextBlock status = Assert.IsType<TextBlock>(host.Root.Children[0]);
        Assert.Equal(windowed ? ReportListTestData.English.NoItems : "Showing 0/2", status.Text);
        Button action = windowed
            ? Assert.IsType<Button>(Assert.IsType<Grid>(host.Root.Children[1]).Children[1])
            : Assert.IsType<Button>(host.Root.Children[1]);
        Assert.True(action.IsEffectivelyEnabled);
        PagerTemplateTestHost.Execute(action);
        Assert.Equal(windowed ? "Showing 1-1 of 2" : "Showing 1/2", status.Text);
        Assert.Equal(status.Text, AutomationProperties.GetName(status));
    }

    /// <summary>Both spacing bindings resolve at the caller and refresh together when that resource changes.</summary>
    [AvaloniaFact]
    public void WindowedSpacingUsesTheHostResourceWithoutOwningADefault()
    {
        var page = ReportWindowedListViewModel.Create(PagerTemplateTestHost.Rows(65), 64, ReportListTestData.English);
        using PagerTemplateTestHost host = PagerTemplateTestHost.Create(page, true);
        Grid actions = Assert.IsType<Grid>(host.Root.Children[1]);
        Assert.False(PagerTemplateTestHost.LoadResources(false).Loaded.ContainsKey("Nvt.ReportList.WindowedSpacing"));
        Assert.Equal(8, host.Root.RowSpacing);
        Assert.Equal(8, actions.ColumnSpacing);
        host.Window.Resources["Nvt.ReportList.WindowedSpacing"] = 12d;
        PagerTemplateTestHost.Render(host.Window);
        Assert.Equal(12, host.Root.RowSpacing);
        Assert.Equal(12, actions.ColumnSpacing);
    }

    /// <summary>Both templates resolve complete role styles without any former host class, including live caption resource changes.</summary>
    /// <param name="windowed">Whether to build the fixed-window template.</param>
    /// <param name="dark">Whether to use the dark palette.</param>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PagerStylesResolveWithoutHostClasses(bool windowed, bool dark)
    {
        AssertNoHostStyles(Application.Current!.Styles);
        using PagerTemplateTestHost host = PagerTemplateTestHost.Create(
            PagerTemplateTestHost.CreateModel(9, 4, windowed, false), windowed, dark: dark);
        AssertNoHostStyles(host.Window.Styles);
        TextBlock status = Assert.IsType<TextBlock>(host.Root.Children[0]);
        PagerTemplateTestHost.AssertCaptionRoles(status);
        Button[] buttons = [.. host.Root.GetVisualDescendants().OfType<Button>()];
        Assert.Equal(windowed ? 2 : 1, buttons.Length);
        Assert.All(buttons, PagerTemplateTestHost.AssertNeutralRole);

        var family = new FontFamily("avares://Nvt.Core.Fonts/Assets/CascadiaMono#Cascadia Mono");
        host.Window.Resources["Nvt.Font.Caption.Family"] = family;
        host.Window.Resources["Nvt.Font.Caption.Size"] = 16d;
        host.Window.Resources["Nvt.Font.Caption.Weight"] = FontWeight.SemiBold;
        PagerTemplateTestHost.Render(host.Window);
        Assert.Equal(family, status.FontFamily);
        Assert.Equal(16d, status.FontSize);
        Assert.Equal(FontWeight.SemiBold, status.FontWeight);
        Assert.All(buttons, PagerTemplateTestHost.AssertNeutralRole);
    }

    private static void AssertNoHostStyles(IStyle style)
    {
        if (style is Style rule)
        {
            string selector = rule.Selector?.ToString() ?? string.Empty;
            foreach (string name in new[] { "semanticAction", "secondary", "captionText" })
            {
                Assert.DoesNotContain($".{name}", selector, StringComparison.Ordinal);
            }
        }

        foreach (IStyle child in style.Children)
        {
            AssertNoHostStyles(child);
        }
    }

    private static void AssertPagedControls(ReportPagedListViewModel page, TextBlock status, Button more)
    {
        AssertStatus(page.PageStatus, status);
        AssertButton(page.LoadMoreLabel, more);
        Assert.Equal(page.HasMoreItems, more.IsEffectivelyEnabled);
    }

    private static void AssertWindowedControls(ReportWindowedListViewModel page, TextBlock status, Button previous, Button next)
    {
        AssertStatus(page.PageStatus, status);
        Assert.Equal(status.Text, ToolTip.GetTip(status));
        AssertButton(page.PreviousPageLabel, previous);
        AssertButton(page.NextPageLabel, next);
        Assert.Equal(page.HasPreviousPage, previous.IsEffectivelyEnabled);
        Assert.Equal(page.HasNextPage, next.IsEffectivelyEnabled);
        Assert.True(previous.IsVisible);
        Assert.True(next.IsVisible);
    }

    private static void AssertStatus(string expected, TextBlock status)
    {
        Assert.Equal(expected, status.Text);
        Assert.Equal(status.Text, AutomationProperties.GetName(status));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
        Assert.Equal(expected, ControlAutomationPeer.CreatePeerForElement(status)!.GetName());
        PagerTemplateTestHost.AssertCaptionRoles(status);
    }

    private static void AssertButton(string expected, Button button)
    {
        Assert.Equal(expected, button.Content);
        Assert.Equal(button.Content, AutomationProperties.GetName(button));
        Assert.Equal(expected, ControlAutomationPeer.CreatePeerForElement(button)!.GetName());
        PagerTemplateTestHost.AssertNeutralRole(button);
    }
}

internal sealed class PagerTemplateTestHost(Window window, ContentControl content) : IDisposable
{
    // Window, controls and their mutable properties are accessed only on the Avalonia test UI thread.
    internal Window Window { get; } = window;

    internal ContentControl Content { get; } = content;

    internal Grid Root => Content.GetVisualDescendants().OfType<Grid>().First();

    internal static ReportListLabels Labels(bool chinese) => chinese ? ReportListTestData.Chinese : ReportListTestData.English;

    internal static string[] Rows(int count) => [.. Enumerable.Range(0, count).Select(index => $"Row {index}")];

    internal static object CreateModel(int count, int pageSize, bool windowed, bool chinese, bool loadInitialPage = true) =>
        windowed
            ? ReportWindowedListViewModel.Create(Rows(count), pageSize, Labels(chinese), loadInitialPage)
            : ReportPagedListViewModel.Create(Rows(count), pageSize, Labels(chinese), loadInitialPage);

    internal static ResourceInclude LoadResources(bool frozen)
    {
        var uri = new Uri(frozen
            ? "avares://Nvt.Core.Avalonia.Tests/ReportList/FrozenPagerTemplates.axaml"
            : "avares://Nvt.Core.Avalonia/ReportList/ReportPagerTemplates.axaml");
        return new ResourceInclude(uri) { Source = uri };
    }

    internal static IDataTemplate FindTemplate(ResourceInclude resources, bool windowed, bool frozen)
    {
        string key = frozen
            ? (windowed ? "HexEditorChangedBlockPagerTemplate" : "ReportPagerTemplate")
            : (windowed ? "Nvt.ReportList.WindowedPagerTemplate" : "Nvt.ReportList.PagedPagerTemplate");
        Assert.True(resources.TryGetResource(key, ThemeVariant.Default, out object? template));
        return Assert.IsAssignableFrom<IDataTemplate>(template);
    }

    internal static PagerTemplateTestHost Create(object model, bool windowed, bool frozen = false, double width = 336, bool dark = false)
    {
        ResourceInclude resources = LoadResources(frozen);
        var content = new ContentControl
        {
            Content = model,
            ContentTemplate = FindTemplate(resources, windowed, frozen),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = new Window
        {
            Width = width,
            Height = 180,
            Content = content,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            FontFamily = Assert.IsType<FontFamily>(Application.Current!.FindResource("NfcUiFontFamily")),
            FontSize = 13,
        };
        window.Resources["Nvt.ReportList.WindowedSpacing"] = 8d;
        var fonts = new Uri("avares://Nvt.Core.Fonts/FontRoles.axaml");
        window.Resources.MergedDictionaries.Add(new ResourceInclude(fonts) { Source = fonts });
        window.Resources.MergedDictionaries.Add(resources);
        window.Styles.Add(new FluentTheme());
        var buttons = new Uri("avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml");
        window.Styles.Add(new StyleInclude(buttons) { Source = buttons });
        window.Classes.Add("reducedMotion");
        try
        {
            window.Show();
            Render(window);
            return new PagerTemplateTestHost(window, content);
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    internal static void Execute(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        Assert.NotNull(button.Command);
        Assert.True(button.Command.CanExecute(button.CommandParameter));
        button.Command.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        (TopLevel.GetTopLevel(button) as Window)!.UpdateLayout();
    }

    internal static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    internal static void AssertCaptionRoles(TextBlock status)
    {
        Assert.Empty(status.Classes);
        Assert.Equal(Assert.IsType<FontFamily>(status.FindResource("Nvt.Font.Caption.Family")), status.FontFamily);
        Assert.Equal(Assert.IsType<double>(status.FindResource("Nvt.Font.Caption.Size")), status.FontSize);
        Assert.Equal(Assert.IsType<FontWeight>(status.FindResource("Nvt.Font.Caption.Weight")), status.FontWeight);
    }

    internal static void AssertNeutralRole(Button button)
    {
        Assert.Equal("actionNeutral", Assert.Single(button.Classes, name => !name.StartsWith(':')));
        Assert.Null(button.Theme);
        Assert.NotNull(button.Template);
        Border border = Assert.Single(button.GetVisualDescendants().OfType<Border>(), item => item.Name == "RoleBorder");
        Assert.Equal(32d, button.Height);
        Assert.Equal(32d, button.MinHeight);
        Assert.Equal(new Thickness(14, 0), button.Padding);
        Assert.Equal(new Thickness(1), button.BorderThickness);
        Assert.Equal(Assert.IsType<CornerRadius>(button.FindResource("NfcPillCornerRadius")), button.CornerRadius);
        Assert.Equal(button.Padding, border.Padding);
        Assert.Equal(button.CornerRadius, border.CornerRadius);
        Assert.Equal(Assert.IsType<FontFamily>(button.FindResource("NfcUiFontFamily")), button.FontFamily);
        Assert.Equal(13d, button.FontSize);
        AssertRoleBrush(button, button.Background, button.IsEffectivelyEnabled ? "NfcSurfaceBrush" : "NfcSurfaceSubtleBrush");
        AssertRoleBrush(button, button.BorderBrush, button.IsEffectivelyEnabled ? "NfcBorderBrush" : "NfcBorderMutedBrush");
        AssertRoleBrush(button, button.Foreground, button.IsEffectivelyEnabled ? "NfcTextBrush" : "NfcTextDisabledBrush");
        Assert.Same(button.Background, border.Background);
        Assert.Same(button.BorderBrush, border.BorderBrush);
    }

    private static void AssertRoleBrush(Button button, IBrush? actual, string resource)
    {
        Assert.True(button.TryFindResource(resource, button.ActualThemeVariant, out object? brush), resource);
        Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
        Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
    }

    public void Dispose() => Window.Close();
}
