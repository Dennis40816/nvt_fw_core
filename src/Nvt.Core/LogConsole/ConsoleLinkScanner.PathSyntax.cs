// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Text;

namespace Nvt.Core.LogConsole;

public static partial class ConsoleLinkScanner
{
    // Both entry points share these scalar rules through Scan(ILogTextContent).
    // Separators and location syntax are grammar tokens, not component name characters.
    private static bool IsPathName(Rune value) => Rune.GetUnicodeCategory(value) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
        or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
        or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
        or UnicodeCategory.DecimalDigitNumber || value.Value is '_' or '-' or '.';

    // The same boundary guards separator paths and filename-only location targets.
    // Recognized absolute prefixes retain the existing exception for adjacent CJK prose.
    private static bool IsPathStart(Rune value, Rune previous, bool absolutePrefix = false) =>
        (IsPathName(value) || value.Value == '/' || absolutePrefix && value.Value == '\\')
        && (absolutePrefix ? !IsUrlWord(previous)
            : !IsPathName(previous) && previous.Value is not ('/' or '\\' or ':'));
}
