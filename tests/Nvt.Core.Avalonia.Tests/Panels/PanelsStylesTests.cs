// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Panels;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Panels;

/// <summary>Ports applicable NFH layout guards to the extracted styles.</summary>
public sealed class PanelsStylesTests
{
    /// <summary>Compiled styles follow both Core themes and the host's NFH token overrides.</summary>
    [AvaloniaFact]
    public void MappedResourcesRespondToThemeChangesAndHostOverrides()
    {
        var panel = new CollapsiblePanel { Content = new TextBlock { Text = "Panel body" } };
        var shell = new WorkspaceShell();
        Window host = PanelsTestHost.Create(new StackPanel { Children = { panel, shell } });
        try
        {
            Border root = PanelsTestHost.Find<Border>(panel, "panelBlockRoot");
            Grid main = Assert.Single(shell.GetVisualDescendants().OfType<Grid>(), grid => grid.ColumnSpacing == 12);
            host.RequestedThemeVariant = ThemeVariant.Light;
            Assert.True(Application.Current!.TryGetResource("NfcSurfaceSubtleBrush", ThemeVariant.Light, out object? light));
            Assert.Same(light, root.Background);
            host.RequestedThemeVariant = ThemeVariant.Dark;
            Assert.True(Application.Current.TryGetResource("NfcSurfaceSubtleBrush", ThemeVariant.Dark, out object? dark));
            Assert.Same(dark, root.Background);
            Assert.NotSame(light, dark);

            var mappedBrush = new SolidColorBrush(Colors.Purple);
            host.Resources["NfcSurfaceSubtleBrush"] = mappedBrush;
            host.Resources["NfcCompactCornerRadius"] = new CornerRadius(7);
            host.Resources["NfcSpace12"] = 20d;
            Assert.Same(mappedBrush, root.Background);
            Assert.Equal(new CornerRadius(7), root.CornerRadius);
            Assert.Equal(20, main.ColumnSpacing);
        }
        finally { host.Close(); }
    }

    /// <summary>Ports NFH's centralized-color and no-style-setter-collision concerns.</summary>
    [AvaloniaFact]
    public void StylesUseOnlyExistingDynamicCoreResourcesAndNoInlineHexColors()
    {
        XDocument styles = ReadStyles();
        string source = styles.ToString();
        Assert.DoesNotMatch(@"#[0-9A-Fa-f]{3,8}\b", source);
        Assert.DoesNotContain("{StaticResource", source, StringComparison.Ordinal);
        MatchCollection references = Regex.Matches(source, @"\{DynamicResource (?<key>[^}]+)\}", RegexOptions.CultureInvariant);
        Assert.NotEmpty(references);
        foreach (Match reference in references)
        {
            string key = reference.Groups["key"].Value;
            Assert.StartsWith("Nfc", key, StringComparison.Ordinal);
            Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Light, out _), key);
            Assert.True(Application.Current.TryGetResource(key, ThemeVariant.Dark, out _), key);
        }

        XNamespace avalonia = "https://github.com/avaloniaui";
        foreach (XElement style in styles.Descendants(avalonia + "Style"))
        {
            string[] properties = [.. style.Elements(avalonia + "Setter").Select(setter => setter.Attribute("Property")!.Value)];
            Assert.Equal(properties.Length, properties.Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <summary>Ports NFH's no-Expander and no-Bounds.Width-binding layout concerns.</summary>
    [AvaloniaFact]
    public void TemplatesKeepFlatPanelsAndAvoidBoundsWidthBindings()
    {
        string source = ReadStyles().ToString();
        Assert.DoesNotContain("<Expander", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("(Width|MaxWidth)\\s*=\\s*\"\\{Binding\\s+#.+?\\.Bounds\\.Width\\}\"", source);
        Assert.DoesNotContain("FontIcon", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IconGlyphs", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NotchExport", source, StringComparison.Ordinal);
    }

    private static XDocument ReadStyles()
    {
        string relativePath = Path.Combine("src", "Nvt.Core.Avalonia", "Panels", "PanelsStyles.axaml");
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, relativePath)))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return XDocument.Load(Path.Combine(directory.FullName, relativePath));
    }
}
