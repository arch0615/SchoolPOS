using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Data;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.Vendor;

/// <summary>
/// Panel de la tienda escolar (vista del proveedor). Solo lectura sobre los datos del POS;
/// agrega todas las escuelas del proveedor (schoolId nulo).
/// </summary>
[Authorize(Policy = "Vendor")]
public class StoreModel : PageModel
{
    private readonly SchoolDbContext _db;
    public StoreModel(SchoolDbContext db) => _db = db;

    public StoreDashboardData Data { get; private set; } = null!;

    public async Task OnGetAsync() => Data = await StoreDashboard.BuildAsync(_db, schoolId: null);
}
