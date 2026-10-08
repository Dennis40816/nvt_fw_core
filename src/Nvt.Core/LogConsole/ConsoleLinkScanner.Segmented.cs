// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Nvt.Core.LogConsole;

public static partial class ConsoleLinkScanner
{
    // The cursor and result list belong only to this invocation. No app reads run under a lock.
    // Candidate ranges survive read boundaries. Only confirmed targets become strings.
    internal static ImmutableArray<ConsoleLinkSpan> Scan(ILogTextContent content)
    {
        var text = new SegmentedCursor(content);
        var spans = new List<ConsoleLinkSpan>();
        for (var start = 0; start < text.Length; start++)
        {
            if (start != 0 && IsUrlWord(text.Before(start))) continue;
            var prefix = text.Matches(start, "https://") ? 8 : text.Matches(start, "http://") ? 7
                : text.Matches(start, "ftp://") ? 6 : 0;
            if (prefix == 0) continue;
            var end = TargetEnd(start + prefix);
            if (end == start + prefix) continue;
            Add(start, end - start, new LinkTarget(LinkKind.Url, TrimTarget(text.Read(start, end - start))));
            start = end - 1;
        }
        for (var start = 0; start < text.Length; start++)
        {
            var quote = text.At(start);
            if (quote is not ('\'' or '"')) continue;
            var end = start + 1;
            var hasSeparator = false;
            while (end < text.Length && text.At(end) != quote && text.At(end) is not ('\r' or '\n'))
            {
                hasSeparator |= IsSeparator(text.At(end));
                end++;
            }
            if (end == text.Length || text.At(end) is '\r' or '\n') { start = end - 1; continue; }
            var suffixEnd = LocationEnd(end + 1);
            if (end > start + 1 && (hasSeparator || suffixEnd != end + 1))
            {
                if (!Overlaps(start, suffixEnd - start))
                {
                    var path = text.Read(start + 1, end - start - 1);
                    var suffix = text.Read(end + 1, suffixEnd - end - 1);
                    Add(start, suffixEnd - start, ParsePath(path + suffix));
                }
                start = suffixEnd - 1;
            }
            else start = end;
        }
        for (var start = 0; start < text.Length; start++)
        {
            var prefix = IsDriveLetter(text.At(start)) && text.At(start + 1) == ':' && IsSeparator(text.At(start + 2)) ? 3
                : text.At(start) == '\\' && text.At(start + 1) == '\\' ? 2 : 0;
            if (prefix == 0 || !IsPathStart(text.ScalarAt(start), text.Before(start), absolutePrefix: true)) continue;
            var end = PathEnd(start + prefix);
            if (!Overlaps(start, end - start))
            {
                var value = TrimTarget(text.Read(start, end - start));
                Add(start, value.Length, ParsePath(value));
            }
            start = end - 1;
        }
        for (var start = 0; start < text.Length; start++)
        {
            if (!IsPathStart(text.ScalarAt(start), text.Before(start))) continue;
            var nameEnd = start;
            while (nameEnd < text.Length && IsPathName(text.ScalarAt(nameEnd)))
                nameEnd += text.ScalarAt(nameEnd).Utf16SequenceLength;
            var hasSeparator = IsSeparator(text.At(nameEnd));
            var end = hasSeparator ? PathEnd(nameEnd) : LocationEnd(nameEnd);
            if (!hasSeparator && (end == nameEnd || IsPathName(text.ScalarAt(end))))
            {
                start = nameEnd - 1;
                continue;
            }
            if (!Overlaps(start, end - start))
            {
                var value = TrimTarget(text.Read(start, end - start));
                Add(start, value.Length, ParsePath(value));
            }
            start = end - 1;
        }
        return spans.OrderBy(span => span.Start).ToImmutableArray();

        int TargetEnd(int offset)
        {
            while (offset < text.Length && !IsTargetBoundary(text.At(offset))) offset++;
            return offset;
        }
        int PathEnd(int offset)
        {
            while (offset < text.Length)
            {
                var value = text.ScalarAt(offset);
                if (!IsPathName(value) && !IsSeparator(text.At(offset))) break;
                offset += value.Utf16SequenceLength;
            }
            return LocationEnd(offset);
        }
        int LocationEnd(int offset)
        {
            var first = text.At(offset);
            if (first is not ('(' or ':')) return offset;
            var end = PositiveNumberEnd(offset + 1);
            if (end == offset + 1) return offset;
            if (text.At(end) == (first == '(' ? ',' : ':'))
            {
                var columnEnd = PositiveNumberEnd(end + 1);
                if (columnEnd == end + 1) return first == '(' ? offset : end;
                end = columnEnd;
            }
            if (first == '(') return text.At(end) == ')' ? end + 1 : offset;
            return end;
        }
        int PositiveNumberEnd(int offset)
        {
            if (text.At(offset) is < '1' or > '9') return offset;
            var end = offset + 1;
            while (text.At(end) is >= '0' and <= '9') end++;
            return end;
        }
        void Add(int start, int length, LinkTarget target)
        {
            if (target.Kind == LinkKind.Url) length = target.Path.Length;
            if (length == 0 || Overlaps(start, length)) return;
            spans.Add(new ConsoleLinkSpan(start, length, target));
        }
        bool Overlaps(int start, int length) => spans.Any(s => start < s.Start + s.Length && s.Start < start + length);
    }

