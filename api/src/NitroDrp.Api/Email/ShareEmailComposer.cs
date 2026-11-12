using System.Globalization;
using System.Net;
using System.Text;

namespace NitroDrp.Api.Email;

/// <param name="SenderName">Who the files are from: the person's own name, or the brand if they gave none.</param>
/// <param name="BrandName">The configured name of this instance ("Jaded Media").</param>
/// <param name="FileNames">Left out of the email entirely when the link is password protected.</param>
/// <param name="Message">The sender's own words, plain text.</param>
public sealed record ShareEmailContext(
    string SenderName, string BrandName, string Link, int FileCount, long TotalBytes, DateTimeOffset ExpiresAt,
    bool PasswordProtected, IReadOnlyList<string> FileNames, string? Message);

public sealed record ComposedEmail(string Subject, string Text, string Html);

/// <summary>
/// Builds the email as pure text, so it is easy to test. Everything a sender typed is HTML-encoded: it is only
/// ever shown as text. The password is never in the email (the sender tells people separately), and a protected
/// link doesn't reveal its file names either.
/// </summary>
public static class ShareEmailComposer
{
    private const int MaxNamesListed = 10;

    public static ComposedEmail Compose(ShareEmailContext c)
    {
        var sender = OneLine(c.SenderName, 60);
        var brand = OneLine(c.BrandName, 60);
        var files = c.FileCount == 1 ? "1 file" : $"{c.FileCount} files";
        var size = FormatBytes(c.TotalBytes);
        var expires = c.ExpiresAt.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) + " (UTC)";
        var subject = $"{sender} sent you {files}";
        var message = string.IsNullOrWhiteSpace(c.Message) ? null : c.Message.Trim();
        var names = c.PasswordProtected ? [] : c.FileNames.Take(MaxNamesListed).Select(n => OneLine(n, 120)).ToList();
        var more = c.PasswordProtected ? 0 : Math.Max(0, c.FileNames.Count - MaxNamesListed);
        var passwordNote = $"This link is protected by a password. {sender} will give it to you separately; it isn't in this email.";
        var footer = $"Sent through {brand} at {sender}'s request. If you weren't expecting this you can ignore it: nothing is downloaded unless you open the link.";

        // plain text
        var t = new StringBuilder();
        t.AppendLine($"{sender} sent you {files} ({size}).").AppendLine();
        if (message is not null) t.AppendLine(message).AppendLine();
        t.AppendLine($"Download: {c.Link}").AppendLine();
        t.AppendLine($"The link expires on {expires}.");
        if (c.PasswordProtected) t.AppendLine().AppendLine(passwordNote);
        if (names.Count > 0)
        {
            t.AppendLine().AppendLine("Files:");
            foreach (var n in names) t.AppendLine($"  - {n}");
            if (more > 0) t.AppendLine($"  - and {more} more");
        }
        t.AppendLine().AppendLine("--").AppendLine(footer);

        // html: inline styles only (email clients ignore most CSS); every dynamic value is encoded
        string E(string s) => WebUtility.HtmlEncode(s);
        var h = new StringBuilder();
        h.Append("<!doctype html><html><body style=\"margin:0;padding:24px;background:#f4f4f4;font-family:Arial,Helvetica,sans-serif;color:#0a0a0a\">");
        h.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\">");
        h.Append("<table role=\"presentation\" width=\"520\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:520px;background:#ffffff;border:2px solid #0a0a0a;\"><tr><td style=\"padding:28px\">");
        h.Append($"<h1 style=\"margin:0 0 8px;font-size:22px;line-height:1.25\">{E(sender)} sent you {E(files)}</h1>");
        h.Append($"<p style=\"margin:0 0 20px;color:#555555\">{E(size)} in total</p>");
        if (message is not null)
            h.Append($"<p style=\"margin:0 0 20px;padding:12px 14px;background:#f4f4f4;border-left:4px solid #ff5a1f\">{E(message).Replace("\r\n", "\n").Replace("\n", "<br>")}</p>");
        h.Append($"<p style=\"margin:0 0 20px\"><a href=\"{E(c.Link)}\" style=\"display:inline-block;padding:14px 24px;background:#ff5a1f;color:#0a0a0a;font-weight:bold;text-decoration:none;border:2px solid #0a0a0a\">Download your files</a></p>");
        h.Append($"<p style=\"margin:0 0 6px;font-size:13px;color:#555555\">Or copy this link: {E(c.Link)}</p>");
        h.Append($"<p style=\"margin:0 0 16px;font-size:13px;color:#555555\">The link expires on {E(expires)}.</p>");
        if (c.PasswordProtected) h.Append($"<p style=\"margin:0 0 16px;font-size:14px\"><strong>Password protected.</strong> {E(sender)} will give you the password separately; it isn't in this email.</p>");
        if (names.Count > 0)
        {
            h.Append("<p style=\"margin:0 0 4px;font-size:13px;color:#555555\">Files:</p><ul style=\"margin:0 0 16px;padding-left:20px;font-size:14px\">");
            foreach (var n in names) h.Append($"<li>{E(n)}</li>");
            if (more > 0) h.Append($"<li>and {more} more</li>");
            h.Append("</ul>");
        }
        h.Append($"<p style=\"margin:24px 0 0;padding-top:16px;border-top:1px solid #dddddd;font-size:12px;color:#777777\">{E(footer)}</p>");
        h.Append("</td></tr></table></td></tr></table></body></html>");

        return new ComposedEmail(subject, t.ToString(), h.ToString());
    }

    /// <summary>One line, no control characters (so nothing can break a header), trimmed and capped.</summary>
    public static string OneLine(string s, int max)
    {
        var clean = Services.TextSafety.SingleLine(s);
        return clean.Length <= max ? clean : clean[..max].TrimEnd() + "…";
    }

    /// <summary>Matches the web app: 1.8 GB, 86 MB.</summary>
    public static string FormatBytes(long n)
    {
        if (n < 1000) return $"{n} B";
        string[] units = ["KB", "MB", "GB", "TB"];
        var v = n / 1000.0;
        var i = 0;
        while (v >= 1000 && i < units.Length - 1) { v /= 1000; i++; }
        var rounded = v >= 100 ? Math.Round(v) : Math.Round(v, 1);
        return $"{rounded.ToString(CultureInfo.InvariantCulture)} {units[i]}";
    }
}
