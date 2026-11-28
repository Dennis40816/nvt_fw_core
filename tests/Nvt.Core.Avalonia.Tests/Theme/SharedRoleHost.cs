// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Nvt.Core.Avalonia.Tests.ListMenu;
using Nvt.Core.Avalonia.Theme;

namespace Nvt.Core.Avalonia.Tests.Theme;

internal static class SharedRoleHost
{
    internal static readonly ItemState[] States =
    [
        .. ListMenuTestHost.States,
        new("Press without hover", Pressed: true),
        new("Disabled + press", Hover: true, Pressed: true, Disabled: true),
        new("Disabled + selected + press", Selected: true, Hover: true, Pressed: true, Disabled: true),
    ];

    internal static Window Create(Control content, bool dark, bool square, bool none)
    {
        Window host = RedesignSheets.Create(Surface(content), dark);
        host.Width = 640;
        host.Height = 1000;
        ListMenuTestHost.Include(host, "ButtonStyles");
        host.Classes.Add("reducedMotion");
        ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
        ThemeRestFills.SetRestFill(host.Resources, none ? ThemeRestFill.None : ThemeRestFill.Soft);
        return host;
    }

    internal static Border Surface(Control content)
    {
        var surface = new Border { Padding = new Thickness(16), Child = content };
        surface.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NfcSurfaceBrush"));
        return surface;
    }

    internal static Button Rail(bool toggle = false, bool marked = false, string caption = "Library")
    {
        Button button = toggle ? new ToggleButton() : new Button();
        button.Classes.AddRange(["actionGhost", "flat", "rail"]);
        button.Classes.Set("marked", marked);
        button.Content = new StackPanel
        {
            Children =
            {
                new PathIcon { Width = 16, Height = 16, Data = Geometry.Parse("M0,0 L12,0 L12,12 L0,12 Z") },
                new TextBlock { Text = caption },
            },
        };
        return button;
    }

    internal static Expander Disclosure(bool up = false) => new()
    {
        Header = "Advanced options",
        Content = new TextBlock { Text = "Synthetic detail content" },
        Classes = { "flat" },
        ExpandDirection = up ? ExpandDirection.Up : ExpandDirection.Down,
        IsExpanded = true,
    };

    internal static void SetState(Button button, ItemState state, bool flyout = false)
    {
        if (button is ToggleButton toggle) toggle.IsChecked = state.Selected;
        else button.Classes.Set("active", state.Selected);
        ListMenuTestHost.SetState(button, state);
        ((IPseudoClasses)button.Classes).Set(":flyout-open", flyout);
    }

    internal static StackPanel Column(params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Top };
        panel.Children.AddRange(controls);
        return panel;
    }
}
