// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.MessageCenter;
using Xunit;

namespace Nvt.Core.Tests.MessageCenter;

/// <summary>Characterizes modal generation and pre-commit callbacks independently of host history and export I/O.</summary>
public sealed class SessionTests
{
    /// <summary>A new session is closed with activity selected and generation zero.</summary>
    [Fact]
    public void InitialStateIsClosedWithActivitySelectedAndGenerationZero()
    {
        var session = new MessageCenterSession();

        Assert.False(session.IsOpen);
        Assert.True(session.IsActivitySelected);
        Assert.Equal(0, session.ExportContextGeneration);
        Assert.False(session.IsExportContextCurrent(0));
    }

    /// <summary>Repeated open and close advance generation and invoke callbacks before visibility commits.</summary>
    [Fact]
    public void RepeatedOpenAndCloseAlwaysAdvanceBeforeCallbacks()
    {
        var session = new MessageCenterSession();
        int callbacks = 0;
        void Observe(long generation, bool oldVisibility)
        {
            callbacks++;
            Assert.Equal(generation, session.ExportContextGeneration);
            Assert.Equal(oldVisibility, session.IsOpen);
            Assert.True(session.IsActivitySelected);
            Assert.Equal(oldVisibility, session.IsExportContextCurrent(generation));
            Assert.False(session.IsExportContextCurrent(generation - 1));
        }

        session.Close(() => Observe(1, false));
        Assert.False(session.IsOpen);
        session.Open(() => Observe(2, false));
        Assert.True(session.IsOpen);
        session.Open(() => Observe(3, true));
        Assert.True(session.IsOpen);
        session.Close(() => Observe(4, true));
        Assert.False(session.IsOpen);
        session.Close(() => Observe(5, false));

        Assert.False(session.IsOpen);
        Assert.Equal(5, session.ExportContextGeneration);
        Assert.Equal(5, callbacks);
    }

    /// <summary>Pane callbacks observe the advanced generation and the previous selection while visibility stays open.</summary>
    [Fact]
    public void PaneChangesPreservePropertyChangingTiming()
    {
        var session = new MessageCenterSession();
        session.Open();
        int callbacks = 0;

        session.SelectActivity(false, () =>
        {
            callbacks++;
            Assert.Equal(2, session.ExportContextGeneration);
            Assert.True(session.IsOpen);
            Assert.True(session.IsActivitySelected);
            Assert.False(session.IsExportContextCurrent(1));
            Assert.True(session.IsExportContextCurrent(2));
        });
        Assert.False(session.IsActivitySelected);
        Assert.False(session.IsExportContextCurrent(2));
        session.SelectActivity(true, () =>
        {
            callbacks++;
            Assert.Equal(3, session.ExportContextGeneration);
            Assert.True(session.IsOpen);
            Assert.False(session.IsActivitySelected);
            Assert.False(session.IsExportContextCurrent(3));
        });

        Assert.True(session.IsActivitySelected);
        Assert.True(session.IsOpen);
        Assert.True(session.IsExportContextCurrent(3));
        Assert.Equal(2, callbacks);
    }

    /// <summary>Selecting the committed pane is a no-op in either visibility state and invokes no callback.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SamePaneSelectionDoesNotAdvanceOrInvokeCallback(bool isOpen, bool activitySelected)
    {
        MessageCenterSession session = CreateSession(isOpen, activitySelected);
        long generation = session.ExportContextGeneration;

        session.SelectActivity(activitySelected, static () => throw new InvalidOperationException("No-op callback"));

        Assert.Equal(generation, session.ExportContextGeneration);
        Assert.Equal(isOpen, session.IsOpen);
        Assert.Equal(activitySelected, session.IsActivitySelected);
    }

    /// <summary>Visibility changes keep the selected pane, including repeated open/close on the other pane.</summary>
    [Fact]
    public void OpenAndClosePreserveTheSelectedPane()
    {
        var session = new MessageCenterSession();
        session.SelectActivity(false);
        session.Open();
        session.Open();
        Assert.True(session.IsOpen);
        Assert.False(session.IsActivitySelected);
        Assert.False(session.IsExportContextCurrent(session.ExportContextGeneration));
        session.Close();
        session.Close();
        session.Open();

        Assert.True(session.IsOpen);
        Assert.False(session.IsActivitySelected);
        Assert.Equal(6, session.ExportContextGeneration);
    }

