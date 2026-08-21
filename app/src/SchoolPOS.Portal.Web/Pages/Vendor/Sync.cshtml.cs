using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Portal.Web.Pages.Vendor;

/// <summary>
/// Llaves de la API de sincronización de una escuela: el Sync Agent del POS las usa para hablar
/// con <c>/api/sync/*</c> en vez de tener una cadena de conexión directa a la base de datos
/// (FR-SYNC-API). La llave en claro solo existe en la respuesta de <see cref="OnPostIssueAsync"/>;
/// de ahí en adelante solo su hash vive en la base.
/// </summary>
[Authorize(Policy = "Vendor")]
public class SyncModel : PageModel
{
    private readonly ISyncApiKeyService _keys;
    private readonly SchoolDbContext _db;

    public SyncModel(ISyncApiKeyService keys, SchoolDbContext db)
    {
        _keys = keys;
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public Guid SchoolId { get; set; }

    public string SchoolName { get; private set; } = string.Empty;
    public IReadOnlyList<SyncApiKey> Keys { get; private set; } = Array.Empty<SyncApiKey>();

    /// <summary>Solo se llena justo después de generarla — nunca se vuelve a poder leer.</summary>
    [TempData] public string? IssuedKey { get; set; }
    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var school = await _db.Schools.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SchoolId);
        if (school is null)
            return NotFound();
        SchoolName = school.Name;

        try
        {
            Keys = await _keys.ListAsync(SchoolId);
        }
        catch (Exception ex)
        {
            Error = $"No se pudieron cargar las llaves: {ex.Message}";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostIssueAsync(string? label)
    {
        try
        {
            IssuedKey = await _keys.IssueAsync(SchoolId, label ?? string.Empty);
        }
        catch (Exception ex)
        {
            Error = $"No se pudo generar la llave: {ex.Message}";
        }
        return RedirectToPage(new { SchoolId });
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid keyId)
    {
        try
        {
            await _keys.RevokeAsync(keyId);
            Message = "Llave revocada. El Sync Agent que la use dejará de poder sincronizar en su próxima corrida.";
        }
        catch (Exception ex)
        {
            Error = $"No se pudo revocar la llave: {ex.Message}";
        }
        return RedirectToPage(new { SchoolId });
    }
}
