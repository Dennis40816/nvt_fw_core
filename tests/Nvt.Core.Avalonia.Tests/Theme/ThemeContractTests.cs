// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Pins the approved shared theme and style contracts.</summary>
public sealed class ThemeContractTests
{
    internal static readonly XNamespace Presentation = "https://github.com/avaloniaui";
    internal static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Pins every key, value, selector, setter, template and transition in source order.</summary>
    [AvaloniaTheory]
    [InlineData("ThemeTokens")]
    [InlineData("ButtonStyles")]
    [InlineData("ScrollStyles")]
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

        XDocument styleDocument = ReadExtracted("ButtonStyles");
        Assert.Empty(styleDocument.Descendants(Presentation + "StaticResource"));
        Assert.Equal("Nvt.Focus.RingRadiusConverter", Assert.Single(styleDocument.Descendants(),
            element => element.Name.LocalName == "FocusRingRadiusConverter").Attribute(Xaml + "Key")!.Value);
        string styles = styleDocument.ToString();
        string[] staticReferences = [.. Regex.Matches(styles, @"\{StaticResource (?<key>[^}]+)\}", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["key"].Value).Distinct(StringComparer.Ordinal)];
        Assert.Equal(["Nvt.Focus.RingRadiusConverter"], staticReferences);
        string[] references = [.. Regex.Matches(styles, @"\{DynamicResource (?<key>N(?:fc|vt)[^}]+)\}", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["key"].Value).Distinct(StringComparer.Ordinal)];
        Assert.NotEmpty(references);
        foreach (XElement[] resources in themes.Values)
        {
            Assert.Empty(references.Except(Keys(common).Concat(Keys(resources)), StringComparer.Ordinal));
        }
    }

    /// <summary>Every button selector is scoped to a public role; focus sets only an adorner.</summary>
    [AvaloniaFact]
    public void ButtonsAreRoleScopedAndFocusChangesOnlyTheAdorner()
    {
        XDocument styles = ReadExtracted("ButtonStyles");
        Assert.Empty(styles.Descendants(Presentation + "ControlTheme"));
        foreach (XElement style in styles.Descendants(Presentation + "Style"))
        {
            string selector = style.Attribute("Selector")!.Value;
            foreach (string branch in selector.Split(','))
            {
                Assert.Matches(@"(?:Button|ToggleButton)\.(?:actionPrimary|actionNeutral|actionDanger|actionGhost|actionIconButton|chipAction)|Border\.chipStatus", branch);
            }

            if (selector.Contains(":focus", StringComparison.Ordinal))
            {
                Assert.Contains(":focus-visible", selector, StringComparison.Ordinal);
                Assert.DoesNotContain(":focus ", selector, StringComparison.Ordinal);
                Assert.Equal("FocusAdorner", Assert.Single(style.Elements()).Attribute("Property")!.Value);
            }
        }
    }

    /// <summary>Resolves every static and dynamic resource in every Theme file in both variants.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryThemeResourceReferenceResolves(bool dark)
    {
        var host = new Window { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            string[] files = [.. typeof(ThemeContractTests).Assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith("Nvt.Core.Avalonia.Tests.Theme.Source.", StringComparison.Ordinal))];
            foreach (string file in files)
            {
                string name = file["Nvt.Core.Avalonia.Tests.Theme.Source.".Length..^4];
                XDocument document = ReadExtracted(name);
                var uri = new Uri($"avares://Nvt.Core.Avalonia/Theme/{name}.axaml");
                if (document.Root!.Name.LocalName == "Styles")
                    host.Styles.Add(new StyleInclude(uri) { Source = uri });
                else
                    host.Resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });
            }

            foreach (string file in files)
            {
                string name = file["Nvt.Core.Avalonia.Tests.Theme.Source.".Length..^4];
                XDocument document = ReadExtracted(name);
                foreach (Match match in Regex.Matches(document.ToString(), @"\{(?:DynamicResource|StaticResource) (?<key>[^}]+)\}", RegexOptions.CultureInvariant))
                {
                    string key = match.Groups["key"].Value;
                    Assert.True(host.TryFindResource(key, host.ActualThemeVariant, out object? value), $"{name}: {key}");
                    Assert.NotNull(value);
                }
                foreach (XElement alias in document.Descendants(Presentation + "StaticResource"))
                {
                    string key = alias.Attribute("ResourceKey")!.Value;
                    Assert.True(host.TryFindResource(key, host.ActualThemeVariant, out _), $"{name}: {key}");
                }
            }
        }
        finally { host.Close(); }
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
            // Expand the eight font aliases before comparing the approved palette.
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

}
