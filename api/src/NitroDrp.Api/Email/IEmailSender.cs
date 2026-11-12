namespace NitroDrp.Api.Email;

/// <param name="ReplyTo">Where replies go (the person who sent the files), or null.</param>
public sealed record EmailMessage(
    string To, string FromName, string FromAddress, string? ReplyTo, string Subject, string TextBody, string HtmlBody);

/// <summary>Sends one email. Behind an interface so the rules around it are testable without AWS.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>Used when email is switched off. Nothing should call it; the service refuses first.</summary>
public sealed class NoEmailSender : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct) =>
        throw new InvalidOperationException("Email sending is not enabled.");
}
