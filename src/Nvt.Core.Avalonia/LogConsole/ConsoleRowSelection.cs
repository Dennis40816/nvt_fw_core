// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

internal static class ConsoleRowSelection
{
    internal static bool Contains(ConsoleRow row, ConsoleViewState state)
        => row.MemberSequences.Any(state.Selection.Contains);
}
