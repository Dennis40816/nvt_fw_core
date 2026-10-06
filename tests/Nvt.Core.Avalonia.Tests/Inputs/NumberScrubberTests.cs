// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Nvt.Core.Avalonia.Inputs;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Inputs;

/// <summary>Characterizes the frozen NumberScrubber using synthetic values and routed input.</summary>
public sealed partial class NumberScrubberTests
{
    /// <summary>The original styled properties retain their defaults and Value's binding mode.</summary>
    [AvaloniaFact]
    public void StyledPropertiesKeepFrozenDefaults()
    {
        var control = new NumberScrubber();
        Assert.Equal(0m, control.Value);
        Assert.Equal(decimal.MinValue, control.Minimum);
        Assert.Equal(decimal.MaxValue, control.Maximum);
        Assert.Equal(1m, control.SmallChange);
        Assert.Equal(10m, control.LargeChange);
        Assert.Equal("0.###", control.FormatString);
        Assert.True(control.RequireAltForWheel);
        Assert.True(control.SnapToStep);
        Assert.Equal(6d, control.ScrubPixelsPerStep);
        Assert.False(control.IsReadOnly);
        Assert.False(control.IsMixed);
        Assert.Null(control.ScrubHint);
        Assert.Equal(BindingMode.TwoWay,
            NumberScrubber.ValueProperty.GetMetadata<NumberScrubber>().DefaultBindingMode);
        Assert.Equal("0", Input(control).Text);
    }

