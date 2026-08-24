using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Portal.Web.Infrastructure.Email;

/// <summary>
/// Envía el CFDI de comisión (XML + PDF) recién timbrado al correo de facturación de la escuela.
/// Se invoca justo después de <c>ICommissionInvoiceService.IssueForPeriodAsync</c> — nunca antes:
/// el CFDI queda timbrado y guardado sin depender de que el correo salga. Si la escuela no tiene
/// <see cref="Domain.Entities.School.BillingEmail"/> configurado, se omite sin error: el CFDI
/// sigue disponible para descarga manual desde /Vendor/Invoices.
/// </summary>
public static class CommissionInvoiceNotifier
{
    public static async Task SendStampedAsync(
        SchoolDbContext db, IEmailSender email, CommissionInvoicePdfRenderer pdfRenderer,
        ILogger logger, Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await db.CommissionInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice is null || invoice.Status != CfdiStatus.Stamped)
            return;

        var school = await db.Schools.AsNoTracking().FirstOrDefaultAsync(s => s.Id == invoice.SchoolId, ct);
        if (school is null || string.IsNullOrWhiteSpace(school.BillingEmail))
            return;

        try
        {
            var body =
                $"<p>Se timbró el CFDI de comisión del periodo " +
                $"<strong>{invoice.PeriodFromUtc:dd/MM/yyyy}</strong> a " +
                $"<strong>{invoice.PeriodToUtc:dd/MM/yyyy}</strong> por " +
                $"<strong>{invoice.CommissionAmount:C2} {invoice.Currency}</strong>.</p>" +
                $"<p>Folio fiscal (UUID): {invoice.Uuid}</p>" +
                "<p>Se adjunta el XML timbrado y su representación en PDF.</p>";

            var attachments = new List<EmailAttachment>
            {
                new($"CFDI-{invoice.Uuid}.xml",
                    System.Text.Encoding.UTF8.GetBytes(invoice.StampedXml ?? string.Empty), "application/xml"),
                new($"CFDI-{invoice.Uuid}.pdf", pdfRenderer.Render(invoice, school), "application/pdf"),
            };

            await email.SendAsync(school.BillingEmail, "CFDI de comisión emitido", body, attachments, ct);
        }
        catch (Exception ex)
        {
            // Un aviso fallido nunca debe revertir ni ocultar que el CFDI sí se timbró: sigue
            // descargable a mano desde /Vendor/Invoices.
            logger.LogWarning(ex, "No se pudo enviar el CFDI {InvoiceId} por correo a {Email}", invoiceId, school.BillingEmail);
        }
    }
}
