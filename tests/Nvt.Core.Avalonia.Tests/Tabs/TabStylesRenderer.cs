// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.Tests.Forms;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Tabs;

/// <summary>Exports additive tab state sheets in both shapes and themes.</summary>
public sealed class TabStylesRenderer(ITestOutputHelper output)
{
    /// <summary>Renders selected and unselected states, native strip composition, and a Fluent comparison.</summary>
    [AvaloniaFact]
    public void RenderTabs()
    {
        FormSheets.Render("tabitem", "Soft selected colors / selected indicator / native TabControl strip and page",
            FormSheets.TabSection, output);
        FormSheets.Render("tabs", "Default Fluent and Core Astra / native controls / same data", () => FormSheets.Comparison(true), output, comparison: true);
    }
}
