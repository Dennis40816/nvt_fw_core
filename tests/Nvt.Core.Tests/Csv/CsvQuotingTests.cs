// Copyright (c) 2026 Dennis Liu. All rights reserved.
// Frozen source: Dennis40816/nvt-event-buffer-replay, origin/0.2.0, commit 915d0c1b571a2c4a95c8c6d2d3cc6421079ff99b
// src/Nvt.Replay.Rendering/AnalysisOutputWriter.cs:164-165
// src/Nvt.Replay.Rendering/ReadableCommunicationLogWriter.cs:123-124

using Nvt.Core.Csv;
using Xunit;

namespace Nvt.Core.Tests.Csv;

/// <summary>
/// Characterizes the identical CSV quoting helpers from the frozen NFU source.
/// </summary>
public sealed class CsvQuotingTests
{
    /// <summary>
    /// Compares each corpus value with a literal expected result from the frozen rule.
    /// </summary>
    /// <param name="value">The original field value.</param>
    /// <param name="expected">The literal expected CSV field.</param>
    [Theory]
    [InlineData("", "")]
    [InlineData("plain ASCII 123", "plain ASCII 123")]
    [InlineData("繁體中文 café 😀", "繁體中文 café 😀")]
    [InlineData("left,right", "\"left,right\"")]
    [InlineData("\"", "\"\"\"\"")]
    [InlineData("say \"hello\"", "\"say \"\"hello\"\"\"")]
    [InlineData("first\rsecond", "\"first\rsecond\"")]
    [InlineData("first\nsecond", "\"first\nsecond\"")]
    [InlineData("first\r\nsecond", "\"first\r\nsecond\"")]
    [InlineData(" 雪,\"quoted\"\r\nline\t; ", "\" 雪,\"\"quoted\"\"\r\nline\t; \"")]
    [InlineData("left\tright", "left\tright")]
    [InlineData("left;right", "left;right")]
    [InlineData(" leading", " leading")]
    [InlineData("trailing ", "trailing ")]
    [InlineData(" both ", " both ")]
    public void QuoteMatchesFrozenNfuCorpus(string value, string expected)
    {
        Assert.Equal(expected, CsvQuoting.Quote(value));
    }

    /// <summary>
    /// Verifies that null is rejected with the public parameter name.
    /// </summary>
    [Fact]
    public void QuoteRejectsNull()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => CsvQuoting.Quote(null!));

        Assert.Equal("value", exception.ParamName);
    }
}
