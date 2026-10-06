// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Nvt.Core.Avalonia.Testing;
using Nvt.Core.Avalonia.Tests.Theme;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Testing;

/// <summary>Characterizes the shared host through the actual Avalonia test runner.</summary>
public sealed class AvaloniaTestHostTests
{
    /// <summary>Checks the runner's reflection lookup of the default empty application builder.</summary>
    [Fact]
    public void DefaultHostSupportsRunnerRegistration()
    {
        var method = typeof(AvaloniaTestHost).GetMethod(nameof(AvaloniaTestHost.BuildAvaloniaApp), Type.EmptyTypes);
        Assert.NotNull(method);
        var builder = Assert.IsType<AppBuilder>(method.Invoke(null, null));
        Assert.Equal(typeof(Application), builder.ApplicationType);
    }

    /// <summary>Preserves the source UI-thread assertion and the test assembly's theme application.</summary>
    [AvaloniaFact]
    public void ApplicationStartsOnTheUiThreadWithThemeResources()
    {
        Assert.True(Dispatcher.UIThread.CheckAccess());
        var application = Assert.IsType<ThemeTestApplication>(Application.Current);
        Assert.Null(application.ApplicationLifetime);
        Assert.Empty(application.Styles);
        Assert.Single(application.Resources.MergedDictionaries);
    }

    /// <summary>Dispatches background work back to the test's UI thread.</summary>
    [AvaloniaFact]
    public async Task DispatcherReturnsToTheTestUiThread()
    {
        int uiThread = Environment.CurrentManagedThreadId;
        int dispatchedThread = await Task.Run(async () =>
            await Dispatcher.UIThread.InvokeAsync(() => Environment.CurrentManagedThreadId),
            TestContext.Current.CancellationToken);

        Assert.Equal(uiThread, dispatchedThread);
        Assert.True(Dispatcher.UIThread.CheckAccess());
    }

    /// <summary>Shows, lays out and closes a synthetic window on the headless platform.</summary>
    [AvaloniaFact]
    public void WindowSupportsLayout()
    {
        var content = new Border
        {
            Width = 90,
            Height = 30,
            Margin = new Thickness(10, 15, 0, 0),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        };
        var window = new Window { Width = 320, Height = 240, Content = content };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.True(window.IsVisible);
            Assert.Equal(new Size(320, 240), window.ClientSize);
            Assert.Equal(new Rect(10, 15, 90, 30), content.Bounds);
        }
        finally
        {
            window.Close();
        }

        Assert.False(window.IsVisible);
    }

    /// <summary>Routes synthetic keyboard and Unicode text input to a focused control.</summary>
    /// <param name="text">The text delivered by the headless platform.</param>
    [AvaloniaTheory]
    [InlineData("Core")]
    [InlineData("測試")]
    [InlineData("🙂")]
    public void WindowRoutesKeyboardAndTextInput(string text)
    {
        var control = new Control { Focusable = true };
        var window = new Window { Width = 160, Height = 120, Content = control };
        Key? receivedKey = null;
        KeyModifiers receivedModifiers = KeyModifiers.None;
        string? receivedText = null;
        control.KeyDown += (_, args) =>
        {
            receivedKey = args.Key;
            receivedModifiers = args.KeyModifiers;
        };
        control.TextInput += (_, args) => receivedText = args.Text;
        try
        {
            window.Show();
            Assert.True(control.Focus());

            window.KeyPress(Key.F6, RawInputModifiers.Control, PhysicalKey.F6, null);
            window.KeyRelease(Key.F6, RawInputModifiers.Control, PhysicalKey.F6, null);
            window.KeyTextInput(text);

            Assert.Equal(Key.F6, receivedKey);
            Assert.Equal(KeyModifiers.Control, receivedModifiers);
            Assert.Equal(text, receivedText);
        }
        finally
        {
            window.Close();
        }
    }
}