    private static bool IsDriveLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    private static bool IsUrlWord(Rune value) => !IsCjk(value)
        && (IsPathName(value) && value.Value is not ('-' or '.')
            || Rune.GetUnicodeCategory(value) == UnicodeCategory.ConnectorPunctuation);
    private static bool IsCjk(Rune value) => value.Value is >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF
        or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x2FA1F or >= 0x30000 and <= 0x3347F;
    private static bool IsSeparator(char value) => value is '/' or '\\';
    private static bool IsTargetBoundary(char value) => char.IsWhiteSpace(value) || value is
        '<' or '>' or '"' or '\'' or '，' or '。' or '！' or '？' or '；' or '：' or '、' or '「' or '」'
        or '『' or '』' or '（' or '）' or '【' or '】' or '〈' or '〉' or '《' or '》';

    private sealed class SegmentedCursor(ILogTextContent content)
    {
        // Access is confined to one scan. This fixed buffer never retains a whole candidate.
        private readonly char[] _chunk = new char[1024];
        private int _start = -1;
        internal int Length { get; } = content.Length;

        internal char At(int offset)
        {
            if (offset < 0 || offset >= Length) return '\0';
            var start = offset / _chunk.Length * _chunk.Length;
            if (_start != start)
            {
                content.Read(start, _chunk.AsSpan(0, Math.Min(_chunk.Length, Length - start)));
                _start = start;
            }
            return _chunk[offset - start];
        }

        internal Rune ScalarAt(int offset)
        {
            var first = At(offset);
            if (char.IsHighSurrogate(first) && Rune.TryCreate(first, At(offset + 1), out var pair)) return pair;
            return Rune.TryCreate(first, out var scalar) ? scalar : Rune.ReplacementChar;
        }

        internal Rune Before(int offset) => ScalarAt(char.IsLowSurrogate(At(offset - 1))
            && char.IsHighSurrogate(At(offset - 2)) ? offset - 2 : offset - 1);

        internal bool Matches(int offset, string value)
        {
            if (offset > Length - value.Length) return false;
            for (var i = 0; i < value.Length; i++)
                if (char.ToLowerInvariant(At(offset + i)) != value[i]) return false;
            return true;
        }

        internal string Read(int offset, int length) => string.Create(length, (content, offset),
            static (destination, state) =>
            {
                for (var copied = 0; copied < destination.Length;)
                {
                    var count = Math.Min(1024, destination.Length - copied);
                    state.content.Read(state.offset + copied, destination.Slice(copied, count));
                    copied += count;
                }
            });
    }
}
