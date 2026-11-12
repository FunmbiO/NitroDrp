using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;

namespace NitroDrp.Api.Email;

public sealed class SesEmailSender(IAmazonSimpleEmailServiceV2 ses, EmailOptions options) : IEmailSender
{
    public static IAmazonSimpleEmailServiceV2 CreateClient(EmailOptions o)
    {
        var cfg = new AmazonSimpleEmailServiceV2Config();
        if (!string.IsNullOrWhiteSpace(o.ServiceUrl))
        {
            cfg.ServiceURL = o.ServiceUrl;
            cfg.AuthenticationRegion = o.Region;
        }
        else
        {
            cfg.RegionEndpoint = RegionEndpoint.GetBySystemName(o.Region);
        }
        // Without explicit keys, fall back to the default AWS credential chain (the AWS_ACCESS_KEY_ID env vars).
        return !string.IsNullOrEmpty(o.AccessKey)
            ? new AmazonSimpleEmailServiceV2Client(new BasicAWSCredentials(o.AccessKey, o.SecretKey), cfg)
            : new AmazonSimpleEmailServiceV2Client(cfg);
    }

    public Task SendAsync(EmailMessage m, CancellationToken ct) =>
        ses.SendEmailAsync(new SendEmailRequest
        {
            FromEmailAddress = EmailAddressFormat.Display(m.FromName, m.FromAddress),
            Destination = new Destination { ToAddresses = [m.To] },
            ReplyToAddresses = m.ReplyTo is null ? [] : [m.ReplyTo],
            ConfigurationSetName = string.IsNullOrWhiteSpace(options.ConfigurationSet) ? null : options.ConfigurationSet,
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = m.Subject, Charset = "UTF-8" },
                    Body = new Body
                    {
                        Text = new Content { Data = m.TextBody, Charset = "UTF-8" },
                        Html = new Content { Data = m.HtmlBody, Charset = "UTF-8" },
                    },
                },
            },
        }, ct);

    /// <summary>Dev only: register the sender's domain with an emulator, which (like SES) refuses unknown senders.</summary>
    public async Task EnsureIdentityAsync(string domain, CancellationToken ct)
    {
        try
        {
            await ses.CreateEmailIdentityAsync(new CreateEmailIdentityRequest { EmailIdentity = domain }, ct);
        }
        catch (AlreadyExistsException)
        {
        }
    }
}
