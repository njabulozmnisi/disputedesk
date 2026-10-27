using DisputeDesk.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DisputeDesk.Infrastructure.Sms;

/// <summary>
/// <see cref="ISmsSender"/> that delivers each SMS as an email to the configured SMTP host and nowhere else, so locally
/// every message lands in Mailpit and none can reach a real phone. The cellphone number becomes the recipient's
/// local part, for example <c>+27820000004@sms.disputedesk.test</c>.
/// </summary>
public sealed class SmtpSmsSender(IOptions<SmsOptions> options) : ISmsSender
{
    private const string From = "sms@disputedesk.test";
    private const string RecipientDomain = "sms.disputedesk.test";

    public async Task SendAsync(string cellphone, string message, CancellationToken cancellationToken)
    {
        using var mail = new MimeMessage();
        mail.From.Add(MailboxAddress.Parse(From));
        mail.To.Add(MailboxAddress.Parse($"{cellphone}@{RecipientDomain}"));
        mail.Subject = "DisputeDesk SMS";
        mail.Body = new TextPart("plain") { Text = message };

        SmsOptions sms = options.Value;
        // An OTP is only useful for 5 minutes, so give up on a stuck server after 10 seconds (MailKit's default is 2 minutes).
        using var smtp = new SmtpClient { Timeout = 10_000 }; // milliseconds
        await smtp.ConnectAsync(sms.Host, sms.Port, sms.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
        await smtp.SendAsync(mail, cancellationToken);
        await smtp.DisconnectAsync(quit: true, cancellationToken);
    }
}
