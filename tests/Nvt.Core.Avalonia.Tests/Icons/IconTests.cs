// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Icons;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Icons;

/// <summary>Checks icon names, resources, bundled glyphs, styling, and accessibility.</summary>
[Collection(nameof(IconTestIsolation))]
public sealed class IconTests
{
    private const string IconFamily =
        "avares://Nvt.Core.Fonts/Assets/MaterialSymbolsOutlined#Material Symbols Outlined";
    private const string IconAsset =
        "avares://Nvt.Core.Fonts/Assets/MaterialSymbolsOutlined/MaterialSymbolsOutlined-Regular.ttf";
    private static readonly string[] ConsumerKeys = ["ConstantIcon", "ResourceIcon"];
    private static readonly string[] IconSelectors =
        ["TextBlock.nvtIcon", "TextBlock.nvtIcon.small", "TextBlock.nvtIcon.large"];
    private static readonly Uri ResourcesUri = new("avares://Nvt.Core.Avalonia/Icons/IconResources.axaml");
    private static readonly Uri StylesUri = new("avares://Nvt.Core.Avalonia/Icons/IconStyles.axaml");

    private readonly IconSessionFixture fixture;

    /// <summary>Initializes the tests with the icon collection's shared session.</summary>
    /// <param name="fixture">The shared headless session.</param>
    public IconTests(IconSessionFixture fixture) => this.fixture = fixture;

    /// <summary>Checks every constant against the actual bundled font and its private-use character range.</summary>
    [Fact]
    public Task ConstantsAreSinglePrivateUseCharactersWithBundledGlyphs() => RunAsync(() =>
    {
        Dictionary<string, string> constants = ReadConstants();
        Assert.Equal(68, constants.Count);
        var family = Resource<FontFamily>("Nvt.Font.Icon.Family");
        Assert.Equal(new FontFamily(IconFamily), family);
        Assert.True(FontManager.Current.TryGetGlyphTypeface(
            new Typeface(family, weight: FontWeight.Normal), out var typeface));
        Assert.Equal("Material Symbols Outlined", typeface.FamilyName);
        Assert.Equal(FontWeight.Normal, typeface.Weight);
        Assert.Equal(FontSimulations.None, typeface.FontSimulations);
        Assert.True(typeface.PlatformTypeface.TryGetStream(out var stream));
        using (stream)
        using (var asset = AssetLoader.Open(new Uri(IconAsset)))
        {
            Assert.Equal(SHA256.HashData(asset), SHA256.HashData(stream));
        }

        foreach ((string name, string glyph) in constants)
        {
            Assert.Single(glyph);
            Assert.InRange(glyph[0], '\uE000', '\uF8FF');
            Assert.True(typeface.CharacterToGlyphMap.TryGetGlyph(glyph[0], out ushort index), name);
            Assert.NotEqual((ushort)0, index);
        }
    });

