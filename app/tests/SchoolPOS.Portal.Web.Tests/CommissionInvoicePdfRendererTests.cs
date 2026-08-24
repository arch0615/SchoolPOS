using FluentAssertions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Tests;

/// <summary>
/// El renderer de PDF es código nuevo con riesgo real de fallar solo en tiempo de ejecución (el
/// resolutor de fuentes de PDFsharp): compilar no prueba que el font resolver funcione. Esto es lo
/// más cercano a probarlo igual que correría en el contenedor Linux del portal.
/// </summary>
public sealed class CommissionInvoicePdfRendererTests
{
    private readonly CommissionInvoicePdfRenderer _renderer = new();

    [Fact]
    public void Render_produces_a_non_empty_pdf()
    {
        var school = new School { Name = "Escuela Demo", LegalName = "Escuela Demo SA de CV", Rfc = "XAXX010101000", TaxRegime = "601", CfdiUse = "G03" };
        var invoice = new CommissionInvoice
        {
            SchoolId = school.Id,
            PeriodFromUtc = new DateTime(2026, 8, 1),
            PeriodToUtc = new DateTime(2026, 8, 31),
            CommissionAmount = 123.45m,
            Currency = "MXN",
            Status = CfdiStatus.Stamped,
            Uuid = Guid.NewGuid().ToString(),
            StampedAtUtc = DateTime.UtcNow,
        };

        var bytes = _renderer.Render(invoice, school);

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(500, "un PDF de una página con texto no debería quedar casi vacío");
        // Firma de archivo PDF ("%PDF-"): confirma que es un PDF real, no basura.
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Render_tolerates_missing_optional_fiscal_fields()
    {
        // Una escuela recién dada de alta puede no tener razón social/RFC todavía; el renderer no
        // debe reventar solo porque falten (CommissionInvoiceService ya bloquea el timbrado sin
        // ellos, pero el PDF en sí no debería depender de esa validación para no explotar).
        var school = new School { Name = "Escuela Sin Datos" };
        var invoice = new CommissionInvoice
        {
            SchoolId = school.Id,
            PeriodFromUtc = new DateTime(2026, 8, 1),
            PeriodToUtc = new DateTime(2026, 8, 31),
            CommissionAmount = 0m,
            Status = CfdiStatus.Stamped,
        };

        var act = () => _renderer.Render(invoice, school);

        act.Should().NotThrow();
    }
}
