// Copyright (c) 2026 Dennis Liu. All rights reserved.

using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Controls.Shapes;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.ListMenu;
using Nvt.Core.Avalonia.Tests.Theme;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Toggle;

/// <summary>Checks the opt-in compact switch against the default switch.</summary>
public sealed class CompactSwitchTests
{
    /// <summary>Compact has a 40 x 20 track, a 32 DIP click area, an equal knob inset at both ends and the same fills.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactChangesSizeOnly(bool dark)
    {
        using var scope = new AccentSelectionScope("NFH", dark);
        foreach (bool native in new[] { false, true })
        {
            foreach (bool enabled in new[] { true, false })
                foreach (bool selected in new[] { false, true })
                {
                    ToggleButton regular = Create(native, selected, enabled), compact = Create(native, selected, enabled);
                    compact.Classes.Add("compact");
                    scope.Show(new StackPanel { Children = { regular, compact } });

                    Assert.Equal((58d, 40d), (regular.Bounds.Width, regular.Bounds.Height));
                    Assert.Equal((46d, 32d), (compact.Bounds.Width, compact.Bounds.Height));
                    Border track = Part<Border>(compact, "ToggleBody");
                    Assert.Equal((40d, 20d), (track.Bounds.Width, track.Bounds.Height));
                    Ellipse knob = Part<Ellipse>(compact, "ToggleKnob");
                    Assert.Equal((14d, 14d), (knob.Bounds.Width, knob.Bounds.Height));
                    var origin = knob.TranslatePoint(default, track)!.Value;
                    Assert.Equal(selected ? 23d : 3d, origin.X, 3);
                    Assert.Equal(3d, origin.Y, 3);
                    Border ring = Part<Border>(compact, "ToggleFocusRing");
                    Assert.Equal((48d, 28d), (ring.Width, ring.Height));
                    Assert.Equal(ColorOf(Part<Border>(regular, "ToggleBody").Background), ColorOf(track.Background));
                    Assert.Equal(ColorOf(Part<Ellipse>(regular, "ToggleKnob").Fill), ColorOf(knob.Fill));
                }
        }
    }

    /// <summary>A shown switch can take and drop the class, toggle in both directions and center its keyboard focus ring.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactFollowsLiveChanges(bool native)
    {
        using var scope = new AccentSelectionScope("NFH", dark: true);
        ToggleButton control = Create(native, selected: false, enabled: true);
        Window host = scope.Show(new StackPanel { Children = { control } });
        Grid moving = Part<Grid>(control, "PART_MovingKnobs");
        moving.Transitions = null;
        if (control is ToggleSwitch toggle)
            toggle.KnobTransitions = null!;

        control.Classes.Add("compact");
        ListMenuTestHost.Flush(host);
        Assert.Equal((46d, 32d), (control.Bounds.Width, control.Bounds.Height));
        Border track = Part<Border>(control, "ToggleBody");
        Ellipse knob = Part<Ellipse>(control, "ToggleKnob");
        foreach (bool selected in new[] { true, false, true })
        {
            control.IsChecked = selected;
            ListMenuTestHost.Flush(host);
            Assert.Equal(selected ? 23d : 3d, knob.TranslatePoint(default, track)!.Value.X, 3);
        }

        Assert.True(control.Focus(NavigationMethod.Tab));
        ListMenuTestHost.Flush(host);
        Border ring = Part<Border>(control, "ToggleFocusRing");
        Assert.True(ring.IsEffectivelyVisible);
        Assert.Equal((48d, 28d), (ring.Bounds.Width, ring.Bounds.Height));
        Point center = ring.TranslatePoint(new Point(24, 14), track)!.Value;
        Assert.Equal(20d, center.X, 3);
        Assert.Equal(10d, center.Y, 3);

        control.IsChecked = false;
        control.Classes.Remove("compact");
        ListMenuTestHost.Flush(host);
        Assert.Equal((58d, 40d), (control.Bounds.Width, control.Bounds.Height));
        Assert.Equal((52d, 28d), (track.Bounds.Width, track.Bounds.Height));
    }

    private static ToggleButton Create(bool native, bool selected, bool enabled)
    {
        ToggleButton control = native ? new ToggleSwitch() : new ToggleButton { Classes = { "toggleSwitch" } };
        control.IsChecked = selected;
        control.IsEnabled = enabled;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        return control;
    }

    private static T Part<T>(Control owner, string name)
        where T : Control => owner.GetVisualDescendants().OfType<T>().Single(part => part.Name == name);

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
