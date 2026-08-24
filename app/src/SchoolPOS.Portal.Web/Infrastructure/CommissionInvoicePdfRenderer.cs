using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>
/// Genera una "representación impresa" simple del CFDI de comisión, en PDF. Es un resumen
/// informativo (emisor/receptor, concepto, importes, folio fiscal) — <b>no</b> es todavía la
/// representación impresa oficialmente compatible con el estándar del SAT (código QR contra el
/// verificador del SAT, cadena original, sellos digitales renderizados): eso requiere conocer la
/// forma exacta en que el PAC (SW Sapien) devuelve esos datos, lo cual no se ha podido validar
/// contra una cuenta real todavía. Cuando haya credenciales de SW, ajustar este renderer con el
/// QR/cadena original reales antes de repartir el PDF como comprobante fiscal formal.
/// </summary>
public sealed class CommissionInvoicePdfRenderer
{
    private const string FontFamily = "Roboto";

    static CommissionInvoicePdfRenderer()
    {
        // PDFsharp 6.x ya no soporta las 14 fuentes base de PDF sin incrustar (Helvetica, etc.):
        // sin un FontResolver que devuelva bytes reales de un .ttf, dibujar texto revienta con un
        // NullReferenceException — no solo en Linux, en cualquier entorno. Roboto va incrustada
        // como recurso (ver csproj) precisamente para no depender de qué fuentes tenga instaladas
        // el sistema operativo donde corra el portal (Windows en desarrollo, Linux en producción).
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
    }

    public byte[] Render(CommissionInvoice invoice, School school)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.Letter;
        using var gfx = XGraphics.FromPdfPage(page);

        var title = new XFont(FontFamily, 18, XFontStyleEx.Bold);
        var heading = new XFont(FontFamily, 11, XFontStyleEx.Bold);
        var body = new XFont(FontFamily, 10, XFontStyleEx.Regular);
        var small = new XFont(FontFamily, 8, XFontStyleEx.Regular);

        double margin = 40;
        double y = margin;
        double pageWidth = page.Width.Point;

        gfx.DrawString("Comprobante de comisión (CFDI)", title, XBrushes.Black, new XPoint(margin, y));
        y += 26;
        gfx.DrawString(
            "Resumen informativo — no sustituye el XML timbrado, que es el comprobante fiscal válido.",
            small, XBrushes.Gray, new XPoint(margin, y));
        y += 24;

        void Line(string label, string value, XFont? font = null)
        {
            gfx.DrawString(label, heading, XBrushes.Black, new XPoint(margin, y));
            gfx.DrawString(value, font ?? body, XBrushes.Black, new XPoint(margin + 160, y));
            y += 18;
        }

        Line("Folio fiscal (UUID):", invoice.Uuid ?? "—");
        Line("Fecha de timbrado:", invoice.StampedAtUtc?.ToString("dd/MM/yyyy HH:mm 'UTC'") ?? "—");
        y += 8;

        Line("Receptor:", school.LegalName ?? school.Name);
        Line("RFC receptor:", school.Rfc ?? "—");
        Line("Régimen fiscal:", school.TaxRegime ?? "—");
        Line("Uso de CFDI:", school.CfdiUse ?? "—");
        y += 8;

        Line("Periodo facturado:",
            $"{invoice.PeriodFromUtc:dd/MM/yyyy} – {invoice.PeriodToUtc:dd/MM/yyyy}");
        Line("Importe:", invoice.CommissionAmount.ToString("C2") + " " + invoice.Currency);
        y += 16;

        gfx.DrawLine(XPens.LightGray, margin, y, pageWidth - margin, y);
        y += 16;
        gfx.DrawString(
            "El XML timbrado adjunto (o descargable desde el panel del proveedor) es el " +
            "comprobante fiscal digital válido ante el SAT.",
            small, XBrushes.Gray, new XRect(margin, y, pageWidth - 2 * margin, 40), XStringFormats.TopLeft);

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Sirve Roboto Regular/Bold desde los recursos incrustados del ensamblado — nunca toca el
    /// sistema de archivos ni depende de fuentes instaladas en el SO.
    /// </summary>
    private sealed class EmbeddedFontResolver : IFontResolver
    {
        private const string RegularFace = "Roboto#Regular";
        private const string BoldFace = "Roboto#Bold";

        public string DefaultFontName => RegularFace;

        public byte[] GetFont(string faceName)
        {
            var resourceName = faceName == BoldFace
                ? "SchoolPOS.Portal.Web.Assets.Fonts.Roboto-Bold.ttf"
                : "SchoolPOS.Portal.Web.Assets.Fonts.Roboto-Regular.ttf";

            using var stream = typeof(EmbeddedFontResolver).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"No se encontró el recurso incrustado '{resourceName}'.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(isBold ? BoldFace : RegularFace);
    }
}
