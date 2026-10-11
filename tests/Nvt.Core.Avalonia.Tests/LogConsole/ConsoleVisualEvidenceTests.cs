// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using static Nvt.Core.Avalonia.Tests.LogConsole.ConsoleAccessibilitySupport;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleVisualEvidenceTests
{
    private readonly ITestOutputHelper _output;
    public ConsoleVisualEvidenceTests(ITestOutputHelper output) => _output = output;

    [AvaloniaTheory]
    [InlineData(false, false, "text", 4.5)]
    [InlineData(false, true, "text", 4.5)]
    [InlineData(true, false, "text", 4.5)]
    [InlineData(true, true, "text", 4.5)]
    [InlineData(false, false, "icon", 3)]
    [InlineData(false, true, "icon", 3)]
    [InlineData(true, false, "icon", 3)]
    [InlineData(true, true, "icon", 3)]
    [InlineData(false, false, "focus", 3)]
    [InlineData(false, true, "focus", 3)]
    [InlineData(true, false, "focus", 3)]
    [InlineData(true, true, "focus", 3)]
    [InlineData(false, false, "disabled", 3)]
    [InlineData(false, true, "disabled", 3)]
    [InlineData(true, false, "disabled", 3)]
    [InlineData(true, true, "disabled", 3)]
    public void Contrast_ResolvedRowStates_MeetsCoreFloor(bool dark, bool square, string group, double floor)
    {
        var minimum = ConsoleVisualEvidenceSupport.Minimum(dark, square, group);
        _output.WriteLine($"{(dark ? "Dark" : "Light")}/{(square ? "Square" : "Pill")}: {group} minimum {minimum:F6}:1");
        AssertFloor(minimum, group, floor);
    }
    private static void AssertFloor(double minimum, string group, double floor)
        => Assert.True(minimum >= floor, $"{group} minimum {minimum:F6}:1 < {floor}:1");

    [AvaloniaFact]
    public void Contrast_HostLowContrastOverride_IsRejectedByFloor()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        scene.Window.Resources["NfcTextBrush"] = scene.View.Background;
        ConsoleInteractionWindow.Pump(scene.Window);
        var ratio = global::Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost.Contrast(
            global::Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost.ColorOf(scene.Row(1).MessageText.CurrentLayout!.Foreground),
            global::Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost.ColorOf(scene.View.Background));
        Assert.Throws<global::Xunit.Sdk.TrueException>(() => AssertFloor(ratio, "text", 4.5));
    }

    [AvaloniaFact]
    public void FocusRing_OutsetHostOverride_IsRejectedByViewportCheck()
    {
        using var scene = new ConsoleScrollScene(100);
        scene.Window.Resources["Nvt.Controls.InsetFocusRingMargin"] = Resource<Thickness>(scene.View, "Nvt.Controls.FocusRingMargin");
        Assert.Throws<global::Xunit.Sdk.TrueException>(() => ConsoleVisualEvidenceSupport.AssertRingInside(scene, false));
    }

    [AvaloniaFact]
    public void HitTest_RowPadding_SelectsRow()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        var point = scene.Row(1).TranslatePoint(new Point(4, 10), scene.Window)!.Value;
        scene.Window.MouseDown(point, MouseButton.Left); scene.Window.MouseUp(point, MouseButton.Left);
        Assert.Equal([1L], scene.Controller.ViewState.Selection);
    }
    [AvaloniaFact]
    public void HitTest_ControlClickLink_OpensWithoutSelectingRow()
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        scene.Click(1, RawInputModifiers.Control);
        Assert.Single(scene.Adapters.Opened); Assert.Empty(scene.Controller.ViewState.Selection);
    }
    [AvaloniaFact]
    public void ArrowTarget_DenseRow_PinsHeightBelowExistingControlSize()
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        var row = scene.Row(1);
        const double ApprovedTargetHeight = 20;
        const double ExistingControlTargetHeight = 24;
        var minimum = Resource<double>(row, "NfcSpace24");
        var width = Resource<double>(row, "Nvt.Console.List.ArrowWidth");
        _output.WriteLine($"Expand target {width} x {row.Bounds.Height} DIP; existing controls use {minimum} DIP; height shortfall {minimum - row.Bounds.Height} DIP.");
        Assert.True(width >= minimum);
        Assert.Equal(ExistingControlTargetHeight, minimum);
        Assert.Equal(ApprovedTargetHeight, row.Bounds.Height);
    }
    [AvaloniaTheory]
    [InlineData(0, 0, true)]
    [InlineData(23.999, 0, true)]
    [InlineData(0, 19.999, true)]
    [InlineData(23.999, 19.999, true)]
    [InlineData(-0.001, 10, false)]
    [InlineData(24, 10, false)]
    [InlineData(12, -0.001, false)]
    [InlineData(12, 20, false)]
    public void ArrowTarget_PointerBoundary_TogglesOnlyInsideColumn(double x, double y, bool accepted)
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        var row = scene.Row(1);
        var columnLeft = row.CountLabel.Bounds.Right;
        var point = row.TranslatePoint(new Point(columnLeft + x, y), scene.Window)!.Value;

        scene.Window.MouseDown(point, MouseButton.Left);
        scene.Window.MouseUp(point, MouseButton.Left);

        _output.WriteLine($"Arrow-column pointer ({x}, {y}) DIP from ({columnLeft}, 0): accepted {accepted}");
        Assert.Equal(accepted, scene.Controller.ViewState.ExpandedIds.Contains(row.RowId!.Value));
    }
    [AvaloniaTheory]
    [InlineData(19.999, false)]
    [InlineData(20, true)]
    [InlineData(30, true)]
    public void ArrowTarget_ExpandedRow_TogglesOnlyOnFirstLine(double y, bool stillExpanded)
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        scene.Key(Key.Enter);
        var row = scene.Row(1);
        Assert.Contains(row.RowId!.Value, scene.Controller.ViewState.ExpandedIds);
        var point = row.TranslatePoint(new Point(row.CountLabel.Bounds.Right + 12, y), scene.Window)!.Value;

        scene.Window.MouseDown(point, MouseButton.Left);
        scene.Window.MouseUp(point, MouseButton.Left);

        Assert.Equal(stillExpanded, scene.Controller.ViewState.ExpandedIds.Contains(row.RowId!.Value));
    }
    [AvaloniaFact]
    public void HitTest_FeedbackAndFocusRing_DoNotInterceptPointer()
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        scene.View.Focus(NavigationMethod.Directional);
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.False(scene.Row(1).Children[0].IsHitTestVisible);
        Assert.False(Ring(scene.Row(1)).IsHitTestVisible);
    }
    [AvaloniaTheory]
    [InlineData(1, 20, 2)]
    [InlineData(1.25, 25, 2.5)]
    [InlineData(1.5, 30, 3)]
    [InlineData(2, 40, 4)]
    public void Geometry_RenderScaling_ReportsDevicePixels(double scale, double expectedHeight, double expectedStroke)
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        scene.Window.SetRenderScaling(scale);
        scene.View.Focus(NavigationMethod.Directional);
        ConsoleInteractionWindow.Pump(scene.Window);
        var rowPixels = scene.Row(1).Bounds.Height * scene.Window.RenderScaling;
        var strokePixels = Ring(scene.Row(1)).BorderThickness.Left * scene.Window.RenderScaling;
        _output.WriteLine($"Scaling {scale}: row {rowPixels} px; ring stroke {strokePixels} px; outward raster envelope {Math.Ceiling(strokePixels)} px.");
        Assert.Equal(expectedHeight, rowPixels); Assert.Equal(Math.Round(rowPixels), rowPixels);
        Assert.Equal(expectedStroke, strokePixels); Assert.InRange(Math.Ceiling(strokePixels) - strokePixels, 0, 0.5);
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusRing_ViewportEdge_RemainsInsideVisibleBounds(bool last)
    {
        using var scene = new ConsoleScrollScene(100);
        ConsoleVisualEvidenceSupport.AssertRingInside(scene, last);
    }
}
