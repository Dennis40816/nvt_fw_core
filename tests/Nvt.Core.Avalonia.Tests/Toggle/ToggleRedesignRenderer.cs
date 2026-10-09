// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Nvt.Core.Avalonia.Tests.Theme;
using static Nvt.Core.Avalonia.Tests.Toggle.ToggleTestHost;

namespace Nvt.Core.Avalonia.Tests.Toggle;

public sealed partial class ToggleStylesRenderer
{
    /// <summary>Exports native and role switches plus the existing toggle roles in every shape and theme.</summary>
    [AvaloniaFact]
    public void RenderRedesignToggles()
    {
        RedesignSheets.Render("NVT_TOGGLE_IMAGES_DIR", "switch", "52 x 28 DIP tracks / round 22 DIP knobs / 24 DIP travel / shape-aware focus",
            () => RedesignRows(["toggleSwitch", "nativeSwitch"]), Restore, output);
        RedesignSheets.Render("NVT_TOGGLE_IMAGES_DIR", "toggle", "Segment / tab / icon / soft filter / persistent selection and distinct pressed feedback",
            () => RedesignRows(["toggleSegment", "toggleTab", "toggleIcon", "toggleSoft"]), Restore, output);
    }

    private static Grid RedesignRows(string[] roles)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180," + string.Join(',', roles.Select(_ => "*"))), ColumnSpacing = 16 };
        grid.RowDefinitions.Add(new RowDefinition(32, GridUnitType.Pixel));
        Add(grid, Label("STATE", 11), 0, 0);
        for (int column = 0; column < roles.Length; column++) Add(grid, Label(roles[column], 13), 0, column + 1);
        ToggleState[] states = [.. States, new("Checked + keyboard focus", Checked: true, Focus: true)];
        for (int row = 0; row < states.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(64, GridUnitType.Pixel));
            Add(grid, Label(states[row].Name, 13), row + 1, 0);
            for (int column = 0; column < roles.Length; column++)
                Add(grid, roles[column] == "toggleIcon" ? Icon(states[row]) : Sample(roles[column], states[row], "Overview"), row + 1, column + 1);
        }
        return grid;
    }
}
