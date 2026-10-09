// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.Tests.Theme;
using Nvt.Core.Avalonia.Theme;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Choice;

public sealed partial class ChoiceStylesRenderer
{
    /// <summary>Exports transparent-rest choice sheets through the existing renderer into the separate none-mode destination.</summary>
    [AvaloniaFact]
    public void RenderRestFillNoneChoices()
    {
        foreach (bool radio in new[] { false, true })
            RedesignSheets.Render("NVT_RESTFILL_NONE_IMAGES_DIR", radio ? "radiobutton" : "checkbox",
                "32 DIP rows / 20 DIP indicators / 8 DIP label gap / checked, mixed and disabled states",
                () =>
                {
                    var sheet = StateSection(radio);
                    ThemeRestFills.SetRestFill(sheet.Resources, ThemeRestFill.None);
                    return sheet;
                }, Restore, output);
    }
}
