using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Portal.Web.Infrastructure.Email;

/// <summary>
/// Emisor de correo por SMTP usando MailKit. Soporta SSL implícito (puerto 465, como el servidor del
/// cliente) mediante <see cref="SecureSocketOptions.SslOnConnect"/>, además de STARTTLS.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(SmtpOptions options, ILogger<SmtpEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, ResolveSecurity(), ct);
        if (!string.IsNullOrEmpty(_options.User))
            await client.AuthenticateAsync(_options.User, _options.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);

        _logger.LogInformation("Correo enviado a {To}: {Subject}", toEmail, subject);
    }

    private SecureSocketOptions ResolveSecurity() => _options.Security switch
    {
        "SslOnConnect" => SecureSocketOptions.SslOnConnect,
        "StartTls" => SecureSocketOptions.StartTls,
        "None" => SecureSocketOptions.None,
        _ => _options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
    };
}