    /// <summary>Matching generation alone is insufficient; visibility and activity selection are also required.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CurrentContextRequiresOpenVisibilityAndActivitySelection(bool isOpen, bool activitySelected)
    {
        MessageCenterSession session = CreateSession(isOpen, activitySelected);

        Assert.Equal(isOpen && activitySelected, session.IsExportContextCurrent(session.ExportContextGeneration));
    }

    /// <summary>Negative, stale, future, and extreme generations never match the open generation-one context.</summary>
    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(2L)]
    [InlineData(long.MaxValue)]
    public void NonmatchingGenerationsAreStale(long generation)
    {
        var session = new MessageCenterSession();
        session.Open();

        Assert.False(session.IsExportContextCurrent(generation));
        Assert.True(session.IsExportContextCurrent(1));
    }

    /// <summary>The frozen close/reopen context assertions are preserved without a picker or exporter.</summary>
    [Fact]
    public void ClosedAndReopenedContextRejectsOldGeneration()
    {
        var session = new MessageCenterSession();
        session.Open();
        long captured = session.ExportContextGeneration;
        Assert.True(session.IsExportContextCurrent(captured));
        session.Close();
        Assert.False(session.IsExportContextCurrent(captured));
        Assert.False(session.IsExportContextCurrent(session.ExportContextGeneration));
        session.Open();

        Assert.False(session.IsExportContextCurrent(captured));
        Assert.True(session.IsExportContextCurrent(session.ExportContextGeneration));
        session.SelectActivity(false);
        long otherPane = session.ExportContextGeneration;
        Assert.False(session.IsExportContextCurrent(otherPane));
        session.SelectActivity(true);
        Assert.False(session.IsExportContextCurrent(otherPane));
        Assert.True(session.IsExportContextCurrent(session.ExportContextGeneration));
    }

    /// <summary>A deterministic delayed observation rejects the old context after close/reopen, retaining the source's ten-second gate.</summary>
    [Fact]
    public async Task DelayedObservationRejectsReopenedContext()
    {
        var session = new MessageCenterSession();
        session.Open();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> ObserveAsync(long generation)
        {
            entered.SetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
            return session.IsExportContextCurrent(generation);
        }
        Task<bool> observation = ObserveAsync(session.ExportContextGeneration);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            session.Close();
            session.Open();
        }
        finally
        {
            release.SetResult();
        }

        Assert.False(await observation);
        Assert.True(session.IsExportContextCurrent(session.ExportContextGeneration));
    }

    /// <summary>A throwing open callback preserves either prior visibility and the pane but retains its generation advance.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThrowingOpenCallbackKeepsCommittedStateAndAdvancedGeneration(bool isOpen)
    {
        MessageCenterSession session = CreateSession(isOpen, false);
        long previous = session.ExportContextGeneration;
        var failure = new InvalidOperationException("Synthetic open failure");

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
        {
            session.Open(() =>
            {
                Assert.Equal(previous + 1, session.ExportContextGeneration);
                Assert.Equal(isOpen, session.IsOpen);
                Assert.False(session.IsActivitySelected);
                throw failure;
            });
        });

        Assert.Same(failure, thrown);
        Assert.Equal(previous + 1, session.ExportContextGeneration);
        Assert.Equal(isOpen, session.IsOpen);
        Assert.False(session.IsActivitySelected);
        Assert.False(session.IsExportContextCurrent(previous));
    }

    /// <summary>A throwing close callback preserves either prior visibility and the pane but retains its generation advance.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThrowingCloseCallbackKeepsCommittedStateAndAdvancedGeneration(bool isOpen)
    {
        MessageCenterSession session = CreateSession(isOpen, true);
        long previous = session.ExportContextGeneration;
        var failure = new InvalidOperationException("Synthetic close failure");

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
        {
            session.Close(() =>
            {
                Assert.Equal(previous + 1, session.ExportContextGeneration);
                Assert.Equal(isOpen, session.IsOpen);
                Assert.True(session.IsActivitySelected);
                throw failure;
            });
        });

        Assert.Same(failure, thrown);
        Assert.Equal(previous + 1, session.ExportContextGeneration);
        Assert.Equal(isOpen, session.IsOpen);
        Assert.True(session.IsActivitySelected);
        Assert.False(session.IsExportContextCurrent(previous));
        Assert.Equal(isOpen, session.IsExportContextCurrent(session.ExportContextGeneration));
    }

    /// <summary>A throwing pane callback keeps the previous pane and visibility while invalidating the previous generation.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ThrowingSelectionCallbackKeepsCommittedStateAndAdvancedGeneration(bool isOpen, bool activitySelected)
    {
        MessageCenterSession session = CreateSession(isOpen, activitySelected);
        long previous = session.ExportContextGeneration;
        var failure = new InvalidOperationException("Synthetic selection failure");

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
        {
            session.SelectActivity(!activitySelected, () =>
            {
                Assert.Equal(previous + 1, session.ExportContextGeneration);
                Assert.Equal(isOpen, session.IsOpen);
                Assert.Equal(activitySelected, session.IsActivitySelected);
                throw failure;
            });
        });

        Assert.Same(failure, thrown);
        Assert.Equal(previous + 1, session.ExportContextGeneration);
        Assert.Equal(isOpen, session.IsOpen);
        Assert.Equal(activitySelected, session.IsActivitySelected);
        Assert.False(session.IsExportContextCurrent(previous));
        Assert.Equal(isOpen && activitySelected, session.IsExportContextCurrent(session.ExportContextGeneration));
    }

    /// <summary>Every advancing operation accepts the value below the maximum and the maximum, then overflows before invoking a callback.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void GenerationBoundaryIsCheckedBeforeCallbacksAndStateCommit(int operation)
    {
        var session = new MessageCenterSession(long.MaxValue - 2);
        int callbacks = 0;
        ApplyOperation(session, operation, () => callbacks++);
        Assert.Equal(long.MaxValue - 1, session.ExportContextGeneration);
        ApplyOperation(session, operation, () => callbacks++);
        Assert.Equal(long.MaxValue, session.ExportContextGeneration);
        bool oldVisibility = session.IsOpen;
        bool oldPane = session.IsActivitySelected;

        Assert.Throws<OverflowException>(() =>
        {
            ApplyOperation(session, operation, () =>
            {
                callbacks++;
                throw new InvalidOperationException("Overflow must precede this callback");
            });
        });

        Assert.Equal(2, callbacks);
        Assert.Equal(long.MaxValue, session.ExportContextGeneration);
        Assert.Equal(oldVisibility, session.IsOpen);
        Assert.Equal(oldPane, session.IsActivitySelected);
    }

    /// <summary>A callback can fail at the maximum without rolling back generation; the following operation overflows.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CallbackFailureAtMaximumKeepsTheAdvance(int operation)
    {
        var session = new MessageCenterSession(long.MaxValue - 1);
        var failure = new InvalidOperationException("Synthetic boundary failure");
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
        {
            ApplyOperation(session, operation, () =>
            {
                Assert.Equal(long.MaxValue, session.ExportContextGeneration);
                Assert.False(session.IsOpen);
                Assert.True(session.IsActivitySelected);
                throw failure;
            });
        });

        Assert.Same(failure, thrown);
        Assert.Equal(long.MaxValue, session.ExportContextGeneration);
        Assert.False(session.IsOpen);
        Assert.True(session.IsActivitySelected);
        Assert.Throws<OverflowException>(() => { ApplyOperation(session, operation); });
    }

    /// <summary>A same-pane selection still does nothing at the generation maximum for either pane.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SamePaneSelectionAtMaximumDoesNotOverflow(bool activitySelected)
    {
        var session = new MessageCenterSession(activitySelected ? long.MaxValue : long.MaxValue - 1);
        session.SelectActivity(activitySelected);

        session.SelectActivity(activitySelected, static () => throw new InvalidOperationException("No-op callback"));

        Assert.Equal(long.MaxValue, session.ExportContextGeneration);
        Assert.Equal(activitySelected, session.IsActivitySelected);
        Assert.False(session.IsOpen);
    }

    private static MessageCenterSession CreateSession(bool isOpen, bool activitySelected)
    {
        var session = new MessageCenterSession();
        session.SelectActivity(activitySelected);
        if (isOpen)
        {
            session.Open();
        }
        return session;
    }

    private static void ApplyOperation(MessageCenterSession session, int operation, Action? callback = null)
    {
        switch (operation)
        {
            case 0:
                session.Open(callback);
                break;
            case 1:
                session.Close(callback);
                break;
            case 2:
                session.SelectActivity(!session.IsActivitySelected, callback);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}
