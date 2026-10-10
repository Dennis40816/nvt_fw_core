// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.Tests.Choice;
using Nvt.Core.Avalonia.Tests.Dividers;
using Nvt.Core.Avalonia.Tests.Forms;
using Nvt.Core.Avalonia.Tests.ListMenu;
using Nvt.Core.Avalonia.Tests.Tabs;
using Nvt.Core.Avalonia.Tests.TextStyles;
using Nvt.Core.Avalonia.Tests.Toggle;
using Nvt.Core.Avalonia.Theme;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Reuses existing renderer methods to export all 65 reference sheets in explicit Soft mode.</summary>
/// <param name="output">Receives image dimensions and byte counts.</param>
[Collection(nameof(RestFillIsolation))]
public sealed class RestFillSheetsRenderer(ITestOutputHelper output)
{
    /// <summary>Preserves colliding dropdown-row and closed-field filenames in separate reference-set directories.</summary>
    [AvaloniaFact]
    public void RenderRestFillSoftReferences()
    {
        string? directory = Environment.GetEnvironmentVariable("NVT_RESTFILL_SOFT_IMAGES_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        string[] variables = ["NVT_CHOICE_IMAGES_DIR", "NVT_DIVIDER_IMAGES_DIR", "NVT_LIST_IMAGES_DIR", "NVT_TOGGLE_IMAGES_DIR", "NVT_FORMS_IMAGES_DIR"];
        string?[] values = variables.Select(Environment.GetEnvironmentVariable).ToArray();
        var resources = Application.Current!.Resources;
        var saved = resources.MergedDictionaries.ToArray();
        try
        {
            ThemeRestFills.SetRestFill(resources, ThemeRestFill.Soft);
            foreach (string variable in variables)
                Environment.SetEnvironmentVariable(variable, System.IO.Path.Combine(directory, variable == "NVT_FORMS_IMAGES_DIR" ? "forms" : "redesign"));
            new ChoiceStylesRenderer(output).RenderRedesignChoices();
            new DividerStylesRenderer(output).RenderRedesignDividers();
            new ListMenuStylesRenderer(output).RenderRedesignListsAndMenus();
            new ToggleStylesRenderer(output).RenderRedesignToggles();
            new FormStylesRenderer(output).RenderForms();
            new TabStylesRenderer(output).RenderTabs();
            new TextStylesRenderer(output).RenderTextStyles();
        }
        finally
        {
            for (int index = 0; index < variables.Length; index++) Environment.SetEnvironmentVariable(variables[index], values[index]);
            resources.MergedDictionaries.Clear();
            foreach (var dictionary in saved) resources.MergedDictionaries.Add(dictionary);
        }
    }
}
