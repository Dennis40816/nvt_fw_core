// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Xml.Linq;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.Tests.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.ListMenu.ListMenuTestHost;

namespace Nvt.Core.Avalonia.Tests.ListMenu;

/// <summary>Guards token-only styling, motion, and keyboard-only focus for the list and menu family.</summary>
public sealed class ListMenuContractTests
{
    /// <summary>Rejects literal colors and corners in templates, setters, and embedded style resources.</summary>
    [Theory]
    [InlineData("ListStyles")]
    [InlineData("MenuStyles")]
    public void StylesContainNoLiteralColorsOrCorners(string name)
    {
        XElement root = ThemeContractTests.ReadExtracted(name).Root!;
        string[] properties = ["Background", "Foreground", "BorderBrush", "Color", "Fill", "Stroke", "CornerRadius", "BoxShadow"];
        foreach (XElement element in root.Descendants())
        {
            Assert.False(element.Name.LocalName is "Color" or "SolidColorBrush" or "CornerRadius" or "BoxShadows");
            foreach (XAttribute attribute in element.Attributes().Where(attribute => properties.Contains(attribute.Name.LocalName.Split('.').Last())))
                AssertResource(attribute.Value);
            if (element.Name.LocalName == "Setter" && properties.Contains(element.Attribute("Property")?.Value))
                AssertResource(element.Attribute("Value")?.Value ?? element.Value);
            if (element.Name.LocalName.EndsWith(".CornerRadius", StringComparison.Ordinal))
                Assert.Equal("MultiBinding", Assert.Single(element.Elements()).Name.LocalName);
            if (element.Name.LocalName == "Style" && element.Attribute("Selector")!.Value.Contains(":focus", StringComparison.Ordinal))
                Assert.Contains(":focus-visible", element.Attribute("Selector")!.Value, StringComparison.Ordinal);
        }
        static void AssertResource(string value) => Assert.True(value.StartsWith("{DynamicResource ", StringComparison.Ordinal)
            || value.StartsWith("{TemplateBinding ", StringComparison.Ordinal)
            || value == "{Binding $parent[ListBoxItem].Foreground}"
            || value == "{Binding $parent[MenuItem].Foreground}", value);
    }

    /// <summary>Pins 150 ms brush transitions and verifies the existing reducedMotion class on roots and items.</summary>
    [AvaloniaFact]
    public void MotionUsesSharedDurationAndCanBeReduced()
    {
        var list = new ListBoxItem { Content = "Sample" };
        var menu = new MenuItem { Header = "Sample" };
        var root = new StackPanel { Children = { list, menu } };
        Window host = Create(root);
        try
        {
            Show(host, snapshot: false);
            foreach (TemplatedControl item in new TemplatedControl[] { list, menu })
            {
                Assert.NotNull(item.Transitions);
                Assert.Equal(item is MenuItem ? 1 : 2, item.Transitions.Count);
                if (item is MenuItem)
                {
                    var body = Part(item, "PART_LayoutRoot");
                    Assert.NotNull(body.Transitions);
                    Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.IsType<BrushTransition>(Assert.Single(body.Transitions)).Duration);
                }
                Assert.All(item.Transitions, transition => Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.IsType<BrushTransition>(transition).Duration));
                item.Classes.Add("reducedMotion");
                Assert.Null(item.Transitions);
                item.Classes.Remove("reducedMotion");
            }
            root.Classes.Add("reducedMotion");
            Assert.Null(list.Transitions);
            Assert.Null(menu.Transitions);
            Assert.Null(Part(menu, "PART_LayoutRoot").Transitions);
        }
        finally { host.Close(); }
    }
}
