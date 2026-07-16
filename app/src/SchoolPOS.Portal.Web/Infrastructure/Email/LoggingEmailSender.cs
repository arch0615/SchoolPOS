using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Portal.Web.Infrastructure.Email;

/// <summary>
/// Emisor de correo para desarrollo: no envía nada, solo registra el mensaje en el log (útil para
/// ver el enlace de restablecimiento sin un servidor SMTP configurado).
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        _logger.LogInformation("[CORREO-DEV] Para: {To} · Asunto: {Subject}\n{Body}", toEmail, subject, htmlBody);
        return Task.CompletedTask;
    }
}
