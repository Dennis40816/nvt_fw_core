// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;

namespace Nvt.Core.LogConsole;

public static partial class ConsoleLinkScanner
{
    // Both entry points use this grammar through the shared segmented engine.
    // An opening needs a name boundary and nonempty path content. A same-type
    // opening takes priority over closing, except after CJK or a separator before CJK prose.
    private static QuoteBoundary QuoteBoundaryAt(SegmentedCursor text, int offset, char candidateQuote = '\0')
    {
        var quote = text.At(offset);
        if (quote is not ('\'' or '"') || candidateQuote != '\0' && quote != candidateQuote)
            return QuoteBoundary.None;
        // CJK components and separator-ending targets close before adjacent CJK prose.
        if (candidateQuote != '\0' && (IsCjk(text.Before(offset)) || IsSeparator(text.At(offset - 1)))
            && IsCjk(text.ScalarAt(offset + 1)))
            return QuoteBoundary.Close;
        if ((offset == 0 || !IsPathName(text.Before(offset)) || IsCjk(text.Before(offset)))
            && CanStartQuotedContent(text, offset + 1))
            return QuoteBoundary.Open;
        // Both location suffix openers, ':' and '(', are punctuation.
        if (candidateQuote != '\0' && (offset + 1 == text.Length || char.IsWhiteSpace(text.At(offset + 1))
            || Rune.IsPunctuation(text.ScalarAt(offset + 1)) || IsCjk(text.ScalarAt(offset + 1))))
            return QuoteBoundary.Close;
        return QuoteBoundary.None;
    }

    private static bool CanStartQuotedContent(SegmentedCursor text, int offset)
    {
        // A lone dot after a closing quote is prose punctuation; leading dots
        // followed by a component or separator can start a relative target.
        while (text.At(offset) == '.') offset++;
        return IsPathName(text.ScalarAt(offset)) || IsSeparator(text.At(offset)) || text.At(offset) is '~' or '%' or '$';
    }

    private static bool TryQuotedRange(SegmentedCursor text, int opening, out int end, out bool hasSeparator)
    {
        var quote = text.At(opening);
        hasSeparator = false;
        for (end = opening + 1; end < text.Length && text.At(end) is not ('\r' or '\n'); end++)
        {
            var boundary = QuoteBoundaryAt(text, end, quote);
            // Abandoned and unpaired candidates leave the original opening unchanged.
            // The caller resumes at opening + 1, including any nested quote of either type.
            if (boundary == QuoteBoundary.Open) return false;
            if (boundary == QuoteBoundary.Close) return true;
            hasSeparator |= IsSeparator(text.At(end));
        }
        return false;
    }

    private enum QuoteBoundary { None, Open, Close }
}
