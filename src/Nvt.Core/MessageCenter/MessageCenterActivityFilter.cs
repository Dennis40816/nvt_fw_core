// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.MessageCenter;

/// <summary>Filters passive activity metadata before invoking host display projections.</summary>
public static class MessageCenterActivityFilter
{
    /// <summary>Applies disclosure, severity filtering, stable descending order, and row projection.</summary>
    /// <param name="activities">The admitted metadata in host order.</param>
    /// <param name="filter">The severity filter evaluated only for disclosed activities.</param>
    /// <param name="includeDebug">Whether to disclose debug activity.</param>
    /// <returns>A materialized row snapshot, with one projection per retained activity.</returns>
    /// <remarks>
    /// Hidden activities are never projected. Projection and enumeration failures propagate.
    /// The host must keep metadata and row severity consistent; no additional validation is performed.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="activities" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An invalid filter is evaluated for a disclosed activity.</exception>
    public static IReadOnlyList<MessageCenterActivityItem> Apply(
        IEnumerable<MessageCenterActivity> activities,
        MessageActivityFilter filter,
        bool includeDebug)
    {
        return
        [
            .. activities
                .Where(activity => includeDebug ||
                    activity.Importance == MessageActivityImportance.Important)
                .Where(activity => filter switch
                {
                    MessageActivityFilter.Important => true,
                    MessageActivityFilter.Warnings => activity.Severity == MessageActivitySeverity.Warning,
                    MessageActivityFilter.Errors => activity.Severity == MessageActivitySeverity.Error,
                    // NFC throws the parameterless exception here. Keep its message and null parameter name exactly.
#pragma warning disable CA2208
                    _ => throw new ArgumentOutOfRangeException(),
#pragma warning restore CA2208
                })
                .OrderByDescending(static activity => activity.Sequence)
                .Select(static activity => activity.ProjectItem()),
        ];
    }
}