    /// <summary>Checks both directions of the constant and resource contract, including compiled resource values.</summary>
    [Fact]
    public Task ConstantsAndResourcesMatchExactly() => RunAsync(() =>
    {
        Dictionary<string, string> constants = ReadConstants();
        XElement root = ReadSource("IconResources").Root!;
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var resources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XElement element in root.Elements())
        {
            Assert.Equal(xaml + "String", element.Name);
            string key = element.Attribute(xaml + "Key")!.Value;
            Assert.StartsWith("Nvt.Icon.", key, StringComparison.Ordinal);
            Assert.True(resources.TryAdd(key["Nvt.Icon.".Length..], element.Value), key);
        }

        Assert.Equal(constants.Keys.Order(StringComparer.Ordinal), resources.Keys.Order(StringComparer.Ordinal));
        var include = new ResourceInclude(ResourcesUri) { Source = ResourcesUri };
        foreach ((string name, string glyph) in constants)
        {
            Assert.Equal(glyph, resources[name]);
            Assert.True(include.TryGetResource("Nvt.Icon." + name, null, out var value), name);
            Assert.Equal(glyph, Assert.IsType<string>(value));
        }
    });

    /// <summary>Checks canonical codepoints against the pinned upstream subset without relying on font aliases.</summary>
    [Fact]
    public void ConstantsMatchThePinnedSourceTable()
    {
        using var stream = OpenEmbedded("codepoints-subset.txt");
        using var reader = new StreamReader(stream);
        string[] lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            "# Source: google/material-design-icons@737e3324305806514d7909874fa1818ae1808232 " +
            "variablefont/MaterialSymbolsOutlined[FILL,GRAD,opsz,wght].codepoints", lines[0].TrimEnd('\r'));
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines.Skip(1))
        {
            string[] columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, columns.Length);
            string name = string.Concat(columns[0].Split('_')
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
            int codepoint = int.Parse(columns[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Assert.True(expected.TryAdd(name, char.ConvertFromUtf32(codepoint)), name);
        }

        Dictionary<string, string> constants = ReadConstants();
        Assert.Equal(constants.Keys.Order(StringComparer.Ordinal), expected.Keys.Order(StringComparer.Ordinal));
        foreach ((string name, string glyph) in constants)
        {
            Assert.Equal(expected[name], glyph);
        }
    }

    /// <summary>Checks the documented constant and resource syntax through compiled XAML consumers.</summary>
    [Fact]
    public Task CompiledConsumersResolveConstantsAndResources() => RunAsync(() =>
    {
        var uri = new Uri("avares://Nvt.Core.Avalonia.Tests/Icons/IconConsumers.axaml");
        var include = new ResourceInclude(uri) { Source = uri };
        foreach (string key in ConsumerKeys)
        {
            Assert.True(include.TryGetResource(key, null, out var value), key);
            var icon = Assert.IsType<TextBlock>(value);
            Assert.Equal(NvtIcons.Close, icon.Text);
            Assert.Contains("nvtIcon", icon.Classes);
        }
    });

    /// <summary>Checks font roles, size modifiers, inherited colors, accessibility, and the ordinary text default.</summary>
    /// <param name="dark">Whether to start with the dark theme.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task IconStylesResolveRolesAndInheritForeground(bool dark) => RunAsync(() =>
    {
        var plain = new TextBlock { Text = "Ordinary text" };
        var icon = new TextBlock { Text = NvtIcons.Warning };
        icon.Classes.Add("nvtIcon");
        var panel = new StackPanel();
        panel.Children.Add(plain);
        panel.Children.Add(icon);
        var host = new Window
        {
            Content = panel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        host.Resources.ThemeDictionaries.Add(ThemeVariant.Light,
            new ResourceDictionary { ["InheritedForeground"] = Brushes.Black });
        host.Resources.ThemeDictionaries.Add(ThemeVariant.Dark,
            new ResourceDictionary { ["InheritedForeground"] = Brushes.White });
        host.Bind(TemplatedControl.ForegroundProperty, new DynamicResourceExtension("InheritedForeground"));
        host.Show();
        try
        {
            host.UpdateLayout();
            FontFamily defaultFamily = plain.FontFamily;
            double defaultSize = plain.FontSize;
            FontWeight defaultWeight = plain.FontWeight;
            Assert.NotEqual(new FontFamily(IconFamily), defaultFamily);
            host.Styles.Add(new StyleInclude(StylesUri) { Source = StylesUri });
            host.UpdateLayout();

            foreach ((string modifier, double size) in new[] { ("", 16d), ("small", 13d), ("large", 24d) })
            {
                icon.Classes.Remove("small");
                icon.Classes.Remove("large");
                if (modifier.Length != 0)
                {
                    icon.Classes.Add(modifier);
                }

                host.UpdateLayout();
                Assert.Equal(Resource<FontFamily>("Nvt.Font.Icon.Family"), icon.FontFamily);
                Assert.Equal(size, icon.FontSize);
                Assert.Equal(FontWeight.Normal, icon.FontWeight);
                Assert.Equal(HorizontalAlignment.Center, icon.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Center, icon.VerticalAlignment);
                Assert.Equal(TextAlignment.Center, icon.TextAlignment);
                Assert.Equal(TextTrimming.None, icon.TextTrimming);
                Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(icon));
                Assert.Same(dark ? Brushes.White : Brushes.Black, icon.Foreground);
                Assert.Same(host.Foreground, icon.Foreground);
                Assert.Equal(defaultFamily, plain.FontFamily);
                Assert.Equal(defaultSize, plain.FontSize);
                Assert.Equal(defaultWeight, plain.FontWeight);
            }

            host.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
            host.UpdateLayout();
            Assert.Same(dark ? Brushes.Black : Brushes.White, icon.Foreground);
            var changedForeground = new SolidColorBrush(Colors.Crimson);
            host.Foreground = changedForeground;
            Assert.Same(changedForeground, icon.Foreground);
            Assert.Same(changedForeground, plain.Foreground);

            icon.Classes.Clear();
            host.UpdateLayout();
            Assert.Equal(defaultFamily, icon.FontFamily);
            Assert.Equal(defaultSize, icon.FontSize);
            Assert.Equal(defaultWeight, icon.FontWeight);
            Assert.Equal(AccessibilityView.Default, AutomationProperties.GetAccessibilityView(icon));
        }
        finally
        {
            host.Close();
        }
    });

    /// <summary>Checks that icon selectors cannot change global text styling or override inherited foreground.</summary>
    [Fact]
    public void IconStylesHaveNoGlobalTextBlockOrForegroundSetters()
    {
        XElement[] styles = [.. ReadSource("IconStyles").Root!.Elements()];
        Assert.Equal(
            IconSelectors,
            styles.Select(style => style.Attribute("Selector")!.Value));
        Assert.All(styles.SelectMany(style => style.Elements()), setter =>
            Assert.NotEqual("Foreground", setter.Attribute("Property")!.Value));
    }

    private static Dictionary<string, string> ReadConstants() =>
        typeof(NvtIcons).GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(field =>
            {
                Assert.True(field.IsLiteral && !field.IsInitOnly, field.Name);
                return field.Name;
            }, field => Assert.IsType<string>(field.GetRawConstantValue()), StringComparer.Ordinal);

    private static T Resource<T>(string key)
    {
        Assert.NotNull(Application.Current);
        Assert.True(Application.Current.Resources.TryGetResource(key, null, out var value), key);
        return Assert.IsType<T>(value);
    }

    private static XDocument ReadSource(string name)
    {
        using var stream = OpenEmbedded("Source." + name + ".xml");
        return XDocument.Load(stream);
    }

    private static Stream OpenEmbedded(string name) =>
        typeof(IconTests).Assembly.GetManifestResourceStream("Nvt.Core.Avalonia.Tests.Icons." + name)
        ?? throw new InvalidOperationException("Missing icon test resource: " + name);

    private Task RunAsync(Action action) =>
        this.fixture.Session.Dispatch(action, TestContext.Current.CancellationToken);
}
