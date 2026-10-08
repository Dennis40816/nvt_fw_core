// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;

namespace Nvt.Core.LogConsole;

public static partial class ConsoleLinkScanner
{
    // Both entry points use this grammar through the shared segmented engine.
    // An opening needs a name boundary and nonempty path content. A same-type
    // opening takes priority over closing, even before punctuation in ./, ../ or UNC.
    private static QuoteBoundary QuoteBoundaryAt(SegmentedCursor text, int offset, char candidateQuote = '\0')
    {
        var quote = text.At(offset);
        if (quote is not ('\'' or '"') || candidateQuote != '\0' && quote != candidateQuote)
            return QuoteBoundary.None;
        if ((offset == 0 || !IsPathName(text.Before(offset))) && CanStartQuotedContent(text, offset + 1))
            return QuoteBoundary.Open;
        // Both location suffix openers, ':' and '(', are punctuation.
        if (candidateQuote != '\0' && (offset + 1 == text.Length || char.IsWhiteSpace(text.At(offset + 1))
            || Rune.IsPunctuation(text.ScalarAt(offset + 1))))
            return QuoteBoundary.Close;
        return QuoteBoundary.None;
    }

    private static bool CanStartQuotedContent(SegmentedCursor text, int offset)
    {
        // A lone dot after a closing quote is prose punctuation; leading dots
        // followed by a component or separator can start a relative target.
        while (text.At(offset) == '.') offset++;
        return IsPathName(text.ScalarAt(offset)) || IsSeparator(text.At(offset));
    }

    private enum QuoteBoundary { None, Open, Close }
}
