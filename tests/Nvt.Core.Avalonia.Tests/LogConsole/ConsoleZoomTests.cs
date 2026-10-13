// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleZoomTests
{
    private const RawInputModifiers Ctrl = RawInputModifiers.Control;
    private static readonly double[] _expectedChanges = [1.3, 3.0, 0.8, 1.0];

    private static void Press(ConsoleScrollScene scene, Key key, RawInputModifiers modifiers = Ctrl)
    {
        scene.View.Focus(NavigationMethod.Directional);
        scene.Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        scene.Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        ConsoleInteractionWindow.Pump(scene.Window);
    }

    private static void Wheel(ConsoleScrollScene scene, double delta, RawInputModifiers modifiers = Ctrl)
    {
        scene.Window.MouseWheel(new Point(300, 100), new Vector(0, delta), modifiers);
        ConsoleInteractionWindow.Pump(scene.Window);
    }

    private static double[] LabelSizes(ConsoleScrollScene scene)
        => Rows(scene).First().GetVisualDescendants().OfType<TextBlock>().Select(label => label.FontSize).ToArray();

    private static IEnumerable<ConsoleRowPresenter> Rows(ConsoleScrollScene scene)
        => scene.Host.Children.OfType<ConsoleRowPresenter>();

    [AvaloniaFact]
    public void Zoom_Default_IsNormalSize()
    {
        using var scene = new ConsoleScrollScene(200);
        Assert.Equal(1.0, scene.View.Zoom);
        Assert.Equal(20, scene.Host.ScrollSize.Height);
        Assert.Equal(200 * 20, scene.Host.Extent.Height);
    }

    [AvaloniaFact]
    public void Zoom_Set_ScalesRowHeightTextAndExtent()
    {
        using var scene = new ConsoleScrollScene(200);
        var normal = Rows(scene).First().MessageText.FontSize;
        var labels = LabelSizes(scene);
        Assert.NotEmpty(labels);
        scene.View.Zoom = 1.5;
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.Equal(30, scene.Host.ScrollSize.Height);
        Assert.Equal(200 * 30, scene.Host.Extent.Height);
        Assert.All(Rows(scene), row =>
        {
            Assert.Equal(30, row.Bounds.Height);
            Assert.Equal(normal * 1.5, row.MessageText.FontSize, 3);
        });
        Assert.Equal(labels.Select(size => Math.Round(size * 1.5, 2)), LabelSizes(scene));
        scene.View.ResetZoom();
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.Equal(20, scene.Host.ScrollSize.Height);
        Assert.Equal(200 * 20, scene.Host.Extent.Height);
        Assert.All(Rows(scene), row => Assert.Equal(20, row.Bounds.Height));
        Assert.Equal(normal, Rows(scene).First().MessageText.FontSize, 3);
        Assert.Equal(labels, LabelSizes(scene));
    }

    [AvaloniaFact]
    public void Gesture_CtrlPlusAndCtrlMinus_ChangeByOneStep()
    {
        using var scene = new ConsoleScrollScene(50);
        Press(scene, Key.OemPlus);
        Assert.Equal(1.1, scene.View.Zoom, 3);
        Press(scene, Key.Add);
        Assert.Equal(1.2, scene.View.Zoom, 3);
        Press(scene, Key.OemPlus, Ctrl | RawInputModifiers.Shift);
        Assert.Equal(1.3, scene.View.Zoom, 3);
        Press(scene, Key.OemMinus);
        Assert.Equal(1.2, scene.View.Zoom, 3);
        Press(scene, Key.Subtract);
        Assert.Equal(1.1, scene.View.Zoom, 3);
    }

    [AvaloniaFact]
    public void Gesture_CtrlZero_RestoresNormalSize()
    {
        using var scene = new ConsoleScrollScene(50);
        scene.View.Zoom = 2.0;
        Press(scene, Key.D0);
        Assert.Equal(1.0, scene.View.Zoom);
        scene.View.Zoom = 0.8;
        Press(scene, Key.NumPad0);
        Assert.Equal(1.0, scene.View.Zoom);
    }

    [AvaloniaFact]
    public void Gesture_WithoutCtrlOrWithAlt_DoesNotZoom()
    {
        using var scene = new ConsoleScrollScene(50);
        Press(scene, Key.OemPlus, RawInputModifiers.None);
        Press(scene, Key.OemMinus, RawInputModifiers.Shift);
        Press(scene, Key.OemPlus, Ctrl | RawInputModifiers.Alt);
        Assert.Equal(1.0, scene.View.Zoom);
    }

    [AvaloniaFact]
    public void Gesture_Repeated_StopsAtTheLimitsWithoutExtraEvents()
    {
        using var scene = new ConsoleScrollScene(50);
        var changes = new List<double>();
        scene.View.ZoomChanged += (_, zoom) => changes.Add(zoom);
        for (var press = 0; press < 30; press++) Press(scene, Key.OemPlus);
        Assert.Equal(ConsoleListView.MaximumZoom, scene.View.Zoom);
        Assert.Equal(20, changes.Count);
        for (var press = 0; press < 30; press++) Press(scene, Key.OemMinus);
        Assert.Equal(ConsoleListView.MinimumZoom, scene.View.Zoom);
        Assert.Equal(20 + 22, changes.Count);
        Assert.Equal(ConsoleListView.MinimumZoom, changes[^1]);
    }

    [AvaloniaFact]
    public void Wheel_WithCtrl_ZoomsAndWithoutCtrlDoesNot()
    {
        using var scene = new ConsoleScrollScene(200);
        var point = new Point(300, 100);
        scene.Window.MouseWheel(point, new Vector(0, -1));
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.Equal(1.0, scene.View.Zoom);
        scene.Window.MouseWheel(point, new Vector(0, 1), Ctrl);
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.Equal(1.1, scene.View.Zoom, 3);
        scene.Window.MouseWheel(point, new Vector(0, -1), Ctrl);
        scene.Window.MouseWheel(point, new Vector(0, -1), Ctrl);
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.Equal(0.9, scene.View.Zoom, 3);
    }

    [AvaloniaFact]
    public void Property_OutOfRangeValues_AreClampedAndEventsReportEachChange()
    {
        using var scene = new ConsoleScrollScene(50);
        var changes = new List<double>();
        scene.View.ZoomChanged += (_, zoom) => changes.Add(zoom);
        scene.View.Zoom = 1.3;
        scene.View.Zoom = 1.3;
        scene.View.Zoom = 10;
        scene.View.Zoom = 0.1;
        scene.View.Zoom = double.NaN;
        Assert.Equal(_expectedChanges, changes);
        Assert.Equal(1.0, scene.View.Zoom);
    }

    [AvaloniaFact]
    public void Step_FromBetweenStops_MovesToTheNextStop()
    {
        using var scene = new ConsoleScrollScene(50);
        scene.View.Zoom = 1.05;
        scene.View.ZoomIn();
        Assert.Equal(1.1, scene.View.Zoom, 3);
        scene.View.Zoom = 1.05;
        scene.View.ZoomOut();
        Assert.Equal(1.0, scene.View.Zoom, 3);
        scene.View.ResetZoom();
        scene.View.ZoomOut();
        Assert.Equal(0.9, scene.View.Zoom, 3);
    }

    [AvaloniaFact]
    public void Zoom_WhilePaused_KeepsTheFirstVisibleRowAndItsInset()
    {
        using var scene = new ConsoleScrollScene(200);
        scene.Pause();
        scene.Host.Offset = new(0, 410);
        ConsoleInteractionWindow.Pump(scene.Window);
        var before = scene.Host.CaptureAnchor(false)!;
        Assert.Equal(new ConsoleRowId(21), before.RowId);
        Assert.Equal(10, before.PixelOffset);
        scene.View.Zoom = 1.5;
        ConsoleInteractionWindow.Pump(scene.Window);
        var larger = scene.Host.CaptureAnchor(false)!;
        Assert.Equal(before.RowId, larger.RowId);
        Assert.Equal(15, larger.PixelOffset, 3);
        Assert.Equal(20 * 30 + 15, scene.Host.Offset.Y, 3);
        scene.View.ResetZoom();
        ConsoleInteractionWindow.Pump(scene.Window);
        var restored = scene.Host.CaptureAnchor(false)!;
        Assert.Equal(before.RowId, restored.RowId);
        Assert.Equal(10, restored.PixelOffset, 3);
        Assert.Equal(200 * 20, scene.Host.Extent.Height);
    }

    [AvaloniaFact]
    public void Zoom_LargeList_RealizesOnlyTheRowsInTheViewport()
    {
        using var scene = new ConsoleScrollScene(1000);
        var normal = Rows(scene).Count();
        scene.View.Zoom = ConsoleListView.MaximumZoom;
        ConsoleInteractionWindow.Pump(scene.Window);
        var zoomed = Rows(scene).Count();
        Assert.InRange(zoomed, 1, normal);
        Assert.All(Rows(scene), row => Assert.Equal(60, row.Bounds.Height));
        Assert.Equal(1000 * 60, scene.Host.Extent.Height);
    }

    [AvaloniaFact]
    public void Zoom_ThemeFontChange_IsMultipliedAgain()
    {
        using var scene = new ConsoleScrollScene(50);
        scene.View.Zoom = 2.0;
        ConsoleInteractionWindow.Pump(scene.Window);
        scene.Window.Resources["Nvt.Font.Body.Size"] = 20.0;
        scene.Window.Resources["Nvt.Font.MonoCaption.Size"] = 20.0;
        scene.Window.Resources["Nvt.Console.List.RowHeight"] = 24.0;
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.Equal(40, Rows(scene).First().MessageText.FontSize, 3);
        Assert.Equal(48, scene.Host.ScrollSize.Height);
        Assert.Equal(50 * 48, scene.Host.Extent.Height);
        scene.View.ResetZoom();
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.Equal(20, Rows(scene).First().MessageText.FontSize, 3);
        Assert.Equal(24, scene.Host.ScrollSize.Height);
    }

    [AvaloniaFact]
    public void Wheel_Fractions_AddUpToOneStepPerNotch()
    {
        using var scene = new ConsoleScrollScene(200);
        for (var part = 0; part < 3; part++) Wheel(scene, 0.25);
        Assert.Equal(1.0, scene.View.Zoom);
        Wheel(scene, 0.25);
        Assert.Equal(1.1, scene.View.Zoom, 3);
        // A change of direction drops the part of a notch that was not finished.
        Wheel(scene, 0.5);
        Wheel(scene, -0.5);
        Assert.Equal(1.1, scene.View.Zoom, 3);
        Wheel(scene, -0.5);
        Assert.Equal(1.0, scene.View.Zoom, 3);
    }

    [AvaloniaFact]
    public void Wheel_WithShiftOrAlt_DoesNotZoom()
    {
        using var scene = new ConsoleScrollScene(200);
        Wheel(scene, 1, Ctrl | RawInputModifiers.Shift);
        Wheel(scene, 1, RawInputModifiers.Shift);
        Wheel(scene, 1, Ctrl | RawInputModifiers.Alt);
        Assert.Equal(1.0, scene.View.Zoom);
    }

    [AvaloniaFact]
    public void Zoom_FollowingList_StaysAtTheEnd()
    {
        using var scene = new ConsoleScrollScene(200);
        Assert.True(scene.Host.AtEnd);
        scene.View.Zoom = 2.0;
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.True(scene.Host.AtEnd);
        scene.View.Zoom = 0.8;
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.True(scene.Host.AtEnd);
    }

    [AvaloniaFact]
    public void Zoom_ExpandedRowOutOfView_ScalesTheHeightEstimate()
    {
        using var content = new InMemoryLogTextContent(string.Join(' ', Enumerable.Repeat("word", 800)));
        using var scene = new ConsoleScrollScene(100, content);
        scene.View.ViewState = scene.View.ViewState with { ExpandedIds = [new ConsoleRowId(1)] };
        ConsoleInteractionWindow.Pump(scene.Window);
        scene.Host.ScrollToEnd();
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.DoesNotContain(Rows(scene), row => row.RowId == new ConsoleRowId(1));
        var normal = scene.Host.Extent.Height - 99 * 20;
        Assert.True(normal > 20, "The expanded row must be taller than a collapsed row.");
        scene.View.Zoom = 2.0;
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.DoesNotContain(Rows(scene), row => row.RowId == new ConsoleRowId(1));
        var zoomed = scene.Host.Extent.Height - 99 * 40;
        Assert.True(zoomed >= normal * 1.9, $"{zoomed} must be about twice {normal}.");
    }

    [AvaloniaFact]
    public void Zoom_AssignedBeforeTheTemplateExists_AppliesWithTheTemplate()
    {
        var view = new ConsoleListView { Zoom = 2.0 };
        var window = ConsoleInteractionWindow.Create(view);
        try
        {
            Assert.Equal(40, ConsoleInteractionWindow.Host(view).ScrollSize.Height);
            Assert.Equal(2.0, view.Zoom);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Zoom_AfterDetachAndAttach_KeepsTheValueAndTheGestures()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        scene.View.Zoom = 1.5;
        ConsoleInteractionWindow.Pump(scene.Window);
        scene.Detach();
        scene.Attach();
        Assert.Equal(1.5, scene.View.Zoom);
        Assert.Equal(30, ConsoleInteractionWindow.Host(scene.View).ScrollSize.Height);
        scene.Key(Key.OemPlus, Ctrl);
        Assert.Equal(1.6, scene.View.Zoom, 3);
        Assert.Equal(32, ConsoleInteractionWindow.Host(scene.View).ScrollSize.Height, 3);
    }
}
