// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.Avalonia.Threading;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Runs one test collection alone, because a test clears the shared dispatcher registration.</summary>
[CollectionDefinition(nameof(UiThreadRegistryIsolation), DisableParallelization = true)]
public sealed class UiThreadRegistryIsolation;

/// <summary>Runs every case against Core's resolver and the frozen NFH copy, which must agree.</summary>
[Collection(nameof(UiThreadRegistryIsolation))]
public sealed class UiResourceResolverTests
{
    private const string Core = "Core";
    private const string FrozenNfh = "FrozenNfh";
    private const string ThemedColorKey = "Test.ThemedColor";
    private static readonly Color LightColor = Color.Parse("#FF102030");
    private static readonly Color DarkColor = Color.Parse("#FF405060");

    /// <summary>A control in a window resolves its own theme dictionary for Light and Dark.</summary>
    [AvaloniaTheory]
    [InlineData(Core)]
    [InlineData(FrozenNfh)]
    public void ControlInWindowUsesItsThemeVariant(string version)
    {
        var resolver = Resolver.For(version);
        var (window, owner) = ShowThemedWindow();
        try
        {
            window.RequestedThemeVariant = ThemeVariant.Light;
            Assert.Equal(LightColor, resolver.GetColor(owner, ThemedColorKey));
            Assert.Equal(ExpectedApplicationColor("NfcSurfaceBrush", ThemeVariant.Light),
                resolver.GetColor(owner, "NfcSurfaceBrush"));

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Assert.Equal(DarkColor, resolver.GetColor(owner, ThemedColorKey));
            Assert.Equal(ExpectedApplicationColor("NfcSurfaceBrush", ThemeVariant.Dark),
                resolver.GetColor(owner, "NfcSurfaceBrush"));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Brush resources return as they are. Color resources go through the factory once.</summary>
    [AvaloniaTheory]
    [InlineData(Core)]
    [InlineData(FrozenNfh)]
    public void GetBrushReturnsBrushesAndBuildsBrushesFromColors(string version)
    {
        var resolver = Resolver.For(version);
        var brush = new SolidColorBrush(Colors.Teal);
        var fallback = new ImmutableSolidColorBrush(Colors.Magenta);
        var owner = Owner(("Brush", brush), ("Color", Colors.Orange), ("Number", 4.0));
        var factoryColors = new List<Color>();
        ISolidColorBrush Factory(Color color)
        {
            factoryColors.Add(color);
            return new ImmutableSolidColorBrush(color);
        }

        Assert.Same(brush, resolver.GetBrush(owner, "Brush", fallback, Factory));
        Assert.Empty(factoryColors);

        var created = Assert.IsType<ImmutableSolidColorBrush>(resolver.GetBrush(owner, "Color", fallback, Factory));
        Assert.Equal(Colors.Orange, created.Color);
        Assert.Equal([Colors.Orange], factoryColors);

        Assert.Same(fallback, resolver.GetBrush(owner, "Number", fallback, Factory));
        Assert.Same(fallback, resolver.GetBrush(owner, "Missing", fallback, Factory));
        Assert.Single(factoryColors);
    }

    /// <summary>Colors come from Color and SolidColorBrush resources only.</summary>
    [AvaloniaTheory]
    [InlineData(Core)]
    [InlineData(FrozenNfh)]
    public void ColorsComeFromColorsAndSolidColorBrushesOnly(string version)
    {
        var resolver = Resolver.For(version);
        var owner = Owner(
            ("Color", Colors.Orange),
            ("SolidBrush", new SolidColorBrush(Colors.Teal)),
            ("ImmutableBrush", new ImmutableSolidColorBrush(Colors.Navy)),
            ("Gradient", new LinearGradientBrush()),
            ("Number", 4.0));

        Assert.True(resolver.TryGetColor(owner, "Color", out var color));
        Assert.Equal(Colors.Orange, color);
        Assert.True(resolver.TryGetColor(owner, "SolidBrush", out color));
        Assert.Equal(Colors.Teal, color);

        foreach (var key in new[] { "ImmutableBrush", "Gradient", "Number", "Missing" })
        {
            Assert.False(resolver.TryGetColor(owner, key, out color));
            Assert.Equal(default, color);
            Assert.Equal(Colors.Lime, resolver.GetColor(owner, key, Colors.Lime));
        }

        Assert.Equal(default, resolver.GetColor(owner, "Missing"));
    }

    /// <summary>Doubles, floats and ints convert to double. Other values return the fallback.</summary>
    [AvaloniaTheory]
    [InlineData(Core)]
    [InlineData(FrozenNfh)]
    public void GetDoubleConvertsTheThreeNumberTypes(string version)
    {
        var resolver = Resolver.For(version);
        var owner = Owner(("Double", 2.25), ("Float", 1.5f), ("Int", 3), ("Long", 7L), ("Text", "5"));

        Assert.Equal(2.25, resolver.GetDouble(owner, "Double", -1));
        Assert.Equal(1.5, resolver.GetDouble(owner, "Float", -1));
        Assert.Equal(3.0, resolver.GetDouble(owner, "Int", -1));
        Assert.Equal(-1, resolver.GetDouble(owner, "Long", -1));
        Assert.Equal(-1, resolver.GetDouble(owner, "Text", -1));
        Assert.Equal(-1, resolver.GetDouble(owner, "Missing", -1));
        Assert.Equal(0.0, resolver.GetDouble(owner, "Missing"));
    }

    /// <summary>Corner radius and thickness need their exact types.</summary>
    [AvaloniaTheory]
    [InlineData(Core)]
    [InlineData(FrozenNfh)]
    public void CornerRadiusAndThicknessNeedTheirTypes(string version)
    {
        var resolver = Resolver.For(version);
        var owner = Owner(("Radius", new CornerRadius(3)), ("Thickness", new Thickness(1, 2, 3, 4)), ("Number", 4.0));
        var radiusFallback = new CornerRadius(9);
        var thicknessFallback = new Thickness(9);

        Assert.Equal(new CornerRadius(3), resolver.GetCornerRadius(owner, "Radius", radiusFallback));
        Assert.Equal(radiusFallback, resolver.GetCornerRadius(owner, "Thickness", radiusFallback));
        Assert.Equal(radiusFallback, resolver.GetCornerRadius(owner, "Missing", radiusFallback));
        Assert.Equal(new Thickness(1, 2, 3, 4), resolver.GetThickness(owner, "Thickness", thicknessFallback));
        Assert.Equal(thicknessFallback, resolver.GetThickness(owner, "Number", thicknessFallback));
        Assert.Equal(thicknessFallback, resolver.GetThickness(owner, "Missing", thicknessFallback));
    }

    /// <summary>
    /// A control outside any tree has a null theme, so the lookup uses the Default variant.
    /// The application step finds top-level keys but not keys that exist only in Light and Dark.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(Core)]
    [InlineData(FrozenNfh)]
    public void ControlOutsideATreeUsesTheDefaultVariant(string version)
    {
        var resolver = Resolver.For(version);
        UiThread.RegisterRunningDispatcher(Dispatcher.UIThread);
        var owner = new Border();

        Assert.Null(owner.ActualThemeVariant);
        Assert.Equal(new CornerRadius(999), resolver.GetCornerRadius(owner, "NfcPillCornerRadius", default));
        Assert.Equal(8.0, resolver.GetDouble(owner, "NfcSpace8", -1));
        Assert.Equal(Colors.Lime, resolver.GetColor(owner, "NfcSurfaceBrush", Colors.Lime));
    }

    /// <summary>Without a registered dispatcher, a control outside any tree gets only fallbacks.</summary>
    [AvaloniaTheory]
    [InlineData(Core)]
    [InlineData(FrozenNfh)]
    public void ApplicationStepNeedsARegisteredDispatcher(string version)
    {
        var resolver = Resolver.For(version);
        var registration = typeof(UiThread).GetField("s_runningDispatcher", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(registration);
        var owner = new Border();
        try
        {
            registration.SetValue(null, null);
            Assert.False(UiThread.IsCurrent(out _, out _));
            Assert.Equal(new CornerRadius(5), resolver.GetCornerRadius(owner, "NfcPillCornerRadius", new CornerRadius(5)));
            Assert.Equal(-1, resolver.GetDouble(owner, "NfcSpace8", -1));
        }
        finally
        {
            UiThread.RegisterRunningDispatcher(Dispatcher.UIThread);
        }

        Assert.Equal(new CornerRadius(999), resolver.GetCornerRadius(owner, "NfcPillCornerRadius", new CornerRadius(5)));
    }

    private static Color ExpectedApplicationColor(string key, ThemeVariant theme)
    {
        var application = global::Avalonia.Application.Current;
        Assert.NotNull(application);
        Assert.True(application.Resources.TryGetResource(key, theme, out var value));
        return Assert.IsType<SolidColorBrush>(value).Color;
    }

    private static (Window Window, Border Owner) ShowThemedWindow()
    {
        var owner = new Border();
        var window = new Window { Content = owner };
        var light = new ResourceDictionary { [ThemedColorKey] = LightColor };
        var dark = new ResourceDictionary { [ThemedColorKey] = DarkColor };
        window.Resources.ThemeDictionaries[ThemeVariant.Light] = light;
        window.Resources.ThemeDictionaries[ThemeVariant.Dark] = dark;
        window.Show();
        return (window, owner);
    }

    private static Border Owner(params (string Key, object Value)[] resources)
    {
        var owner = new Border();
        foreach (var (key, value) in resources)
        {
            owner.Resources[key] = value;
        }

        return owner;
    }

    private abstract class Resolver
    {
        public static Resolver For(string version) => version switch
        {
            Core => new CoreResolver(),
            FrozenNfh => new FrozenNfhResolver(),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null),
        };

        public abstract IBrush GetBrush(Control owner, string key, IBrush fallback, Func<Color, ISolidColorBrush> factory);

        public abstract Color GetColor(Control owner, string key, Color fallback = default);

        public abstract bool TryGetColor(Control owner, string key, out Color color);

        public abstract double GetDouble(Control owner, string key, double fallback = 0.0);

        public abstract CornerRadius GetCornerRadius(Control owner, string key, CornerRadius fallback);

        public abstract Thickness GetThickness(Control owner, string key, Thickness fallback);
    }

    private sealed class CoreResolver : Resolver
    {
        public override IBrush GetBrush(Control owner, string key, IBrush fallback, Func<Color, ISolidColorBrush> factory) =>
            UiResourceResolver.GetBrush(owner, key, fallback, factory);

        public override Color GetColor(Control owner, string key, Color fallback = default) =>
            UiResourceResolver.GetColor(owner, key, fallback);

        public override bool TryGetColor(Control owner, string key, out Color color) =>
            UiResourceResolver.TryGetColor(owner, key, out color);

        public override double GetDouble(Control owner, string key, double fallback = 0.0) =>
            UiResourceResolver.GetDouble(owner, key, fallback);

        public override CornerRadius GetCornerRadius(Control owner, string key, CornerRadius fallback) =>
            UiResourceResolver.GetCornerRadius(owner, key, fallback);

        public override Thickness GetThickness(Control owner, string key, Thickness fallback) =>
            UiResourceResolver.GetThickness(owner, key, fallback);
    }

    private sealed class FrozenNfhResolver : Resolver
    {
        public override IBrush GetBrush(Control owner, string key, IBrush fallback, Func<Color, ISolidColorBrush> factory) =>
            FrozenNfhUiResourceResolver.GetBrush(owner, key, fallback, factory);

        public override Color GetColor(Control owner, string key, Color fallback = default) =>
            FrozenNfhUiResourceResolver.GetColor(owner, key, fallback);

        public override bool TryGetColor(Control owner, string key, out Color color) =>
            FrozenNfhUiResourceResolver.TryGetColor(owner, key, out color);

        public override double GetDouble(Control owner, string key, double fallback = 0.0) =>
            FrozenNfhUiResourceResolver.GetDouble(owner, key, fallback);

        public override CornerRadius GetCornerRadius(Control owner, string key, CornerRadius fallback) =>
            FrozenNfhUiResourceResolver.GetCornerRadius(owner, key, fallback);

        public override Thickness GetThickness(Control owner, string key, Thickness fallback) =>
            FrozenNfhUiResourceResolver.GetThickness(owner, key, fallback);
    }
}
