// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Choice;

/// <summary>Guards Avalonia checkbox cycles, radio grouping, directional input, and accessibility metadata.</summary>
public sealed class ChoiceBehaviorTests
{
    /// <summary>Preserves native two-state and three-state cycles when Space activates a checkbox.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CheckBoxCyclesMatchAvalonia(bool styles, bool threeState)
    {
        var control = new CheckBox { Content = "Include archived items", IsThreeState = threeState, IsChecked = false };
        Window host = Create(control, styles: styles);
        try
        {
            Show(host);
            Assert.True(control.Focus(NavigationMethod.Tab));
            bool?[] expected = threeState ? [true, null, false, true, null, false] : [true, false, true, false];
            foreach (bool? selected in expected)
            {
                KeyStroke(host, Key.Space);
                Assert.Equal(selected, control.IsChecked);
            }
            control.IsChecked = null;
            control.IsThreeState = false;
            KeyStroke(host, Key.Space);
            Assert.False(control.IsChecked);
        }
        finally { host.Close(); }
    }

    /// <summary>Preserves native directional focus and Space selection in vertical and horizontal StackPanel groups.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RadioArrowFocusAndSpaceSelectionMatchAvalonia(bool styles, bool horizontal)
    {
        RadioButton[] radios = [new() { Content = "First", IsChecked = true }, new() { Content = "Second" }, new() { Content = "Third" }];
        var panel = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(24), Spacing = 8,
            Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
        };
        foreach (RadioButton radio in radios) panel.Children.Add(radio);
        Window host = Create(panel, styles: styles);
        try
        {
            Show(host);
            Assert.All(radios, radio => Assert.Null(radio.GroupName));
            Assert.True(radios[0].Focus(NavigationMethod.Tab));
            Key next = horizontal ? Key.Right : Key.Down;
            Key previous = horizontal ? Key.Left : Key.Up;
            // Native keyboard XY navigation is opt-in. Choice styles must not silently change it.
            KeyStroke(host, next);
            Assert.Same(radios[0], host.FocusManager!.GetFocusedElement());
            Assert.True(radios[0].IsChecked);
            XYFocus.SetNavigationModes(panel, XYFocusNavigationModes.Keyboard);
            int checkedIndex = 0;
            foreach ((Key key, int focused) in new[] { (next, 1), (next, 2), (previous, 1), (previous, 0) })
            {
                KeyStroke(host, key);
                Assert.Same(radios[focused], host.FocusManager!.GetFocusedElement());
                // Avalonia 12.1.1 does not select a radio merely because an arrow moved focus.
                for (int index = 0; index < radios.Length; index++) Assert.Equal(index == checkedIndex, radios[index].IsChecked);
                KeyStroke(host, Key.Space);
                checkedIndex = focused;
                for (int index = 0; index < radios.Length; index++) Assert.Equal(index == checkedIndex, radios[index].IsChecked);
            }
        }
        finally { host.Close(); }
    }
    /// <summary>Named groups span panels while unnamed groups remain scoped to their parent.</summary>
    [AvaloniaFact]
    public void GroupNameAndDefaultGroupingKeepTheirNativeBoundaries()
    {
        var first = new RadioButton { Content = "First", IsChecked = true };
        var second = new RadioButton { Content = "Second" };
        var independent = new RadioButton { Content = "Independent", IsChecked = true };
        var namedFirst = new RadioButton { Content = "Named first", GroupName = "Review", IsChecked = true };
        var namedSecond = new RadioButton { Content = "Named second", GroupName = "Review" };
        var panel = new StackPanel { Children =
        {
            new StackPanel { Children = { first, second, namedFirst } },
            new StackPanel { Children = { independent, namedSecond } },
        } };
        Window host = Create(panel);
        try
        {
            Show(host);
            second.IsChecked = true;
            Assert.False(first.IsChecked);
            Assert.True(independent.IsChecked);
            namedSecond.IsChecked = true;
            Assert.False(namedFirst.IsChecked);
            Assert.True(second.IsChecked);
            Assert.True(independent.IsChecked);
            Assert.True(namedSecond.Focus(NavigationMethod.Tab));
            KeyStroke(host, Key.Space);
            Assert.True(namedSecond.IsChecked);
        }
        finally { host.Close(); }
    }

    /// <summary>Native automation peers retain supplied names and choice semantics after the template changes.</summary>
    [AvaloniaFact]
    public void AutomationNamesAndControlTypesRemainNative()
    {
        var check = new CheckBox { Content = "_Archive", IsChecked = true };
        var radio = new RadioButton { Content = "_Review", IsChecked = true };
        AutomationProperties.SetName(check, "Include archive");
        AutomationProperties.SetName(radio, "Review mode");
        Window host = Create(new StackPanel { Children = { check, radio } });
        try
        {
            Show(host);
            var checkPeer = global::Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(check)!;
            var radioPeer = global::Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(radio)!;
            Assert.Equal("Include archive", checkPeer.GetName());
            Assert.Equal("Review mode", radioPeer.GetName());
            Assert.Equal(global::Avalonia.Automation.Peers.AutomationControlType.CheckBox, checkPeer.GetAutomationControlType());
            Assert.Equal(global::Avalonia.Automation.Peers.AutomationControlType.RadioButton, radioPeer.GetAutomationControlType());
        }
        finally { host.Close(); }
    }
}
