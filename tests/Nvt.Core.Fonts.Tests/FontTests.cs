// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Xunit;

namespace Nvt.Core.Fonts.Tests;

/// <summary>Checks embedded font selection and shaping with the real Skia backend.</summary>
public sealed class FontTests
{
    private const string InterFamily = "avares://Avalonia.Fonts.Inter/Assets#Inter";
    private const string MonoFamily = "avares://Nvt.Core.Fonts/Assets/CascadiaMono#Cascadia Mono";
    private const string CjkFamily = "avares://Nvt.Core.Fonts/Assets/NotoSansTC#Noto Sans TC";
    private const string IconFamily = "avares://Nvt.Core.Fonts/Assets/MaterialSymbolsOutlined#Material Symbols Outlined";

    /// <summary>Gets the approved roles and their expected static font files.</summary>
    public static TheoryData<string, string, double, FontWeight, string, string> Roles => new()
    {
        { "Title", InterFamily, 24, FontWeight.SemiBold, "Inter SemiBold", "avares://Avalonia.Fonts.Inter/Assets/Inter-SemiBold.ttf" },
        { "Heading", InterFamily, 16, FontWeight.SemiBold, "Inter SemiBold", "avares://Avalonia.Fonts.Inter/Assets/Inter-SemiBold.ttf" },
        { "Body", InterFamily, 13, FontWeight.Normal, "Inter", "avares://Avalonia.Fonts.Inter/Assets/Inter-Regular.ttf" },
        { "Caption", InterFamily, 11, FontWeight.Normal, "Inter", "avares://Avalonia.Fonts.Inter/Assets/Inter-Regular.ttf" },
        { "Mono", MonoFamily, 13, FontWeight.Normal, "Cascadia Mono", "avares://Nvt.Core.Fonts/Assets/CascadiaMono/CascadiaMono-Regular.ttf" },
        { "Numbers", MonoFamily, 13, FontWeight.Normal, "Cascadia Mono", "avares://Nvt.Core.Fonts/Assets/CascadiaMono/CascadiaMono-Regular.ttf" },
        { "BodyStrong", InterFamily, 13, FontWeight.SemiBold, "Inter SemiBold", "avares://Avalonia.Fonts.Inter/Assets/Inter-SemiBold.ttf" },
        { "CaptionStrong", InterFamily, 11, FontWeight.SemiBold, "Inter SemiBold", "avares://Avalonia.Fonts.Inter/Assets/Inter-SemiBold.ttf" },
        { "MonoStrong", MonoFamily, 13, FontWeight.SemiBold, "Cascadia Mono SemiBold", "avares://Nvt.Core.Fonts/Assets/CascadiaMono/CascadiaMono-SemiBold.ttf" },
        { "MonoCaption", MonoFamily, 11, FontWeight.Normal, "Cascadia Mono", "avares://Nvt.Core.Fonts/Assets/CascadiaMono/CascadiaMono-Regular.ttf" },
        { "Icon", IconFamily, 16, FontWeight.Normal, "Material Symbols Outlined", "avares://Nvt.Core.Fonts/Assets/MaterialSymbolsOutlined/MaterialSymbolsOutlined-Regular.ttf" },
    };

