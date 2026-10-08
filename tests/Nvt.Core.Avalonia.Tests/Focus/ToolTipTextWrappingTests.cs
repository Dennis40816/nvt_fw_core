// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Focus;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Focus.ToolTipTestHost;

namespace Nvt.Core.Avalonia.Tests.Focus;

/// <summary>Checks wrapping, subscription ownership, live resources, and keyboard tooltip integration.</summary>
[Collection("ToolTip text wrapping registration")]
public sealed class ToolTipTextWrappingTests
{
    /// <summary>A nonblank string becomes one wrapping TextBlock and opens with the shared foreground.</summary>
    [AvaloniaFact]
    public void StringTipBecomesWrappingTextAndOpensHeadlessly()
    {
        ToolTipTextWrapping.Register();
        var target = new Button { Content = "Tooltip target" };
        var window = Create(target);
        try
        {
            ToolTip.SetTip(target, "  Tooltip text  ");
            var text = Assert.IsType<TextBlock>(ToolTip.GetTip(target));
            Assert.Equal("  Tooltip text  ", text.Text);
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            ToolTip tip = Open(window, target, text);
            Assert.Equal(ColorOf(tip.Foreground), ColorOf(text.Foreground));
            Assert.Equal(320, text.MaxWidth);
        }
        finally
        {
            ToolTip.SetIsOpen(target, false);
            window.Close();
            ToolTipTextWrapping.Unregister();
        }
    }

    /// <summary>Null, empty, and whitespace strings remain unchanged.</summary>
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void BlankTipsRemainUnchanged(string? value)
    {
        ToolTipTextWrapping.Register();
        try
        {
            var target = new Button();
            ToolTip.SetTip(target, value);
            Assert.Same(value, ToolTip.GetTip(target));
        }
        finally { ToolTipTextWrapping.Unregister(); }
    }

    /// <summary>Existing custom controls and other non-string content retain their identity and properties.</summary>
    [AvaloniaFact]
    public void NonStringTipsRemainUnchanged()
    {
        ToolTipTextWrapping.Register();
        try
        {
            var customText = new TextBlock { Text = "Custom content", TextWrapping = TextWrapping.NoWrap };
            object[] tips = [customText, new Border(), new ToolTip(), new object(), 42];
            foreach (object value in tips)
            {
                var target = new Button();
                ToolTip.SetTip(target, value);
                Assert.Same(value, ToolTip.GetTip(target));
            }
            Assert.Equal(TextWrapping.NoWrap, customText.TextWrapping);
        }
        finally { ToolTipTextWrapping.Unregister(); }
    }

    /// <summary>Two registrations perform one replacement; one unregister removes it and permits re-registration.</summary>
    [AvaloniaFact]
    public void RegisterTwiceInstallsOneHandlerAndUnregisterIsIdempotent()
    {
        ToolTipTextWrapping.Register();
        ToolTipTextWrapping.Register();
        try
        {
            var target = new Button();
            int replacements = 0;
            target.PropertyChanged += (_, change) =>
            {
                if (change.Property == ToolTip.TipProperty && change.NewValue is TextBlock) replacements++;
            };
            ToolTip.SetTip(target, "First tip");
            Assert.IsType<TextBlock>(ToolTip.GetTip(target));
            Assert.Equal(1, replacements);
            ToolTipTextWrapping.Unregister();
            ToolTip.SetTip(target, "Unregistered tip");
            Assert.Equal("Unregistered tip", ToolTip.GetTip(target));
            ToolTipTextWrapping.Unregister();
            ToolTipTextWrapping.Register();
            ToolTip.SetTip(target, "Registered again");
            Assert.Equal("Registered again", Assert.IsType<TextBlock>(ToolTip.GetTip(target)).Text);
            Assert.Equal(2, replacements);
        }
        finally { ToolTipTextWrapping.Unregister(); }
    }

