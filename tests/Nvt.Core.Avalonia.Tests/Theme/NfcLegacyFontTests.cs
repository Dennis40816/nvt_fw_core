// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Pins NFC's eight frozen font values and their compatibility consumers.</summary>
public sealed class NfcLegacyFontTests
{
    /// <summary>Ports NFC's exact family and size assertions to the Core resource names.</summary>
    [AvaloniaTheory]
    [InlineData("NfcFontSize10", "Nvt.Font.NfcLegacy.Size10", "10")]
    [InlineData("NfcFontSize11", "Nvt.Font.NfcLegacy.Size11", "11")]
    [InlineData("NfcFontSize12", "Nvt.Font.NfcLegacy.Size12", "12")]
    [InlineData("NfcFontSize13", "Nvt.Font.NfcLegacy.Size13", "13")]
    [InlineData("NfcFontSize14", "Nvt.Font.NfcLegacy.Size14", "14")]
    [InlineData("NfcFontSize16", "Nvt.Font.NfcLegacy.Size16", "16")]
    [InlineData("NfcUiFontFamily", "Nvt.Font.NfcLegacy.Ui.Family",
        "fonts:Inter#Inter, Microsoft JhengHei UI, Noto Sans CJK TC, Noto Sans TC, Segoe UI")]
    [InlineData("NfcTechnicalFontFamily", "Nvt.Font.NfcLegacy.Technical.Family", "Cascadia Mono, Consolas")]
    public void CoreAndCompatibilityKeysMatchFrozenNfcValues(string legacyKey, string coreKey, string expected)
    {
        XElement baseline = Assert.Single(ThemeContractTests.ReadBaseline("ThemeTokens").Root!.Elements(),
            element => element.Attribute(ThemeContractTests.Xaml + "Key")?.Value == legacyKey);
        XElement[] fonts = [.. ThemeContractTests.ReadExtracted("NfcLegacyFontTokens").Root!.Elements()];
        Assert.Equal(8, fonts.Length);
        Assert.Equal(8, fonts.Select(element => element.Attribute(ThemeContractTests.Xaml + "Key")!.Value)
            .Distinct(StringComparer.Ordinal).Count());
        XElement token = Assert.Single(fonts, element => element.Attribute(ThemeContractTests.Xaml + "Key")!.Value == coreKey);
        Assert.Equal(baseline.Name, token.Name);
        Assert.Equal(expected, baseline.Value);
        Assert.Equal(expected, token.Value);

        var uri = new Uri("avares://Nvt.Core.Avalonia/Theme/NfcLegacyFontTokens.axaml");
        var include = new ResourceInclude(uri) { Source = uri };
        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Assert.True(include.TryGetResource(coreKey, variant, out object? standalone), coreKey);
            Assert.True(Application.Current!.TryGetResource(coreKey, variant, out object? core), coreKey);
            Assert.True(Application.Current.TryGetResource(legacyKey, variant, out object? legacy), legacyKey);
            if (baseline.Name.LocalName == "Double")
            {
                double size = double.Parse(expected, CultureInfo.InvariantCulture);
                Assert.Equal(size, Assert.IsType<double>(standalone));
                Assert.Equal(size, Assert.IsType<double>(core));
                Assert.Equal(size, Assert.IsType<double>(legacy));
            }
            else
            {
                Assert.Equal(new FontFamily(expected), Assert.IsType<FontFamily>(standalone));
                Assert.Equal(new FontFamily(expected), Assert.IsType<FontFamily>(core));
                Assert.Same(core, Assert.IsType<FontFamily>(legacy));
            }
        }
    }

    /// <summary>Checks compiled StaticResource and DynamicResource consumers against frozen text layout.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompatibilityConsumersKeepFontsSizesAndWrapping(bool dark)
    {
        var uri = new Uri("avares://Nvt.Core.Avalonia.Tests/Theme/LegacyFontConsumers.axaml");
        var include = new ResourceInclude(uri) { Source = uri };
        var host = new Window { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        host.Resources.MergedDictionaries.Add(include);
        XElement baseline = ThemeContractTests.ReadBaseline("ThemeTokens").Root!;
        foreach ((string role, string text) in new[]
        {
            ("Ui", "Memory processing details"),
            ("Ui", "記憶體處理詳細資料"),
            ("Technical", "0x12AB 1.2.3"),
        })
        {
            string family = Assert.Single(baseline.Elements(), element =>
                element.Attribute(ThemeContractTests.Xaml + "Key")?.Value == $"Nfc{role}FontFamily").Value;
            double size = role == "Ui" ? 14 : 13;
            FontWeight weight = role == "Ui" ? FontWeight.SemiBold : FontWeight.Normal;
            var original = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily(family),
                FontSize = size,
                FontWeight = weight,
                TextWrapping = TextWrapping.Wrap,
            };
            original.Measure(new Size(96, double.PositiveInfinity));
            original.Arrange(new Rect(original.DesiredSize));
            foreach (string mode in new[] { "Static", "Dynamic" })
            {
                Assert.True(include.TryGetResource(mode + role, host.ActualThemeVariant, out object? resource));
                TextBlock consumer = Assert.IsType<TextBlock>(resource);
                host.Content = consumer;
                consumer.Text = text;
                consumer.Measure(new Size(96, double.PositiveInfinity));
                consumer.Arrange(new Rect(consumer.DesiredSize));
                Assert.Equal(original.FontFamily, consumer.FontFamily);
                Assert.Equal(size, consumer.FontSize);
                Assert.Equal(weight, consumer.FontWeight);
                Assert.Equal(original.TextWrapping, consumer.TextWrapping);
                Assert.Equal(original.DesiredSize, consumer.DesiredSize);
                Assert.Equal(original.Bounds, consumer.Bounds);
            }
        }

        host.Close();
    }
}
