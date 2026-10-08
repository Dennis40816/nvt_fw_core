// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Nvt.Core.Lifecycle;

/// <summary>
/// Stores undo actions in LIFO order. The caller executes each popped action.
/// </summary>
public sealed class UndoService
{
    private readonly Stack<UndoAction> _stack = new();

    /// <summary>Gets whether the stack contains an undo action.</summary>
    public bool CanUndo => _stack.Count > 0;

    /// <summary>Pushes an undo action and its description without executing it.</summary>
    /// <param name="undo">The action for the caller to execute after popping.</param>
    /// <param name="description">The description stored with the action.</param>
    public void Push(Action undo, string description)
    {
        _stack.Push(new UndoAction(description, undo));
    }

    /// <summary>Pops the latest entry without executing it.</summary>
    /// <param name="action">The popped entry, or null when the stack is empty.</param>
    /// <returns>True when an entry was popped; otherwise false.</returns>
    public bool TryPop([NotNullWhen(true)] out UndoAction? action)
    {
        if (_stack.Count == 0)
        {
            action = null;
            return false;
        }

        action = _stack.Pop();
        return true;
    }
}

/// <summary>An undo description and the action to be executed by the caller.</summary>
/// <param name="Description">The stored description.</param>
/// <param name="Undo">The stored undo action.</param>
public sealed record UndoAction(string Description, Action Undo);
