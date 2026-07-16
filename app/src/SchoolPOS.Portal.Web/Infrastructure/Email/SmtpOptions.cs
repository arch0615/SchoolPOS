namespace SchoolPOS.Portal.Web.Infrastructure.Email;

/// <summary>Configuración SMTP para el envío de correo del portal.</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    /// <summary>465 = SSL implícito; 587 = STARTTLS.</summary>
    public int Port { get; set; } = 465;

    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Tienda Escolar";

    /// <summary>Auto | SslOnConnect | StartTls | None. Auto decide por el puerto (465→SSL, resto→STARTTLS).</summary>
    public string Security { get; set; } = "Auto";
}
