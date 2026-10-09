// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.Tests.Theme;
using Nvt.Core.Avalonia.Theme;

namespace Nvt.Core.Avalonia.Tests.Dividers;

public sealed partial class DividerStylesRenderer
{
    /// <summary>Exports transparent-rest headers with every existing Expander state and nested example.</summary>
    [AvaloniaFact]
    public void RenderRestFillNoneExpanders() =>
        RedesignSheets.Render("NVT_RESTFILL_NONE_IMAGES_DIR", "expander", "32 DIP headers / 44 DIP sections / small-radius content containers",
            () =>
            {
                var sheet = RedesignExpanders();
                ThemeRestFills.SetRestFill(sheet.Resources, ThemeRestFill.None);
                return sheet;
            }, Restore, output);
}
