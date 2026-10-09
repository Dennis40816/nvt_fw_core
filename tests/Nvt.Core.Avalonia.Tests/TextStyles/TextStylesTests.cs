// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Nvt.Core.Avalonia.Tests.Forms;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.TextStyles;

/// <summary>Checks each opt-in role against its font tokens while preserving unrelated defaults.</summary>
public sealed class TextStylesTests
{
    /// <summary>Applies the complete role triplet to ordinary and selectable text in both themes.</summary>
    [AvaloniaTheory]
    [InlineData("title", "Title", false)]
    [InlineData("title", "Title", true)]
    [InlineData("heading", "Heading", false)]
    [InlineData("heading", "Heading", true)]
    [InlineData("body", "Body", false)]
    [InlineData("body", "Body", true)]
    [InlineData("caption", "Caption", false)]
    [InlineData("caption", "Caption", true)]
    [InlineData("mono", "Mono", false)]
    [InlineData("mono", "Mono", true)]
    [InlineData("numbers", "Numbers", false)]
    [InlineData("numbers", "Numbers", true)]
    [InlineData("bodyStrong", "BodyStrong", false)]
    [InlineData("bodyStrong", "BodyStrong", true)]
    [InlineData("captionStrong", "CaptionStrong", false)]
    [InlineData("captionStrong", "CaptionStrong", true)]
    [InlineData("monoStrong", "MonoStrong", false)]
    [InlineData("monoStrong", "MonoStrong", true)]
    [InlineData("monoCaption", "MonoCaption", false)]
    [InlineData("monoCaption", "MonoCaption", true)]
    public void TextClassMatchesFamilySizeAndWeightTokens(string name, string role, bool dark)
    {
        var text = new TextBlock { Text = "Sample text 0123456789" };
        var selectable = new SelectableTextBlock { Text = "Sample text 0123456789" };
        text.Classes.Add(name);
        selectable.Classes.Add(name);
        Window host = FormsTestHost.Create(new StackPanel { Children = { text, selectable } }, dark);
        try
        {
            FormsTestHost.Show(host);
            foreach (TextBlock control in new TextBlock[] { text, selectable })
            {
                Assert.True(control.TryFindResource($"Nvt.Font.{role}.Family", out object? family));
                Assert.True(control.TryFindResource($"Nvt.Font.{role}.Size", out object? size));
                Assert.True(control.TryFindResource($"Nvt.Font.{role}.Weight", out object? weight));
                Assert.Equal(Assert.IsType<FontFamily>(family), control.FontFamily);
                Assert.Equal(Assert.IsType<double>(size), control.FontSize);
                Assert.Equal(Assert.IsType<FontWeight>(weight), control.FontWeight);
                Assert.Equal(ResourceColor(control, "NfcTextBrush"), ColorOf(control.Foreground));
                Assert.True(double.IsNaN(control.LineHeight));
                FontFamily originalFamily = control.FontFamily;
                double originalSize = control.FontSize;
                FontWeight originalWeight = control.FontWeight;
                control.Classes.Add("muted");
                Assert.Equal(ResourceColor(control, "NfcTextMutedBrush"), ColorOf(control.Foreground));
                Assert.Equal(originalFamily, control.FontFamily);
                Assert.Equal(originalSize, control.FontSize);
                Assert.Equal(originalWeight, control.FontWeight);
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Leaves unclassed TextBlock, SelectableTextBlock, and Label font and line-height defaults intact.</summary>
    [AvaloniaFact]
    public void UnclassedDefaultsStayUnchanged()
    {
        var text = new TextBlock { Text = "Body" };
        var selectable = new SelectableTextBlock { Text = "Selectable" };
        var label = new Label { Content = "Label" };
        Window host = FormsTestHost.Create(new StackPanel { Children = { text, selectable, label } }, core: false);
        try
        {
            host.Show();
            Flush(host);
            var before = (text.FontFamily, text.FontSize, text.FontWeight, text.LineHeight,
                selectable.FontFamily, selectable.FontSize, selectable.FontWeight, selectable.LineHeight,
                label.FontFamily, label.FontSize, label.FontWeight);
            var uri = new Uri("avares://Nvt.Core.Avalonia/Theme/TextStyles.axaml");
            host.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(uri) { Source = uri });
            Flush(host);
            Assert.Equal(before, (text.FontFamily, text.FontSize, text.FontWeight, text.LineHeight,
                selectable.FontFamily, selectable.FontSize, selectable.FontWeight, selectable.LineHeight,
                label.FontFamily, label.FontSize, label.FontWeight));
            text.Classes.Add("muted");
            Assert.Equal(before.Item1, text.FontFamily);
            Assert.Equal(before.Item2, text.FontSize);
            Assert.Equal(before.Item3, text.FontWeight);
        }
        finally { host.Close(); }
    }
}
