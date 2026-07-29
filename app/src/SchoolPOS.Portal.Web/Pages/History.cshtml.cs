using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SchoolPOS.Portal.Web.Pages;

/// <summary>
/// Ruta anterior del historial. Se conserva como redirección permanente a /Transactions
/// para no romper enlaces guardados por los tutores.
/// </summary>
public class HistoryModel : PageModel
{
    public IActionResult OnGet()
    {
        var query = Request.QueryString.HasValue ? Request.QueryString.Value : string.Empty;
        return RedirectPermanent($"/Transactions{query}");
    }
}
