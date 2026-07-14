using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.Vendor;

public class LoginModel : PageModel
{
    private readonly PortalOptions _options;

    public LoginModel(PortalOptions options) => _options = options;

    [BindProperty]
    public string AccessCode { get; set; } = string.Empty;

    public string? Error { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!string.Equals(AccessCode, _options.VendorAccessCode, StringComparison.Ordinal))
        {
            Error = "Código de acceso incorrecto.";
            return Page();
        }

        await PortalSignIn.SignInVendorAsync(HttpContext);
        return RedirectToPage("/Vendor/Index");
    }
}