    /// <summary>An already-open tooltip follows Light, Dark, and Light again through its foreground binding.</summary>
    [AvaloniaFact]
    public void CreatedTipForegroundFollowsThemeChangesAndTokenReplacement()
    {
        ToolTipTextWrapping.Register();
        var target = new Button { Content = "Theme target" };
        var window = Create(target);
        try
        {
            ToolTip.SetTip(target, "Live theme text");
            var text = Assert.IsType<TextBlock>(ToolTip.GetTip(target));
            ToolTip tip = Open(window, target, text);
            foreach (bool dark in new[] { false, true, false })
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                Flush(window);
                Assert.Same(text, ToolTip.GetTip(target));
                Assert.Equal(Color.Parse(dark ? "#E2E8F0" : "#1E293B"), ColorOf(text.Foreground));
                Assert.Equal(ColorOf(tip.Foreground), ColorOf(text.Foreground));
            }
            ToolTipTextWrapping.Unregister();
            var replacement = new SolidColorBrush(Color.Parse("#123456"));
            window.Resources["Nvt.ToolTip.ForegroundBrush"] = replacement;
            Flush(window);
            Assert.Same(replacement, text.Foreground);
            Assert.Same(replacement, tip.Foreground);
        }
        finally
        {
            ToolTip.SetIsOpen(target, false);
            window.Close();
            ToolTipTextWrapping.Unregister();
        }
    }

    /// <summary>Long text spans multiple lines within the default width and a live width override.</summary>
    [AvaloniaFact]
    public void LongTipWrapsWithinMaxWidthAndFollowsWidthChanges()
    {
        ToolTipTextWrapping.Register();
        var target = new Button { Content = "Long text target" };
        var window = Create(target);
        try
        {
            ToolTip.SetTip(target, string.Join(' ', Enumerable.Repeat("Long tooltip text with useful context.", 12)));
            var text = Assert.IsType<TextBlock>(ToolTip.GetTip(target));
            _ = Open(window, target, text);
            Assert.Equal(320, text.MaxWidth);
            Assert.InRange(text.Bounds.Width, 1, 320);
            int initialLines = text.TextLayout.TextLines.Count;
            Assert.True(initialLines > 1);
            window.Resources["Nvt.ToolTip.MaxWidth"] = 160d;
            Flush(window);
            Assert.Equal(160, text.MaxWidth);
            Assert.InRange(text.Bounds.Width, 1, 160);
            Assert.True(text.TextLayout.TextLines.Count > initialLines);
        }
        finally
        {
            ToolTip.SetIsOpen(target, false);
            window.Close();
            ToolTipTextWrapping.Unregister();
        }
    }

    /// <summary>Replacing a bound string preserves subsequent binding updates.</summary>
    [AvaloniaFact]
    public void BoundTipContinuesUpdatingAfterConversion()
    {
        ToolTipTextWrapping.Register();
        try
        {
            var source = new TextBox { Text = "First bound tip" };
            var target = new Button();
            _ = target.Bind(ToolTip.TipProperty, new Binding(nameof(TextBox.Text)) { Source = source });
            Assert.Equal("First bound tip", Assert.IsType<TextBlock>(ToolTip.GetTip(target)).Text);
            source.Text = "Second bound tip";
            Assert.Equal("Second bound tip", Assert.IsType<TextBlock>(ToolTip.GetTip(target)).Text);
            source.Text = " ";
            Assert.Equal(" ", ToolTip.GetTip(target));
        }
        finally { ToolTipTextWrapping.Unregister(); }
    }

    /// <summary>Keyboard focus opens normalized text and Escape closes it without moving focus.</summary>
    [AvaloniaFact]
    public void WrappedTipWorksWithFocusToolTipBehavior()
    {
        ToolTipTextWrapping.Register();
        var target = new Button { Content = "Keyboard target" };
        var window = Create(target);
        try
        {
            ToolTip.SetTip(target, "Keyboard help");
            FocusToolTipBehavior.SetIsEnabled(target, true);
            window.Show();
            Assert.True(target.Focus(NavigationMethod.Tab));
            Flush(window);
            Assert.True(ToolTip.GetIsOpen(target));
            Assert.IsType<TextBlock>(ToolTip.GetTip(target));
            var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            target.RaiseEvent(escape);
            Assert.True(escape.Handled);
            Assert.False(ToolTip.GetIsOpen(target));
            Assert.Same(target, window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            FocusToolTipBehavior.SetIsEnabled(target, false);
            window.Close();
            ToolTipTextWrapping.Unregister();
        }
    }
}
