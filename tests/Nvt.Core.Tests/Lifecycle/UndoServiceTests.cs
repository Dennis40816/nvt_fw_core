// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Lifecycle;
using Xunit;

namespace Nvt.Core.Tests.Lifecycle;

/// <summary>Tests the stack behavior behind the frozen command and undo tests using synthetic state.</summary>
public sealed class UndoServiceTests
{
    /// <summary>A successful pop proves a nonnull action through the conditional nullable annotation.</summary>
    [Fact]
    public void SuccessfulPopAllowsActionWithoutNullSuppression()
    {
        var service = new UndoService();
        bool executed = false;
        service.Push(() => executed = true, "Restore");
        if (service.TryPop(out UndoAction? action))
        {
            Assert.Equal("Restore", action.Description);
            action.Undo();
        }
        Assert.True(executed);
        Assert.False(service.TryPop(out UndoAction? empty));
        Assert.Null(empty);
    }

    /// <summary>A caller can restore a changed value by invoking the popped action.</summary>
    [Fact]
    public void TryPopLetsTheCallerUndoAStateChange()
    {
        var service = new UndoService();
        var value = true;
        var original = value;
        value = !original;

        service.Push(() => value = original, "Restore value");

        Assert.True(service.CanUndo);
        Assert.True(service.TryPop(out var action));
        Assert.False(service.CanUndo);
        Assert.Equal(!original, value);

        action.Undo();

        Assert.Equal(original, value);
    }

    /// <summary>Popping the latest action preserves older entries and never runs either action.</summary>
    [Fact]
    public void TryPopReturnsActionsInLifoOrderAndKeepsEarlierEntries()
    {
        var service = new UndoService();
        var executionOrder = new List<int>();
        Action first = () => executionOrder.Add(1);
        Action second = () => executionOrder.Add(2);

        Assert.False(service.CanUndo);
        service.Push(first, "First change");
        service.Push(second, "Second change");

        Assert.True(service.TryPop(out var latest));
        Assert.Same(second, latest.Undo);
        Assert.Equal("Second change", latest.Description);
        Assert.True(service.CanUndo);
        Assert.Empty(executionOrder);

        latest.Undo();

        Assert.True(service.TryPop(out var earlier));
        Assert.Same(first, earlier.Undo);
        Assert.Equal("First change", earlier.Description);
        Assert.False(service.CanUndo);
        Assert.Equal(2, Assert.Single(executionOrder));

        earlier.Undo();

        Assert.Collection(
            executionOrder,
            item => Assert.Equal(2, item),
            item => Assert.Equal(1, item));
        Assert.False(service.TryPop(out var empty));
        Assert.Null(empty);
    }

    /// <summary>An empty stack returns false and supplies a null entry.</summary>
    [Fact]
    public void TryPopOnAnEmptyStackReturnsFalseAndNullAction()
    {
        var service = new UndoService();

        Assert.False(service.TryPop(out var action));
        Assert.Null(action);
        Assert.False(service.CanUndo);
    }

    /// <summary>Descriptions, including empty strings, whitespace and Unicode, are stored unchanged.</summary>
    /// <param name="description">The description to round-trip through the stack.</param>
    [Theory]
    [InlineData("")]
    [InlineData("  First change\nSecond line  ")]
    [InlineData("還原變更")]
    public void TryPopPreservesDescriptionAndAction(string description)
    {
        var service = new UndoService();
        Action undo = static () => { };

        service.Push(undo, description);

        Assert.True(service.TryPop(out var action));
        Assert.Equal(description, action.Description);
        Assert.Same(undo, action.Undo);
    }
}
