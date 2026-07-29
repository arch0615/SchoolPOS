using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SchoolPOS.Portal.Web.Pages.Vendor;

/// <summary>
/// Ruta anterior del panel del proveedor. Se conserva como redirección permanente a
/// /Vendor/Dashboard para no romper enlaces ni marcadores existentes.
/// </summary>
[Authorize(Policy = "Vendor")]
public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        var query = Request.QueryString.HasValue ? Request.QueryString.Value : string.Empty;
        return RedirectPermanent($"/Vendor/Dashboard{query}");
    }
}