    /// <summary>Parsing uses invariant Float syntax without grouping in every current culture.</summary>
    [AvaloniaTheory]
    [InlineData("12.3456", "12.3456", "12.346", "12.346")]
    [InlineData("+12.5", "12.5", "12.5", "12.5")]
    [InlineData("-12.5", "-12.5", "-12.5", "-12.5")]
    [InlineData(".5", "0.5", "0.5", "0.5")]
    [InlineData("1.", "1", "1", "1")]
    [InlineData("1.25e+2", "125", "125", "125")]
    [InlineData("-2E-2", "-0.02", "-0.02", "-0.02")]
    [InlineData(" \t+3.50\r\n", "3.5", "3.5", "3.5")]
    [InlineData("1,234", "7", "7", "7")]
    [InlineData("1,5", "7", "7", "7")]
    [InlineData("1 234", "7", "7", "7")]
    [InlineData("1.234,5", "7", "7", "7")]
    [InlineData("12-", "7", "7", "7")]
    [InlineData("NaN", "7", "7", "7")]
    [InlineData("Infinity", "7", "7", "7")]
    [InlineData("1e29", "7", "7", "7")]
    [InlineData("79228162514264337593543950336", "7", "7", "7")]
    [InlineData("-79228162514264337593543950336", "7", "7", "7")]
    [InlineData("", "7", "7", "7")]
    [InlineData("   ", "7", "7", "7")]
    [InlineData("oops", "7", "7", "7")]
    public void ParsingAndCommitKeepLiteralResults(string text, string expectedValue, string expectedDisplay,
        string expectedCommittedValue)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "de-DE", "zh-TW" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var control = new NumberScrubber { Value = 7m, SnapToStep = false };
                TextBox input = Input(control);
                FocusInput(input);
                Type(input, text);
                Assert.Equal(Parse(expectedValue), control.Value);
                Assert.Equal(text, input.Text);
                Assert.True(Press(input, Key.Enter).Handled);
                Drain();
                Assert.Equal(Parse(expectedCommittedValue), control.Value);
                Assert.Equal(expectedDisplay, input.Text);
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    /// <summary>Formatting retains custom formats, whitespace fallback and invariant separators.</summary>
    [AvaloniaTheory]
    [InlineData("0.###", "12.346")]
    [InlineData("0.00", "12.35")]
    [InlineData("+0.0;-0.0;0", "+12.3")]
    [InlineData("0.00E+00", "1.23E+01")]
    [InlineData("", "12.3456")]
    [InlineData("  ", "12.3456")]
    public void FormattingKeepsLiteralDisplay(string format, string expectedDisplay)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var control = new NumberScrubber { Value = 12.3456m, FormatString = format };
            Assert.Equal(12.3456m, control.Value);
            Assert.Equal(expectedDisplay, Input(control).Text);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    /// <summary>Each parsable edit snaps immediately; Escape preserves the live value changes.</summary>
    [AvaloniaFact]
    public void LiveEditsEnterAndEscapeKeepValueEventSequence()
    {
        var control = new NumberScrubber { SmallChange = 0.5m, Minimum = -2m, Maximum = 3m };
        var values = new List<decimal>();
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property == NumberScrubber.ValueProperty) { values.Add(control.Value); }
        };
        TextBox input = Input(control);
        FocusInput(input);
        Assert.Contains("focused", Root(control).Classes);
        Type(input, "1.24");
        Assert.Equal(1m, control.Value);
        Assert.Equal("1.24", input.Text);
        Type(input, "1.25");
        Assert.Equal(1.5m, control.Value);
        Assert.Equal("1.25", input.Text);
        Assert.True(Press(input, Key.Escape).Handled);
        Drain();
        Assert.Equal(1.5m, control.Value);
        Assert.Equal("1.5", input.Text);
        Type(input, "9");
        Assert.Equal(3m, control.Value);
        Assert.Equal("9", input.Text);
        Assert.True(Press(input, Key.Enter).Handled);
        Drain();
        Assert.Equal("3", input.Text);
        Type(input, "-9");
        Assert.Equal(-2m, control.Value);
        Type(input, "");
        Type(input, "invalid");
        Assert.Equal(-2m, control.Value);
        Press(input, Key.Escape);
        Drain();
        Assert.Equal("-2", input.Text);
        Assert.False(Press(input, Key.Space).Handled);
        Assert.Collection(values,
            value => Assert.Equal(1m, value),
            value => Assert.Equal(1.5m, value),
            value => Assert.Equal(3m, value),
            value => Assert.Equal(-2m, value));
    }

    /// <summary>Real focus loss commits, ends editing, refreshes text and removes the focused class.</summary>
    [AvaloniaFact]
    public void LostFocusCommitsAndEndsEditing()
    {
        var control = new NumberScrubber { SmallChange = 0.5m };
        var other = new TextBox();
        Window window = Host(new StackPanel { Children = { control, other } });
        try
        {
            window.Show();
            Assert.True(other.Focus());
            Assert.True(Input(control).Focus());
            Type(Input(control), "2.25");
            Assert.Equal(2.5m, control.Value);
            Assert.Equal("2.25", Input(control).Text);
            Assert.True(other.Focus());
            Drain();
            Assert.Equal(2.5m, control.Value);
            Assert.Equal("2.5", Input(control).Text);
            Assert.DoesNotContain("focused", Root(control).Classes);
            control.Value = 4m;
            Drain();
            Assert.Equal("4", Input(control).Text);
            Type(Input(control), "6");
            Assert.Equal(4m, control.Value);
        }
        finally { window.Close(); }
    }

    /// <summary>Empty and invalid edits keep the value and restore its display on commit or focus loss.</summary>
    [AvaloniaTheory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid")]
    public void EmptyAndInvalidInputLeaveValueUnchanged(string text)
    {
        var control = new NumberScrubber { Value = 8m };
        TextBox input = Input(control);
        FocusInput(input);
        Type(input, text);
        Assert.Equal(8m, control.Value);
        input.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        Drain();
        Assert.Equal(8m, control.Value);
        Assert.Equal("8", input.Text);
        FocusInput(input);
        Type(input, text);
        Press(input, Key.Enter);
        Drain();
        Assert.Equal(8m, control.Value);
        Assert.Equal("8", input.Text);
    }

    /// <summary>Enter keeps editing and reparses formatted text; lost focus ends editing before queued text changes.</summary>
    [AvaloniaFact]
    public void EnterEscapeAndLostFocusKeepFormattedTextFeedback()
    {
        var control = new NumberScrubber { SnapToStep = false };
        TextBox input = Input(control);
        var values = new List<decimal>();
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property == NumberScrubber.ValueProperty) { values.Add(control.Value); }
        };
        FocusInput(input);
        Type(input, "12.3456");
        Assert.Equal(12.3456m, control.Value);
        Press(input, Key.Enter);
        Drain();
        Assert.Equal(12.346m, control.Value);
        Assert.Equal("12.346", input.Text);
        Assert.Collection(values,
            value => Assert.Equal(12.3456m, value),
            value => Assert.Equal(12.346m, value));
        values.Clear();
        Type(input, "12.3456");
        Press(input, Key.Escape);
        Drain();
        Assert.Equal(12.346m, control.Value);
        Assert.Equal("12.346", input.Text);
        Assert.Collection(values,
            value => Assert.Equal(12.3456m, value),
            value => Assert.Equal(12.346m, value));
        values.Clear();
        Type(input, "12.3456");
        input.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        Drain();
        Assert.Equal(12.3456m, control.Value);
        Assert.Equal("12.346", input.Text);
        Assert.Collection(values, value => Assert.Equal(12.3456m, value));
    }

    private static TextBox Input(NumberScrubber control) => control.FindControl<TextBox>("InputBox")!;
    private static Border Root(NumberScrubber control) => control.FindControl<Border>("RootBorder")!;
    private static Border Area(NumberScrubber control) => control.FindControl<Border>("ScrubArea")!;
    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private static void Drain() => Dispatcher.UIThread.RunJobs();
    private static void FocusInput(TextBox input) =>
        input.RaiseEvent(new FocusChangedEventArgs(InputElement.GotFocusEvent));
    private static void Type(TextBox input, string text)
    {
        input.Text = text;
        Drain();
    }
    private static KeyEventArgs Press(TextBox input, Key key)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key };
        input.RaiseEvent(args);
        return args;
    }
    private static Window Host(Control content)
    {
        var window = new Window
        {
            Content = content, Width = 320, Height = 120,
            Template = new FuncControlTemplate<Window>((_, scope) => new VisualLayerManager
            {
                Name = "PART_VisualLayerManager",
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter", Content = content,
                }.RegisterInNameScope(scope),
            }.RegisterInNameScope(scope)),
        };
        var uri = new Uri("avares://Nvt.Core.Avalonia/Inputs/InputsStyles.axaml");
        window.Styles.Add(new StyleInclude(uri) { Source = uri });
        return window;
    }
}
