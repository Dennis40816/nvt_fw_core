// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Pins the generic projection of NFC's frozen theme and style contracts.</summary>
public sealed class ThemeContractTests
{
    internal static readonly XNamespace Presentation = "https://github.com/avaloniaui";
    internal static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Pins every key, value, selector, setter, template and transition in source order.</summary>
    [AvaloniaTheory]
    [InlineData("ThemeTokens")]
    [InlineData("ButtonStyles")]
    [InlineData("ActionRoleStyles")]
    public void ExtractedXamlMatchesFrozenBaseline(string name)
    {
        Assert.Equal(ReadBaseline(name).Root!.ToString(), ReadExtracted(name).Root!.ToString());
    }

    /// <summary>Ports theme-key uniqueness, theme parity and resource-reference coverage from NFC.</summary>
    [AvaloniaFact]
    public void SharedThemeTokensHaveUniqueDefinitionsAndResolveEveryButtonReference()
    {
        XElement root = ReadExtracted("ThemeTokens").Root!;
        var themes = root.Element(Presentation + "ResourceDictionary.ThemeDictionaries")!.Elements()
            .ToDictionary(element => element.Attribute(Xaml + "Key")!.Value, element => element.Elements().ToArray());
        Assert.Equal(["Dark", "Light"], themes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(Keys(themes["Light"]), Keys(themes["Dark"]));
        foreach (XElement[] resources in themes.Values)
        {
            Assert.Equal(resources.Length, Keys(resources).Distinct(StringComparer.Ordinal).Count());
        }

        XElement[] common = [.. root.Elements().Where(element => element.Attribute(Xaml + "Key") != null)];
        Assert.Equal(common.Length, Keys(common).Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(Keys(common).Intersect(Keys(themes["Light"]), StringComparer.Ordinal));
        Assert.Equal(7, common.Count(element => element.Name == Xaml + "Double" &&
            (element.Attribute(Xaml + "Key")!.Value.StartsWith("NfcSpace", StringComparison.Ordinal) ||
             element.Attribute(Xaml + "Key")!.Value == "NfcFieldSpacing")));
        Assert.Equal(6, common.Count(element => element.Attribute(Xaml + "Key")!.Value.StartsWith("NfcFontSize", StringComparison.Ordinal)));
        Assert.Equal(2, common.Count(element => element.Name.LocalName == "FontFamily"));
        Assert.Equal(3, common.Count(element => element.Name.LocalName == "CornerRadius"));

        foreach (string name in new[] { "ButtonStyles", "ActionRoleStyles" })
        {
            XDocument styles = ReadExtracted(name);
            XElement? local = styles.Root!.Element(Presentation + "Styles.Resources")?
                .Element(Presentation + "ResourceDictionary");
            XElement[] localCommon = [.. local?.Elements().Where(element => element.Attribute(Xaml + "Key") != null) ?? []];
            string[] references = [.. Regex.Matches(styles.ToString(), @"\{(?:DynamicResource|StaticResource) (?<key>[^}]+)\}", RegexOptions.CultureInvariant)
                .Select(match => match.Groups["key"].Value).Distinct(StringComparer.Ordinal)];
            Assert.NotEmpty(references);
            foreach ((string variant, XElement[] resources) in themes)
            {
                XElement[] localTheme = [.. local?.Element(Presentation + "ResourceDictionary.ThemeDictionaries")?
                    .Elements().Single(element => element.Attribute(Xaml + "Key")!.Value == variant).Elements() ?? []];
                string[] styleKeys = [.. styles.Descendants().Where(element => element.Attribute(Xaml + "Key") != null &&
                    element.Name.LocalName == "ControlTheme").Select(element => element.Attribute(Xaml + "Key")!.Value)];
                Assert.Empty(references.Except(Keys(common).Concat(Keys(resources)).Concat(Keys(localCommon))
                    .Concat(Keys(localTheme)).Concat(styleKeys), StringComparer.Ordinal));
            }
        }
    }

    /// <summary>Ports NFC's owned button template and Open-pill geometry contract.</summary>
    [AvaloniaFact]
    public void SemanticButtonsShareOpenPillsAndKeepExplicitStructuralGeometry()
    {
        XDocument styles = ReadExtracted("ButtonStyles");
        XElement theme = Assert.Single(styles.Descendants(Presentation + "ControlTheme"));
        Assert.Equal("NfcSemanticButtonTheme", theme.Attribute(Xaml + "Key")!.Value);
        Assert.Equal("Button", theme.Attribute("TargetType")!.Value);
        XElement presenter = Assert.Single(theme.Descendants(Presentation + "ContentPresenter"));
        Assert.Equal("PART_ContentPresenter", presenter.Attribute(Xaml + "Name")!.Value);
        Assert.Equal("{DynamicResource NfcCompactCornerRadius}", presenter.Attribute("CornerRadius")!.Value);
        Assert.Equal("True", presenter.Attribute("RecognizesAccessKey")!.Value);
        AssertSetter(styles, "Button", "Theme", "{StaticResource NfcSemanticButtonTheme}");
        AssertSetter(styles, "Button", "FocusAdorner", "{x:Null}");
        foreach (string role in new[] { "semanticAction", "browseAction", "railAction", "summaryChip", "closeButton" })
        {
            AssertSetter(styles, $"Button.{role} /template/ ContentPresenter#PART_ContentPresenter",
                "CornerRadius", "{DynamicResource NfcPillCornerRadius}");
        }

        AssertSetter(styles, "Button.primary", "Background", "{DynamicResource NfcAccentSurfaceBrush}");
        AssertSetter(styles, "Button.primary", "BorderBrush", "{DynamicResource NfcAccentBorderLightBrush}");
        AssertSetter(styles, "Button.secondary", "Background", "{DynamicResource NfcSurfaceBrush}");
        AssertSetter(styles, "Button.secondary:pressed /template/ ContentPresenter#PART_ContentPresenter",
            "Background", "{DynamicResource NfcSecondaryActionPressedBrush}");
        AssertSetter(styles, "Button.action:pointerover /template/ ContentPresenter#PART_ContentPresenter",
            "Background", "{DynamicResource NfcAccentBrush}");
        Assert.DoesNotContain(styles.Descendants(Presentation + "Style"), element =>
            element.Attribute("Selector")!.Value.Contains("Button.browseAction:pointerover", StringComparison.Ordinal));
    }

    /// <summary>Ports NFC's focus-visible contract without product-navigation selectors.</summary>
    [AvaloniaFact]
    public void SemanticButtonFocusUsesFocusVisibleWithoutAPlainFocusSelector()
    {
        XDocument styles = ReadExtracted("ButtonStyles");
        string[] focusSelectors = [.. styles.Descendants(Presentation + "Style")
            .Select(element => element.Attribute("Selector")!.Value)
            .Where(selector => selector.Contains(":focus", StringComparison.Ordinal))];
        Assert.NotEmpty(focusSelectors);
        Assert.All(focusSelectors, selector => Assert.Contains(":focus-visible", selector, StringComparison.Ordinal));
        const string focus = "Button:focus-visible /template/ ContentPresenter#PART_ContentPresenter";
        AssertSetter(styles, focus, "BorderThickness", "2");
        AssertSetter(styles, focus, "BorderBrush", "{DynamicResource NfcAccentBorderStrongBrush}");
    }

    internal static XDocument ReadBaseline(string name)
    {
        using Stream stream = typeof(ThemeContractTests).Assembly.GetManifestResourceStream(
            $"Nvt.Core.Avalonia.Tests.Theme.Baseline.{name}.xml")!;
        return XDocument.Load(stream);
    }

    internal static XDocument ReadExtracted(string name)
    {
        using Stream stream = typeof(ThemeContractTests).Assembly.GetManifestResourceStream(
            $"Nvt.Core.Avalonia.Tests.Theme.Source.{name}.xml")!;
        XDocument document = XDocument.Load(stream);
        if (name == "ThemeTokens")
        {
            // Expand the eight font aliases before comparing the unchanged NFC projection.
            XElement fonts = ReadExtracted("NfcLegacyFontTokens").Root!;
            document.Root!.Element(Presentation + "ResourceDictionary.MergedDictionaries")!.Remove();
            foreach (XElement alias in document.Root.Elements(Presentation + "StaticResource").ToArray())
            {
                XElement token = new(Assert.Single(fonts.Elements(), element =>
                    element.Attribute(Xaml + "Key")!.Value == alias.Attribute("ResourceKey")!.Value));
                token.SetAttributeValue(Xaml + "Key", alias.Attribute(Xaml + "Key")!.Value);
                alias.ReplaceWith(token);
            }
        }

        return document;
    }

    private static string[] Keys(IEnumerable<XElement> elements) =>
        [.. elements.Select(element => element.Attribute(Xaml + "Key")!.Value).Order(StringComparer.Ordinal)];

    private static void AssertSetter(XDocument styles, string selector, string property, string value)
    {
        XElement style = Assert.Single(styles.Descendants(Presentation + "Style"), element =>
            element.Attribute("Selector")!.Value.Split(',').Select(part => part.Trim()).Contains(selector, StringComparer.Ordinal));
        XElement setter = Assert.Single(style.Elements(), element => element.Attribute("Property")!.Value == property);
        Assert.Equal(value, setter.Attribute("Value")!.Value);
    }
}
