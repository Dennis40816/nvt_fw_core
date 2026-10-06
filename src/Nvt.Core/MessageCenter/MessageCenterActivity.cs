// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.MessageCenter;

/// <summary>Defines the disclosure level of an admitted activity.</summary>
public enum MessageActivityImportance
{
    /// <summary>Shown without expanding debug activity.</summary>
    Important,

    /// <summary>Shown when debug activity is expanded.</summary>
    Debug,
}

/// <summary>Defines the filtering and visual severity of an activity.</summary>
public enum MessageActivitySeverity
{
    /// <summary>Neutral information.</summary>
    Information,

    /// <summary>Successful completion.</summary>
    Success,

    /// <summary>Operator attention is useful.</summary>
    Warning,

    /// <summary>An operation failed.</summary>
    Error,
}

/// <summary>Defines the severity filter applied after activity disclosure.</summary>
public enum MessageActivityFilter
{
    /// <summary>Includes every severity admitted by the disclosure setting.</summary>
    Important,

    /// <summary>Includes warning activities.</summary>
    Warnings,

    /// <summary>Includes error activities.</summary>
    Errors,
}

/// <summary>Contains host-supplied display strings for one activity row.</summary>
/// <remarks>A typed binding alias may derive from this record without adding display behavior.</remarks>
/// <param name="Time">The host-formatted observation time.</param>
/// <param name="Title">The host-formatted title.</param>
/// <param name="Detail">The host-formatted detail.</param>
/// <param name="Category">The host-formatted category.</param>
/// <param name="Status">The host-formatted status.</param>
/// <param name="Severity">The same severity as the activity metadata.</param>
public record MessageCenterActivityItem(
    string Time,
    string Title,
    string Detail,
    string Category,
    string Status,
    MessageActivitySeverity Severity)
{
    /// <summary>Gets whether the row represents information.</summary>
    public bool IsInformation => Severity == MessageActivitySeverity.Information;

    /// <summary>Gets whether the row represents success.</summary>
    public bool IsSuccess => Severity == MessageActivitySeverity.Success;

    /// <summary>Gets whether the row represents a warning.</summary>
    public bool IsWarning => Severity == MessageActivitySeverity.Warning;

    /// <summary>Gets whether the row represents an error.</summary>
    public bool IsError => Severity == MessageActivitySeverity.Error;

    /// <summary>Gets the complete accessible row text with the preserved punctuation and field order.</summary>
    public string AccessibleText => $"{Time}. {Title}. {Category}. {Detail}. {Status}.";
}

/// <summary>Contains admitted host activity metadata and its deferred display projection.</summary>
/// <remarks>
/// The host captures immutable entries and supplies all display strings. The projected row's
/// severity must agree with this metadata; projection does not add a validation exception.
/// </remarks>
/// <param name="Sequence">The host sequence used for descending display order with stable ties.</param>
/// <param name="Importance">The activity disclosure level.</param>
/// <param name="Severity">The activity filtering and visual severity.</param>
/// <param name="ProjectItem">Projects the captured immutable entry using the host's current text.</param>
public sealed record MessageCenterActivity(
    long Sequence,
    MessageActivityImportance Importance,
    MessageActivitySeverity Severity,
    Func<MessageCenterActivityItem> ProjectItem);
