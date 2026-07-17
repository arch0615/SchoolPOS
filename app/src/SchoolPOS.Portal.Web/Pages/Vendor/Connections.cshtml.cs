using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;

namespace SchoolPOS.Portal.Web.Pages.Vendor;

[Authorize(Policy = "Vendor")]
public class ConnectionsModel : PageModel
{
    private readonly SchoolDbContext _db;

    public ConnectionsModel(SchoolDbContext db) => _db = db;

    public IReadOnlyList<Row> Schools { get; private set; } = Array.Empty<Row>();

    [BindProperty(SupportsGet = true)] public int? Connected { get; set; }
    [BindProperty(SupportsGet = true)] public int? Error { get; set; }

    public async Task OnGetAsync()
    {
        Schools = await (
            from s in _db.Schools.AsNoTracking()
            join a in _db.SchoolPaymentAccounts.AsNoTracking()
                on new { SchoolId = s.Id, Provider = "MercadoPago" } equals new { a.SchoolId, a.Provider } into acc
            from a in acc.DefaultIfEmpty()
            orderby s.Name
            select new Row(s.Id, s.Name, a != null, a != null ? a.ProviderUserId : null, a != null ? a.ConnectedAtUtc : null))
            .ToListAsync();
    }

    public sealed record Row(Guid SchoolId, string Name, bool Connected, string? ProviderUserId, DateTime? ConnectedAtUtc);
}
