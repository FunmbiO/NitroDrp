using System.Text;

namespace NitroDrp.Api.Services;

/// <summary>
/// Cleaning for text a person typed before it is stored, shown or put in an email. React escapes what the web page
/// shows and the email composer HTML-encodes, so this is the second layer: no control characters, and none of the
/// invisible or direction-flipping characters used to make one thing look like another ("photo‮gpj.exe" can
/// display as "photoexe.jpg").
/// </summary>
public static class TextSafety
{
    /// <summary>Direction overrides and isolates, zero-width space, word joiner, byte-order mark, soft hyphen.</summary>
    public static bool IsSpoofing(char c) =>
        c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩' or '​' or '⁠' or '﻿' or '­';

    /// <summary>One line: control characters become spaces, spoofing characters vanish. Not shortened.</summary>
    public static string SingleLine(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (IsSpoofing(c)) continue;
            sb.Append(char.IsControl(c) ? ' ' : c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// A short message: keeps line breaks and tabs (as \n and a space), drops every other control character and the
    /// spoofing characters. Emoji and other scripts are untouched.
    /// </summary>
    public static string Multiline(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\r') { if (i + 1 < s.Length && s[i + 1] == '\n') continue; sb.Append('\n'); continue; }
            if (c == '\n') { sb.Append('\n'); continue; }
            if (c == '\t') { sb.Append(' '); continue; }
            if (char.IsControl(c) || IsSpoofing(c)) continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
