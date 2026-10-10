// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

// ConsoleProjection owns only the leases in Rows; every other member is immutable metadata.
// WithPausedOrder carries every row and its exact lease once, so the returned projection
// becomes their sole owner. The input must neither escape nor be disposed after transfer.
// Transfers fresh leases without retaining them twice. Remap needs both memberships;
// reorder afterward so a dedupe change preserves pause order with one Project call.
internal static class ConsoleProjectionTransfer
{
    internal static ConsoleProjection WithPausedOrder(ConsoleProjection projection, ConsoleViewState state)
    {
        if (state.Follow is not ConsoleFollow.Paused paused || paused.Anchor.Generation != projection.Generation)
            return projection;
        var order = paused.Anchor.RowOrder.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
        var rows = projection.Rows.OrderBy(row => order.GetValueOrDefault(row.Id, int.MaxValue))
            .ThenBy(row => row.Id.Value).ToImmutableArray();
        var surviving = rows.Select(row => row.Id).ToHashSet();
        var anchor = paused.Anchor.RowId;
        var resolved = anchor is { } requested && surviving.Contains(requested) ? anchor
            : paused.Anchor.RowOrder.Skip(paused.Anchor.RowOrder.IndexOf(anchor ?? default) + 1)
                .Where(surviving.Contains).Select(id => (ConsoleRowId?)id).FirstOrDefault()
                ?? rows.Where(row => row.LastSequence >= paused.Anchor.Sequence).MinBy(row => row.LastSequence)?.Id
                ?? rows.LastOrDefault()?.Id;
        return new ConsoleProjection
        {
            Version = projection.Version,
            Generation = projection.Generation,
            LastSequence = projection.LastSequence,
            CapturedAt = projection.CapturedAt,
            TimeBase = projection.TimeBase,
            Rows = rows,
            LevelCounts = projection.LevelCounts,
            Sources = projection.Sources,
            SourceCounts = projection.SourceCounts,
            RetainedMembership = projection.RetainedMembership,
            EventCount = projection.EventCount,
            NewSincePauseCount = projection.NewSincePauseCount,
            EvictedCount = projection.EvictedCount,
            Deduplicate = projection.Deduplicate,
            ResolvedAnchorId = resolved,
        };
    }
}
