// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Exports additive form state sheets in both shapes and themes.</summary>
public sealed class FormStylesRenderer(ITestOutputHelper output)
{
    /// <summary>Renders all field states, spinner feedback, closed selector states, and a Fluent comparison.</summary>
    [AvaloniaFact]
    public void RenderForms()
    {
        foreach (string kind in new[] { "textbox", "numericupdown", "combobox" })
            FormSheets.Render(kind, "Soft fill / rest, hover, pressed, keyboard focus, disabled, read-only and error",
                () => FormSheets.FormSection(kind), output);
        FormSheets.Render("forms", "Default Fluent and Core Astra / native controls / same data", () => FormSheets.Comparison(false), output, comparison: true);
    }
}
