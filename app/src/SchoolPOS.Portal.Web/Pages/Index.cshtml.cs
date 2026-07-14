using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SchoolPOS.Portal.Web.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated ?? false)
            return RedirectToPage("/Dashboard");
        return Page();
    }
}
