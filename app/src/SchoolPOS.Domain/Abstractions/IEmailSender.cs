namespace SchoolPOS.Domain.Abstractions;

/// <summary>
/// Envío de correo transaccional (recuperación de contraseña, avisos). La implementación real
/// (SMTP) vive en el host del portal; en desarrollo se usa un emisor que solo registra en log.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}
