using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Portal.Web.Pages.Vendor;

/// <summary>
/// Alta de una nueva escuela desde el panel del proveedor — la parte de "onboarding" que hasta
/// ahora sólo era posible por línea de comandos (SchoolPOS.Provision). Crea la escuela y su
/// primer operador administrador (mismo IAuthService que usa Provision y el propio POS), para que
/// el proveedor no dependa de acceso SSH/CLI para dar de alta un cliente nuevo.
/// El resto de la instalación (SQL Server local, POS de escritorio, Sync Agent en la escuela)
/// sigue el runbook de siempre (app/deploy/INSTALL.md) usando el SchoolId que esta página genera.
/// </summary>
[Authorize(Policy = "Vendor")]
public class AddSchoolModel : PageModel
{
    private readonly SchoolDbContext _db;
    private readonly IAuthService _auth;

    public AddSchoolModel(SchoolDbContext db, IAuthService auth)
    {
        _db = db;
        _auth = auth;
    }

    [BindProperty] public InputModel Input { get; set; } = new();

    [TempData] public Guid? CreatedSchoolId { get; set; }
    [TempData] public string? CreatedSchoolName { get; set; }
    [TempData] public string? CreatedAdminUser { get; set; }
    public string? Error { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "El nombre de la escuela es obligatorio.")]
        public string SchoolName { get; set; } = string.Empty;

        [Required]
        public string Currency { get; set; } = "MXN";

        [Range(0, 1, ErrorMessage = "La comisión debe estar entre 0% y 100%.")]
        public decimal CommissionRate { get; set; } = 0.05m;

        [Range(0, 1, ErrorMessage = "El IVA debe estar entre 0% y 100%.")]
        public decimal TaxRate { get; set; }

        public bool TaxInclusive { get; set; } = true;

        // Datos fiscales (receptor del CFDI de comisión) — opcionales al alta, se pueden
        // completar después, pero sin ellos no se le puede facturar la comisión (FR-COM-5).
        public string? Rfc { get; set; }
        public string? LegalName { get; set; }
        public string? TaxRegime { get; set; }
        public string? PostalCode { get; set; }
        public string? CfdiUse { get; set; }

        [Required(ErrorMessage = "El usuario administrador es obligatorio.")]
        [EmailAddress(ErrorMessage = "El usuario administrador debe ser un correo electrónico.")]
        public string AdminUsername { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
        public string AdminPassword { get; set; } = string.Empty;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return Page();
        }

        if (await _db.Schools.AnyAsync(s => s.Name == Input.SchoolName.Trim()))
        {
            Error = $"Ya existe una escuela llamada '{Input.SchoolName}'.";
            return Page();
        }

        var school = new SchoolPOS.Domain.Entities.School
        {
            Name = Input.SchoolName.Trim(),
            Currency = Input.Currency.Trim().ToUpperInvariant(),
            CommissionRate = Input.CommissionRate,
            TaxRate = Input.TaxRate,
            TaxInclusive = Input.TaxInclusive,
            Rfc = string.IsNullOrWhiteSpace(Input.Rfc) ? null : Input.Rfc.Trim(),
            LegalName = string.IsNullOrWhiteSpace(Input.LegalName) ? null : Input.LegalName.Trim(),
            TaxRegime = string.IsNullOrWhiteSpace(Input.TaxRegime) ? null : Input.TaxRegime.Trim(),
            PostalCode = string.IsNullOrWhiteSpace(Input.PostalCode) ? null : Input.PostalCode.Trim(),
            CfdiUse = string.IsNullOrWhiteSpace(Input.CfdiUse) ? null : Input.CfdiUse.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };

        try
        {
            // Escuela + primer administrador en una sola transacción: si el alta del operador
            // falla (p. ej. usuario inválido), la escuela tampoco queda huérfana sin acceso.
            await using var tx = await _db.Database.BeginTransactionAsync();
            _db.Schools.Add(school);
            await _db.SaveChangesAsync();

            await _auth.CreateOperatorAsync(
                school.Id, Input.AdminUsername.Trim(), Input.AdminPassword, UserRole.Admin);

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }

        CreatedSchoolId = school.Id;
        CreatedSchoolName = school.Name;
        CreatedAdminUser = Input.AdminUsername.Trim();
        return RedirectToPage("/Vendor/Schools");
    }
}
