// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.RuntimeQuery;

/// <summary>All startup calls, untouched tool arguments and startup issues.</summary>
/// <param name="Calls">Recognized calls in command-line order.</param>
/// <param name="RemainingArguments">Arguments for the tool's parser, unchanged and in original order.</param>
/// <param name="Issues">Grammar, confirmation and validator issues.</param>
public sealed record RuntimeQueryStartupParseResult(
    IReadOnlyList<RuntimeQueryStartupCall> Calls,
    IReadOnlyList<string> RemainingArguments,
    IReadOnlyList<RuntimeQueryStartupIssue> Issues);

/// <summary>A startup option and its exact issue message.</summary>
/// <param name="Option">The option name, including its two leading hyphens.</param>
/// <param name="Message">The message for the tool to display.</param>
public sealed record RuntimeQueryStartupIssue(string Option, string Message);

/// <summary>A startup call and the response returned by its handler or confirmation guard.</summary>
/// <param name="Call">The executed startup call.</param>
/// <param name="Response">The unchanged router response.</param>
public sealed record RuntimeQueryStartupCallResult(
    RuntimeQueryStartupCall Call,
    RuntimeQueryResponseEnvelope Response);
