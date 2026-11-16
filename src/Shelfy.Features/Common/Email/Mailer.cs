using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shelfy.Features.Common.Email;

/// <summary>One plain-text email to one person (ADR-012: email confirmation).</summary>
public record MailMessage(string To, string Subject, string Body);

/// <summary>Sends Shelfy's few emails. <c>Email:Provider</c> picks <see cref="SmtpMailer"/> ("Smtp") or <see cref="LogMailer"/> ("Log", the default).</summary>
public interface IMailer
{
    Task SendAsync(MailMessage message, CancellationToken ct = default);
}

public class EmailOptions
{
    public const string Section = "Email";

    /// <summary>"Log" (write the email to the server log; for development) or "Smtp".</summary>
    public string Provider { get; set; } = "Log";

    /// <summary>The site's public address used in links, e.g. https://shelfy.ppdac.ltd. Empty: taken from the request.</summary>
    public string PublicBaseUrl { get; set; } = "";

    public string From { get; set; } = "";
    public string FromName { get; set; } = "Shelfy";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "";
    public string SmtpPassword { get; set; } = "";

    /// <summary>STARTTLS. Leave on; Microsoft 365 (smtp.office365.com:587) requires it.</summary>
    public bool SmtpTls { get; set; } = true;
}

/// <summary>Development mailer: writes the email, links included, to the log instead of sending it.</summary>
public class LogMailer(ILogger<LogMailer> log) : IMailer
{
    public Task SendAsync(MailMessage message, CancellationToken ct = default)
    {
        log.LogWarning("Email not sent (Email:Provider is Log). To: {To}. Subject: {Subject}.\n{Body}", message.To, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}

/// <summary>Sends through an SMTP server with a login (for example Microsoft 365 with SMTP AUTH on the sending mailbox).</summary>
public class SmtpMailer(IOptions<EmailOptions> options) : IMailer
{
    public async Task SendAsync(MailMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        using var client = new SmtpClient(o.SmtpHost, o.SmtpPort)
        {
            EnableSsl = o.SmtpTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = string.IsNullOrEmpty(o.SmtpUser) ? null : new NetworkCredential(o.SmtpUser, o.SmtpPassword)
        };
        using var mail = new System.Net.Mail.MailMessage(new MailAddress(o.From, o.FromName), new MailAddress(message.To))
        {
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false
        };
        await client.SendMailAsync(mail, ct);
    }
}
