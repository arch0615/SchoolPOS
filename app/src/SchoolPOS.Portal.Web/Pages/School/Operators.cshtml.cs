using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.School;

/// <summary>
/// Alta y baja de operadores de la tienda (cajero/almacén/administrador) desde el portal —
/// hasta ahora solo era posible crear el administrador inicial vía SchoolPOS.Provision, o
/// cualquier rol desde el propio POS de escritorio. Reutiliza IAuthService.CreateOperatorAsync
/// (mismo hash de contraseña, mismas reglas) para que un administrador dé de alta cajeros y
/// personal de almacén sin depender de Windows.
/// </summary>
[Authorize(Policy = "SchoolAdmin")]
public class OperatorsModel : PageModel
{
    private readonly SchoolDbContext _db;
    private readonly IAuthService _auth;

    public OperatorsModel(SchoolDbContext db, IAuthService auth)
    {
        _db = db;
        _auth = auth;
    }

    [BindProperty] public InputModel Input { get; set; } = new();

    public IReadOnlyList<User> Operators { get; private set; } = Array.Empty<User>();

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "El usuario es obligatorio.")]
        [EmailAddress(ErrorMessage = "El usuario debe ser un correo electrónico.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
        public string Password { get; set; } = string.Empty;

        [Required]
        public UserRole Role { get; set; } = UserRole.Cashier;
    }

    public async Task OnGetAsync() => await LoadOperatorsAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var schoolId = User.GetSchoolId();

        if (!ModelState.IsValid)
        {
            Error = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            await LoadOperatorsAsync();
            return Page();
        }

        try
        {
            await _auth.CreateOperatorAsync(schoolId, Input.Username.Trim(), Input.Password, Input.Role);
            Message = $"Operador '{Input.Username}' creado como {RoleLabel(Input.Role)}.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(Guid id)
    {
        try
        {
            var schoolId = User.GetSchoolId();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.SchoolId == schoolId);
            if (user is not null)
            {
                user.IsActive = !user.IsActive;
                await _db.SaveChangesAsync();
                Message = user.IsActive ? $"'{user.Username}' reactivado." : $"'{user.Username}' desactivado.";
            }
        }
        catch (Exception ex)
        {
            Error = $"No se pudo cambiar el estado del operador: {ex.Message}";
        }

        return RedirectToPage();
    }

    private async Task LoadOperatorsAsync()
    {
        var schoolId = User.GetSchoolId();
        Operators = await _db.Users.AsNoTracking()
            .Where(u => u.SchoolId == schoolId)
            .OrderBy(u => u.Username)
            .ToListAsync();
    }

    public static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Admin => "Administrador",
        UserRole.Warehouse => "Almacén",
        _ => "Cajero",
    };
}
