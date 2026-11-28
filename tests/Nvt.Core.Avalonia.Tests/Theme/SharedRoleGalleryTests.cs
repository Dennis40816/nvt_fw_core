// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.ListMenu;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;
using static Nvt.Core.Avalonia.Tests.Theme.SharedRoleHost;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Renders synthetic English state sheets, exporting only when an output directory is supplied.</summary>
/// <param name="output">Receives image paths and dimensions.</param>
[Collection(nameof(RestFillIsolation))]
public sealed class SharedRoleGalleryTests(ITestOutputHelper output)
{
    private static readonly double[] _scales = [1, 1.5];

    /// <summary>Exports both themes, shapes and rest-fill modes at normal and fractional DPI.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(SharedUiRolesTests.Configurations), MemberType = typeof(SharedUiRolesTests))]
    public void RenderSharedRoles(bool dark, bool square, bool none)
    {
        string? destination = Nvt.Core.Hosting.HostEnvironment.GetVariable("NVT_ROLES_098_IMAGES_DIR");
        StackPanel content = Build(dark, square, none);
        Window host = Create(content, dark, square, none);
        host.Width = 1080;
        host.Height = 2000;
        try
        {
            ListMenuTestHost.Include(host, "FormStyles");
            ListMenuTestHost.Show(host);
            host.Height = Math.Ceiling(content.DesiredSize.Height + 32);
            Restore(content, host);
            host.Height = Math.Ceiling(content.DesiredSize.Height + 32);
            ListMenuTestHost.Flush(host);
            Assert.All(_scales, scale =>
            {
                host.SetRenderScaling(scale);
                ListMenuTestHost.Flush(host);
                using var frame = host.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal((int)(1080 * scale), frame.PixelSize.Width);
                Assert.All(content.GetVisualDescendants().OfType<Button>(), button =>
                {
                    Point point = button.TranslatePoint(default, host)!.Value;
                    Assert.True(point.X >= 4 && point.Y >= 4, $"{button.GetType().Name}: {point}");
                    Assert.True(point.X + button.Bounds.Width + 4 <= host.Bounds.Width, $"{button.GetType().Name}: {point}, {button.Bounds}");
                    Assert.True(point.Y + button.Bounds.Height + 4 <= host.Bounds.Height, $"{button.GetType().Name}: {point}, {button.Bounds}, host {host.Bounds}");
                });
                if (string.IsNullOrWhiteSpace(destination)) return;
                Directory.CreateDirectory(destination);
                string name = $"roles-{(dark ? "dark" : "light")}-{(square ? "square" : "pill")}-{(none ? "none" : "soft")}-{scale * 100:0}.png";
                frame.Save(System.IO.Path.Combine(destination, name), PngBitmapEncoderOptions.Default);
                output.WriteLine($"{name}: {frame.PixelSize}");
            });
        }
        finally { host.Close(); }
    }

    private static StackPanel Build(bool dark, bool square, bool none)
    {
        var root = Column(
            Label($"CORE / SHARED UI ROLES / {(dark ? "DARK" : "LIGHT")} / {(square ? "SQUARE" : "PILL")} / {(none ? "NONE" : "SOFT")}", 24),
            Label("Synthetic English content / 32 DIP disclosure / 56 x 52 DIP rail / 12 DIP flyout padding"),
            Label("DISCLOSURE ROWS", 16));
        var disclosureRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 32 };
        var down = Column();
        var up = Column();
        ItemState[] disclosureStates = [States[0], States[1], States[2], States[6], States[8]];
        foreach (ItemState state in disclosureStates)
        {
            down.Children.Add(DisclosureSample(state, false));
            up.Children.Add(DisclosureSample(state, true));
        }
        Grid.SetColumn(up, 1);
        disclosureRow.Children.AddRange([down, up]);
        root.Children.Add(disclosureRow);
        root.Children.Add(Label("RAIL / BUTTON + TOGGLEBUTTON / FLAT + MARKED", 16));
        root.Children.Add(Label("Rows: Button / ToggleButton / marked Button / marked ToggleButton", 11));
        var states = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (ItemState state in States.Take(10))
        {
            TextBlock heading = Label(state.Name, 11);
            heading.Height = 28;
            var column = Column(heading);
            column.Width = 90;
            foreach (bool marked in new[] { false, true })
                foreach (bool toggle in new[] { false, true })
                {
                    Button rail = Rail(toggle, marked, toggle ? "Preferences" : "Library");
                    rail.HorizontalAlignment = HorizontalAlignment.Center;
                    rail.Tag = state;
                    column.Children.Add(rail);
                }
            states.Children.Add(column);
        }
        root.Children.Add(states);
        var open = Rail(marked: true, caption: "More");
        open.Tag = new ItemState("Flyout open", Selected: true);
        open.Name = "OpenRail";
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children = { Label("Flyout open / selected mark retained"), open },
        });
        root.Children.Add(Label("PLAIN FLYOUT SURFACES", 16));
        var flyouts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        foreach (bool disabled in new[] { false, true })
        {
            var apply = new Button { Content = "Apply", Classes = { "actionNeutral" }, IsEnabled = !disabled };
            flyouts.Children.Add(new FlyoutPresenter
            {
                Content = Column(Label(disabled ? "Unavailable controls" : "Panel settings", 16),
                    Label("Use Core text and surface colors."),
                    new TextBox { Text = "Sample profile", IsEnabled = !disabled },
                    new CheckBox { Content = "Include details", IsChecked = true, IsEnabled = !disabled }, apply),
            });
        }
        root.Children.Add(flyouts);
        root.Children.Add(Label("Host owns row dividers, surrounding margins, rail collapse and navigation. Reduced motion is enabled.", 11));
        return root;
    }

    private static StackPanel DisclosureSample(ItemState state, bool up)
    {
        Expander expander = Disclosure(up);
        expander.Tag = state;
        expander.Header = $"{state.Name} / {(up ? "Up" : "Down")}";
        return Column(expander);
    }

    private static TextBlock Label(string text, double size = 13)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("NfcTextBrush"));
        return label;
    }

    private static void Restore(Control root, Window host)
    {
        foreach (Expander expander in root.GetVisualDescendants().OfType<Expander>())
        {
            var state = (ItemState)expander.Tag!;
            expander.IsEnabled = !state.Disabled;
            ListMenuTestHost.SetState(Part<ToggleButton>(expander, "ExpanderHeader"), state);
        }
        foreach (Button button in root.GetVisualDescendants().OfType<Button>())
            if (button.Tag is ItemState state) SetState(button, state, button.Name == "OpenRail");
        Button focus = root.GetVisualDescendants().OfType<Button>().First(button => button.Tag is ItemState { Focus: true });
        Assert.True(focus.Focus(NavigationMethod.Tab));
        ListMenuTestHost.Flush(host);
    }
}
