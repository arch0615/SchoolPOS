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

    public Task SendAsync(
        string toEmail, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment>? attachments = null, CancellationToken ct = default)
    {
        var attachmentNote = attachments is { Count: > 0 }
            ? $" (adjuntos: {string.Join(", ", attachments.Select(a => a.FileName))})"
            : string.Empty;
        _logger.LogInformation(
            "[CORREO-DEV] Para: {To} · Asunto: {Subject}{Attachments}\n{Body}",
            toEmail, subject, attachmentNote, htmlBody);
        return Task.CompletedTask;
    }
}
