using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SchoolPOS.Portal.Web.Pages.Payments;

public class ResultModel : PageModel
{
    public bool Approved { get; private set; }

    public void OnGet(string? status) => Approved = status == "approved";
}
