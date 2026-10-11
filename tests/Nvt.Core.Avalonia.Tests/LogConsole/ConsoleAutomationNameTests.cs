// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;
using static Nvt.Core.Avalonia.Tests.LogConsole.ConsoleAccessibilitySupport;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleAutomationNameTests
{
    [AvaloniaFact]
    public void Peer_RealizedRow_ReportsListItemAndProjectionPosition()
    {
        using var scene = new ConsoleInteractionScene(message: "message");
        var row = scene.Row(3);
        var peer = Peer(row);
        Assert.Equal(AutomationControlType.ListItem, peer.GetAutomationControlType());
        Assert.Equal("Info, 12:00:00.000, app, message", peer.GetName());
        Assert.Equal(3, peer.PositionInSet); Assert.Equal(4, peer.SizeOfSet);
        Assert.Equal(peer.PositionInSet, AutomationProperties.GetPositionInSet(row));
        Assert.Equal(peer.SizeOfSet, AutomationProperties.GetSizeOfSet(row));
    }

    [AvaloniaTheory]
    [InlineData(false, false, false, false, "Info, 12:00:00.000, app, first")]
    [InlineData(true, false, false, false, "Info, app, first")]
    [InlineData(false, true, false, false, "Info, 12:00:00.000, app, first, repeated 2 times")]
    [InlineData(true, true, false, false, "Info, app, first, repeated 2 times")]
    [InlineData(false, false, true, false, "Info, 12:00:00.000, app, https://example.test/, 1 link")]
    [InlineData(true, false, true, false, "Info, app, https://example.test/, 1 link")]
    [InlineData(false, true, true, false, "Info, 12:00:00.000, app, https://example.test/, repeated 2 times, 1 link")]
    [InlineData(true, true, true, false, "Info, app, https://example.test/, repeated 2 times, 1 link")]
    [InlineData(false, false, false, true, "Info, 12:00:00.000, app, first\nsecond")]
    [InlineData(true, false, false, true, "Info, app, first\nsecond")]
    [InlineData(false, true, false, true, "Info, 12:00:00.000, app, first\nsecond, repeated 2 times")]
    [InlineData(true, true, false, true, "Info, app, first\nsecond, repeated 2 times")]
    [InlineData(false, false, true, true, "Info, 12:00:00.000, app, https://example.test/\nsecond, 1 link")]
    [InlineData(true, false, true, true, "Info, app, https://example.test/\nsecond, 1 link")]
    [InlineData(false, true, true, true, "Info, 12:00:00.000, app, https://example.test/\nsecond, repeated 2 times, 1 link")]
    [InlineData(true, true, true, true, "Info, app, https://example.test/\nsecond, repeated 2 times, 1 link")]
    public void Name_FragmentCombination_UsesPublishedDisplayText(bool hidden, bool repeated, bool linked, bool expanded, string expected)
    {
        using var scene = new ConsoleInteractionScene(count: 3, message: linked ? "https://example.test/\nsecond" : "first\nsecond");
        var row = Configure(scene, hidden, repeated, expanded);
        Assert.Equal(expected, Peer(row).GetName());
    }

    [AvaloniaFact]
    public void Name_MultipleLinks_UsesPluralFragment()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "https://a.test/ https://b.test/");
        Assert.Equal("Info, 12:00:00.000, app, https://a.test/ https://b.test/, 2 links", Peer(scene.Row(1)).GetName());
    }

    [AvaloniaFact]
    public void Name_HostOverride_ReordersNamedFragments()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "first\nsecond");
        scene.Window.Resources["Nvt.Console.List.Row.NameFormat"] = "{Message}；{Source}；{Time}；{Level}；{Links}；{Repeat}";
        scene.Window.Resources["Nvt.Console.List.Level.Info"] = "資訊";
        Assert.Equal("first；app；12:00:00.000；資訊", Peer(scene.Row(1)).GetName());
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("{Unknown}")]
    [InlineData("{Message")]
    [InlineData("{Message} }")]
    [InlineData("{Message} {Level")]
    public void Name_InvalidHostFormat_UsesEnglishFallback(string format)
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        scene.Window.Resources["Nvt.Console.List.Row.NameFormat"] = format;
        Assert.Equal("Info, 12:00:00.000, app, message", Peer(scene.Row(1)).GetName());
    }

    [AvaloniaFact]
    public void Name_CollapsedLongLine_UsesPainterPreviewBound()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: new string('x', 100_000));
        var peer = Peer(scene.Row(1));
        Assert.Equal(1024, scene.Row(1).Row!.GetFirstLine().Text.Length);
        Assert.EndsWith(scene.Row(1).Row!.GetFirstLine().Text, peer.GetName(), StringComparison.Ordinal);
        Assert.Equal(1024 + "Info, 12:00:00.000, app, ".Length, peer.GetName().Length);
    }

    [AvaloniaFact]
    public void Name_ExpandedLongContent_UsesCopyBound()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: new string('x', 100_000));
        scene.Key(global::Avalonia.Input.Key.Enter);
        Assert.EndsWith(ConsoleCopyText.Message(scene.Row(1).Row!), Peer(scene.Row(1)).GetName(), StringComparison.Ordinal);
        Assert.Equal(ConsoleCopyText.MaximumCharacters + "Info, 12:00:00.000, app, ".Length, Peer(scene.Row(1)).GetName().Length);
    }

    [AvaloniaFact]
    public void Name_LeadingLiteral_KeepsOpeningPunctuation()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        scene.Window.Resources["Nvt.Console.List.Row.NameFormat"] = "[{Level}] {Message}";
        Assert.Equal("[Info] message", Peer(scene.Row(1)).GetName());
    }

    [AvaloniaFact]
    public void Name_TrailingLiteral_KeepsClosingPunctuation()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        scene.Window.Resources["Nvt.Console.List.Row.NameFormat"] = "{Level} ({Time})";
        Assert.Equal("Info (12:00:00.000)", Peer(scene.Row(1)).GetName());
    }

    [AvaloniaFact]
    public void Name_OnlyTrailingLiteral_DoesNotUseEnglishFallback()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        scene.Window.Resources["Nvt.Console.List.Row.NameFormat"] = "{Level}!";
        Assert.Equal("Info!", Peer(scene.Row(1)).GetName());
    }

    [AvaloniaFact]
    public void Name_AbsentRightFragment_DropsItsSeparator()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        scene.View.TimeMode = ConsoleTimeMode.Hidden;
        scene.Window.Resources["Nvt.Console.List.Row.NameFormat"] = "{Level}, {Time}, {Source}";
        Assert.Equal("Info, app", Peer(scene.Row(1)).GetName());
    }

    [AvaloniaFact]
    public void Name_AbsentFinalFragment_DropsItsPrefixAndSuffix()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        scene.View.TimeMode = ConsoleTimeMode.Hidden;
        scene.Window.Resources["Nvt.Console.List.Row.NameFormat"] = "{Level} ({Time})";
        Assert.Equal("Info", Peer(scene.Row(1)).GetName());
    }

    [AvaloniaFact]
    public void Name_FirstPresentFragment_KeepsItsAttachedLiteral()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        scene.View.TimeMode = ConsoleTimeMode.Hidden;
        scene.Window.Resources["Nvt.Console.List.Row.NameFormat"] = "({Time}) {Level}";
        Assert.Equal(") Info", Peer(scene.Row(1)).GetName());
    }

    [AvaloniaFact]
    public void Name_EscapedBraces_EmitLiteralBraces()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        scene.Window.Resources["Nvt.Console.List.Row.NameFormat"] = "{{{Level}}} {{message}}";
        Assert.Equal("{Info} {message}", Peer(scene.Row(1)).GetName());
    }

    [AvaloniaFact]
    public void Name_WholeTemplateLiteral_IsKept()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        scene.Window.Resources["Nvt.Console.List.Row.NameFormat"] = "Console {{message}}.";
        Assert.Equal("Console {message}.", Peer(scene.Row(1)).GetName());
    }

    [AvaloniaFact]
    public void Name_TraditionalChineseTemplate_KeepsFullWidthPunctuation()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        scene.Window.Resources["Nvt.Console.List.Row.NameFormat"] = "【{Level}】{Message}（{Source}）";
        scene.Window.Resources["Nvt.Console.List.Level.Info"] = "資訊";
        Assert.Equal("【資訊】message（app）", Peer(scene.Row(1)).GetName());
    }
}
