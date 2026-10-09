// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.Tests.Theme;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Choice;

public sealed partial class ChoiceStylesRenderer
{
    /// <summary>Exports individual choice sheets in both shapes and themes using the existing state matrix.</summary>
    [AvaloniaFact]
    public void RenderRedesignChoices()
    {
        foreach (bool radio in new[] { false, true })
            RedesignSheets.Render("NVT_CHOICE_IMAGES_DIR", radio ? "radiobutton" : "checkbox",
                "32 DIP rows / 20 DIP indicators / 8 DIP label gap / checked, mixed and disabled states",
                () => StateSection(radio), Restore, output);
    }
}