    /// <summary>Checks all three resources and the actual font file for each role.</summary>
    /// <param name="role">The role name.</param>
    /// <param name="familyUri">The expected single-family URI.</param>
    /// <param name="size">The expected font size.</param>
    /// <param name="weight">The expected font weight.</param>
    /// <param name="familyName">The expected font family name.</param>
    /// <param name="assetUri">The expected static font file.</param>
    [AvaloniaTheory]
    [MemberData(nameof(Roles))]
    public void RoleResourcesResolveToTheExpectedStaticFile(
        string role, string familyUri, double size, FontWeight weight, string familyName, string assetUri)
    {
        var family = Resource<FontFamily>($"Nvt.Font.{role}.Family");
        Assert.Equal(new FontFamily(familyUri), family);
        Assert.Single(family.FamilyNames);
        Assert.Equal(size, Resource<double>($"Nvt.Font.{role}.Size"));
        Assert.Equal(weight, Resource<FontWeight>($"Nvt.Font.{role}.Weight"));

        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(family, weight: weight), out var glyphTypeface));
        AssertTypeface(glyphTypeface, familyName, weight, assetUri);
    }

    /// <summary>Checks that the fallback resource and API select the same single family.</summary>
    [AvaloniaFact]
    public void CjkFallbackMatchesTheResource()
    {
        var family = Resource<FontFamily>("Nvt.Font.Fallback.Cjk.Family");
        Assert.Equal(new FontFamily(CjkFamily), family);
        Assert.Single(family.FamilyNames);
        Assert.Equal(family, NvtCoreFonts.CjkFallback.FontFamily);
    }

    /// <summary>Checks Chinese fallback through the layout that TextBlock renders.</summary>
    /// <param name="role">The Inter role.</param>
    /// <param name="cjkWeight">The selected static Noto Sans TC weight.</param>
    /// <param name="cjkFile">The selected static Noto Sans TC file.</param>
    [AvaloniaTheory]
    [InlineData("Body", FontWeight.Normal, "NotoSansTC-Regular.otf")]
    [InlineData("Caption", FontWeight.Normal, "NotoSansTC-Regular.otf")]
    [InlineData("Title", FontWeight.Bold, "NotoSansTC-Bold.otf")]
    [InlineData("Heading", FontWeight.Bold, "NotoSansTC-Bold.otf")]
    [InlineData("BodyStrong", FontWeight.Bold, "NotoSansTC-Bold.otf")]
    [InlineData("CaptionStrong", FontWeight.Bold, "NotoSansTC-Bold.otf")]
    public void ChineseTextUsesNotoSansTcWithoutSimulation(string role, FontWeight cjkWeight, string cjkFile)
    {
        using var layout = CreateLayout(role, "A中文測試Z");
        var runs = layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToArray();
        Assert.Equal("A中文測試Z", string.Concat(runs.Select(run => run.Text.ToString())));
        var chineseRuns = runs.Where(run => run.Text.ToString().Any(c => c is >= '\u4e00' and <= '\u9fff')).ToArray();
        Assert.NotEmpty(chineseRuns);
        Assert.Equal("中文測試", string.Concat(chineseRuns.Select(run => run.Text.ToString())));
        Assert.All(chineseRuns, run =>
        {
            AssertTypeface(run.GlyphRun.GlyphTypeface, "Noto Sans TC", cjkWeight,
                $"avares://Nvt.Core.Fonts/Assets/NotoSansTC/{cjkFile}");
            Assert.All(run.GlyphRun.GlyphInfos, glyph => Assert.NotEqual((ushort)0, glyph.GlyphIndex));
        });

        var latinRuns = runs.Except(chineseRuns).ToArray();
        Assert.NotEmpty(latinRuns);
        var roleWeight = Resource<FontWeight>($"Nvt.Font.{role}.Weight");
        string interFile = roleWeight == FontWeight.Normal ? "Inter-Regular.ttf" : "Inter-SemiBold.ttf";
        string interFamilyName = roleWeight == FontWeight.Normal ? "Inter" : "Inter SemiBold";
        Assert.All(latinRuns, run => AssertTypeface(run.GlyphRun.GlyphTypeface, interFamilyName, roleWeight,
            $"avares://Avalonia.Fonts.Inter/Assets/{interFile}"));
    }

    /// <summary>Checks the upstream home code point, its older alias and its icon-name ligature.</summary>
    [AvaloniaFact]
    public void HomeCodePointAndLigatureProduceTheSameGlyph()
    {
        var family = Resource<FontFamily>("Nvt.Font.Icon.Family");
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out var typeface));
        Assert.True(typeface.CharacterToGlyphMap.TryGetGlyph(0xE9B2, out ushort homeGlyph));
        Assert.NotEqual((ushort)0, homeGlyph);
        Assert.True(typeface.CharacterToGlyphMap.TryGetGlyph(0xE88A, out ushort olderHomeGlyph));
        Assert.Equal(homeGlyph, olderHomeGlyph);

        using var layout = CreateLayout("Icon", "home");
        var run = Assert.Single(Assert.Single(layout.TextLines).TextRuns.OfType<ShapedTextRun>());
        AssertTypeface(run.GlyphRun.GlyphTypeface, "Material Symbols Outlined", FontWeight.Normal,
            "avares://Nvt.Core.Fonts/Assets/MaterialSymbolsOutlined/MaterialSymbolsOutlined-Regular.ttf");
        Assert.Equal(homeGlyph, Assert.Single(run.GlyphRun.GlyphInfos).GlyphIndex);
    }

    /// <summary>Checks the exact embedded bytes of each scaffold font.</summary>
    /// <param name="path">The asset path within Nvt.Core.Fonts.</param>
    /// <param name="length">The expected byte count.</param>
    /// <param name="sha256">The expected SHA-256 hash.</param>
    [AvaloniaTheory]
    [InlineData("CascadiaMono/CascadiaMono-Regular.ttf", 575912, "06520d032ec274fa5040b22c6f4a1d829081b24ba40b2da56dae89bf10c7b481")]
    [InlineData("CascadiaMono/CascadiaMono-SemiBold.ttf", 581840, "8e04c1b811913a20773a3761d8994f15efe3029509c8d0556d74ae989558146d")]
    [InlineData("NotoSansTC/NotoSansTC-Regular.otf", 5683368, "5bab0cb3c1cf89dde07c4a95a4054b195afbcfe784d69d75c340780712237537")]
    [InlineData("NotoSansTC/NotoSansTC-Bold.otf", 5839972, "55420b259eb119bf5f2a0aadba10cf9d736c12d64ab93e78546d69ef5f43558b")]
    [InlineData("MaterialSymbolsOutlined/MaterialSymbolsOutlined-Regular.ttf", 1397116, "e90300193fd701f4a4eb88d292699ce3b4237863c3f297da0eda866dfb415347")]
    public void EmbeddedAssetMatchesThePinnedHash(string path, long length, string sha256)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://Nvt.Core.Fonts/Assets/{path}"));
        Assert.Equal(length, stream.Length);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(stream)));
    }

    /// <summary>Resolves Inter without registering the Inter font collection.</summary>
    [Fact]
    public async Task InterAssetUriNeedsNoRegistration()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(UnregisteredInterApplication));
        await session.Dispatch(() =>
        {
            var family = new FontFamily(InterFamily);
            foreach (var weight in new[] { FontWeight.Normal, FontWeight.SemiBold })
            {
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(family, weight: weight), out var typeface));
                string file = weight == FontWeight.Normal ? "Inter-Regular.ttf" : "Inter-SemiBold.ttf";
                string familyName = weight == FontWeight.Normal ? "Inter" : "Inter SemiBold";
                AssertTypeface(typeface, familyName, weight, $"avares://Avalonia.Fonts.Inter/Assets/{file}");
            }
        }, TestContext.Current.CancellationToken);
    }

    private static T Resource<T>(string key)
    {
        Assert.NotNull(Application.Current);
        Assert.True(Application.Current.Resources.TryGetResource(key, null, out var value), $"Missing resource: {key}");
        return Assert.IsType<T>(value);
    }

    private static TextLayout CreateLayout(string role, string text)
    {
        var control = new TextBlock
        {
            Text = text,
            FontFamily = Resource<FontFamily>($"Nvt.Font.{role}.Family"),
            FontSize = Resource<double>($"Nvt.Font.{role}.Size"),
            FontWeight = Resource<FontWeight>($"Nvt.Font.{role}.Weight"),
        };
        control.Measure(Size.Infinity);
        return control.TextLayout;
    }

    private static void AssertTypeface(GlyphTypeface typeface, string family, FontWeight weight, string assetUri)
    {
        Assert.Equal(family, typeface.FamilyName);
        Assert.Equal(weight, typeface.Weight);
        Assert.Equal(FontSimulations.None, typeface.FontSimulations);
        Assert.Equal(FontSimulations.None, typeface.PlatformTypeface.FontSimulations);
        Assert.Equal(weight, typeface.PlatformTypeface.Weight);
        Assert.True(typeface.PlatformTypeface.TryGetStream(out var stream));
        using (stream)
        using (var asset = AssetLoader.Open(new Uri(assetUri)))
        {
            Assert.Equal(SHA256.HashData(asset), SHA256.HashData(stream));
        }
    }

    /// <summary>Uses Skia without calling WithInterFont.</summary>
    public sealed class UnregisteredInterApplication : Application
    {
        /// <summary>Builds an isolated application without font collection registration.</summary>
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<UnregisteredInterApplication>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .With(new FontManagerOptions
                {
                    DefaultFamilyName = InterFamily,
                });
    }
}
