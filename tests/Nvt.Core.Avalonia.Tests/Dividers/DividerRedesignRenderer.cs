// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Tests.Theme;
using static Nvt.Core.Avalonia.Tests.Dividers.DividerTestHost;

namespace Nvt.Core.Avalonia.Tests.Dividers;

public sealed partial class DividerStylesRenderer
{
    /// <summary>Exports disclosure structure, each progress thickness, splitter grips, and shape-independent separators.</summary>
    [AvaloniaFact]
    public void RenderRedesignDividers()
    {
        RedesignSheets.Render("NVT_DIVIDER_IMAGES_DIR", "expander", "32 DIP headers / 44 DIP sections / small-radius content containers",
            RedesignExpanders, Restore, output);
        RedesignSheets.Render("NVT_DIVIDER_IMAGES_DIR", "progressbar", "3 / 6 / 10 DIP tracks / determinate, static indeterminate placeholder, complete and disabled",
            RedesignProgress, _ => { }, output);
        RedesignSheets.Render("NVT_DIVIDER_IMAGES_DIR", "gridsplitter", "6 DIP hit targets / vertical 4 x 24 DIP grip / horizontal 24 x 4 DIP grip",
            SplitterSheet, Restore, output);
        RedesignSheets.Render("NVT_DIVIDER_IMAGES_DIR", "separator", "Not affected by shape / Light and Dark / soft and strong / horizontal and vertical",
            () => new StackPanel { Spacing = 24, Children =
            {
                Label("LIGHT", 13, true), SeparatorSheet(),
                new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark,
                    Child = Surface("Dark", "Not affected by shape", SeparatorSheet()) },
            } }, _ => { }, output, single: true);
    }

    private static Control RedesignExpanders()
    {
        Grid matrix = ExpanderSheet();
        foreach (RowDefinition row in matrix.RowDefinitions.Skip(1)) row.Height = new GridLength(132);
        var siblings = new StackPanel { Spacing = 8, Children =
        {
            new Expander { Header = "Input checks", IsExpanded = true, Content = new StackPanel { Spacing = 8, Children =
            {
                new TextBlock { Text = "Choose checks for this run." },
                new CheckBox { Content = "Verify sample files", IsChecked = true },
                new CheckBox { Content = "Check metadata", IsChecked = true },
            } } },
            new Expander { Header = "Output checks", Content = new TextBlock { Text = "Review the generated summary." } },
        } };
        var nested = new Expander { Header = "Validation stages", IsExpanded = true, Content = siblings, Width = 540,
            HorizontalAlignment = HorizontalAlignment.Left, Classes = { "section" } };
        return new StackPanel { Spacing = 24, Children = { matrix, Label("44 DIP section / two nested siblings / 8 DIP sibling spacing", 13, true), nested } };
    }

    private static Control RedesignProgress()
    {
        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*,*,*"), ColumnSpacing = 28 };
        rows.RowDefinitions.Add(new RowDefinition(32, GridUnitType.Pixel));
        foreach ((string title, int column) in new[] { ("STATE", 0), ("THIN / 3 DIP", 1), ("DEFAULT / 6 DIP", 2), ("THICK / 10 DIP", 3) })
            Add(rows, Label(title, 11, true), 0, column);
        string[] names = ["Determinate / 42%", "Indeterminate placeholder", "Completed / 100%", "Disabled / 42%", "Empty / 0%"];
        for (int row = 0; row < names.Length; row++)
        {
            rows.RowDefinitions.Add(new RowDefinition(64, GridUnitType.Pixel));
            Add(rows, Label(names[row], 13), row + 1, 0);
            for (int column = 0; column < 3; column++)
            {
                var bar = new ProgressBar { Value = row == 2 ? 100 : row == 4 ? 0 : 42, IsEnabled = row != 3,
                    IsIndeterminate = row == 1, VerticalAlignment = VerticalAlignment.Center, Classes = { "reducedMotion" } };
                if (column != 1) bar.Classes.Add(column == 0 ? "thin" : "thick");
                Add(rows, bar, row + 1, column + 1);
            }
        }
        return new StackPanel { Spacing = 24, Children = { rows, Label("ProgressBar is noninteractive. Hover, pressed and keyboard focus do not apply.", 13) } };
    }
}
