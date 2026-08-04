using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Data;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.School;

/// <summary>
/// Panel de la propia tienda escolar. Limitado a la escuela del operador (claim school_id): una
/// escuela nunca ve datos de otra.
/// </summary>
[Authorize(Policy = "School")]
public class StoreModel : PageModel
{
    private readonly SchoolDbContext _db;
    public StoreModel(SchoolDbContext db) => _db = db;

    public StoreDashboardData Data { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        var schoolId = User.GetSchoolId();
        Data = await StoreDashboard.BuildAsync(_db, schoolId);
    }
}
