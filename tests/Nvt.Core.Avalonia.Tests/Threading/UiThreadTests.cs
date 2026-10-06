// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.Threading;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Threading;

/// <summary>Characterizes the UI dispatcher registry using the existing headless application.</summary>
public sealed class UiThreadTests
{
    /// <summary>Ports NFH's registered-dispatcher fallback when Avalonia's global slot is empty.</summary>
    [AvaloniaFact]
    public void TryGetRunningDispatcherWhenGlobalSlotIsEmptyUsesRegisteredDispatcher()
    {
        var dispatcher = Dispatcher.UIThread;
        UiThread.RegisterRunningDispatcher(dispatcher);
        var dispatcherField = typeof(Dispatcher).GetField("s_uiThread", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(dispatcherField);
        var original = Assert.IsType<Dispatcher>(dispatcherField.GetValue(null));
        Assert.Same(dispatcher, original);
        Assert.True(UiThread.TryGetRunningDispatcher(out var registered));
        Assert.Same(original, registered);

        try
        {
            dispatcherField.SetValue(null, null);
            Assert.True(UiThread.TryGetRunningDispatcher(out var actual));
            Assert.Same(original, actual);
            Assert.Null(dispatcherField.GetValue(null));
        }
        finally
        {
            dispatcherField.SetValue(null, original);
            UiThread.RegisterRunningDispatcher(dispatcher);
        }
    }

    /// <summary>All four inputs require both thread access and run-loop support.</summary>
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void IsUiThreadThatRunsALoopRequiresBothConditions(
        bool hasThreadAccess, bool dispatcherRunsLoops, bool expected)
    {
        try
        {
            Assert.Equal(expected, UiThread.IsUiThreadThatRunsALoop(hasThreadAccess, dispatcherRunsLoops));
        }
        finally
        {
            UiThread.RegisterRunningDispatcher(Dispatcher.UIThread);
        }
    }

    /// <summary>Null registration identifies the parameter and preserves the registered UI dispatcher.</summary>
    [AvaloniaFact]
    public void RegisterRunningDispatcherRejectsNull()
    {
        var dispatcher = Dispatcher.UIThread;
        UiThread.RegisterRunningDispatcher(dispatcher);
        try
        {
            Assert.Equal("dispatcher", Assert.Throws<ArgumentNullException>(
                () => UiThread.RegisterRunningDispatcher(null!)).ParamName);
            Assert.True(UiThread.TryGetRunningDispatcher(out var actual));
            Assert.Same(dispatcher, actual);
        }
        finally
        {
            UiThread.RegisterRunningDispatcher(dispatcher);
        }
    }

    /// <summary>The headless UI thread retrieves the same dispatcher it registered.</summary>
    [AvaloniaFact]
    public void TryGetRunningDispatcherReturnsRegisteredUiDispatcher()
    {
        var dispatcher = Dispatcher.UIThread;
        UiThread.RegisterRunningDispatcher(dispatcher);
        try
        {
            Assert.NotNull(global::Avalonia.Application.Current);
            Assert.True(dispatcher.SupportsRunLoops);
            Assert.True(dispatcher.CheckAccess());
            Assert.True(UiThread.TryGetRunningDispatcher(out var actual));
            Assert.Same(dispatcher, actual);
        }
        finally
        {
            UiThread.RegisterRunningDispatcher(dispatcher);
        }
    }

    /// <summary>The UI thread receives both the registered dispatcher and current application.</summary>
    [AvaloniaFact]
    public void IsCurrentReturnsDispatcherAndApplicationOnUiThread()
    {
        var dispatcher = Dispatcher.UIThread;
        UiThread.RegisterRunningDispatcher(dispatcher);
        try
        {
            Assert.True(UiThread.IsCurrent(out var actualDispatcher, out var application));
            Assert.Same(dispatcher, actualDispatcher);
            Assert.NotNull(application);
            Assert.Same(global::Avalonia.Application.Current, application);
        }
        finally
        {
            UiThread.RegisterRunningDispatcher(dispatcher);
        }
    }

    /// <summary>A background thread fails, keeps the registered dispatcher out value and gets no application.</summary>
    [AvaloniaFact]
    public async Task IsCurrentKeepsTheDispatcherOnBackgroundThread()
    {
        var dispatcher = Dispatcher.UIThread;
        UiThread.RegisterRunningDispatcher(dispatcher);
        try
        {
            await Task.Run(() =>
            {
                Assert.False(dispatcher.CheckAccess());
                Assert.True(UiThread.TryGetRunningDispatcher(out var registered));
                Assert.Same(dispatcher, registered);
                Assert.False(UiThread.IsCurrent(out var actualDispatcher, out var application));
                Assert.Same(dispatcher, actualDispatcher);
                Assert.Null(application);
            });
        }
        finally
        {
            UiThread.RegisterRunningDispatcher(dispatcher);
        }
    }
}
