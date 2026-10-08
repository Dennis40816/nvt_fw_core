// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Nvt.Core.Avalonia.Tests.Theme;
using static Nvt.Core.Avalonia.Tests.ListMenu.ListMenuTestHost;

namespace Nvt.Core.Avalonia.Tests.ListMenu;

public sealed partial class ListMenuStylesRenderer
{
    /// <summary>Exports list, popup item, menu and context menu sheets with independent checked states.</summary>
    [AvaloniaFact]
    public void RenderRedesignListsAndMenus()
    {
        string[] names = ["list", "combobox", "menu", "contextmenu"];
        for (int family = 0; family < names.Length; family++)
        {
            int current = family;
            RedesignSheets.Render("NVT_LIST_IMAGES_DIR", names[family],
                "32 DIP colored rows / soft selected states / 2 DIP inset keyboard focus",
                () => RedesignItems(current), Restore, output);
        }
    }

    private static StackPanel RedesignItems(int family)
    {
        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*,*"), ColumnSpacing = 24 };
        rows.RowDefinitions.Add(new RowDefinition(32, GridUnitType.Pixel));
        Add(rows, Label("INTERACTION", 11), 0, 0);
        Add(rows, Label(family >= 2 ? "COMMAND" : "UNSELECTED", 11), 0, 1);
        Add(rows, Label(family >= 2 ? "CHECKED" : "SELECTED", 11), 0, 2);
        ItemState[] interactions = [new("Rest"), new("Hover", Hover: true), new("Pressed", Hover: true, Pressed: true),
            new("Keyboard focus", Focus: true), new("Disabled", Disabled: true)];
        for (int index = 0; index < interactions.Length; index++)
        {
            rows.RowDefinitions.Add(new RowDefinition(56, GridUnitType.Pixel));
            Add(rows, Label(interactions[index].Name, 13), index + 1, 0);
            for (int selected = 0; selected < 2; selected++)
                Add(rows, Sample(Math.Min(family, 2), interactions[index] with { Selected = selected == 1 }), index + 1, selected + 1);
        }
        var stack = new StackPanel { Spacing = 24, Children = { rows } };
        if (family == 0) stack.Children.Add(ListPreview(false));
        else if (family == 1)
        {
            var items = new StackPanel { Spacing = 4, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
            items.Children.Add(Label("ComboBox popup item sequence", 13));
            foreach (string text in new[] { "Overview", "Details", "History", "Attachments" })
                items.Children.Add(new ComboBoxItem { Content = text, Tag = new ItemState(text, Selected: text == "Overview") });
            stack.Children.Add(items);
        }
        else
        {
            if (family == 2)
                stack.Children.Add(new Menu { Items = { new MenuItem { Header = "File", Items = { new MenuItem { Header = "Open sample" } } },
                    new MenuItem { Header = "Edit" }, new MenuItem { Header = "View" } } });
            stack.Children.Add(Label("Popup / checked items, icon spacing and separators", 13));
            Control popup = MenuPreview();
            popup.Width = 360;
            popup.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(popup);
        }
        return stack;
    }
}
