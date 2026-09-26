using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Exceptions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages;

/// <summary>
/// Pedidos anticipados desde el portal (FR-WP): catálogo de la escuela (menú del día y artículos
/// visibles) y el historial de pedidos del tutor, con opción de cancelar mientras la caja no los
/// haya recibido todavía.
/// </summary>
[Authorize(Policy = "Guardian")]
public class OrdersModel : PageModel
{
    private readonly IGuardianService _guardians;
    private readonly IPortalOrderService _orders;

    public OrdersModel(IGuardianService guardians, IPortalOrderService orders)
    {
        _guardians = guardians;
        _orders = orders;
    }

    public IReadOnlyList<LinkedStudent> Students { get; private set; } = Array.Empty<LinkedStudent>();
    public LinkedStudent? Selected { get; private set; }
    public IReadOnlyList<CatalogItem> Catalog { get; private set; } = Array.Empty<CatalogItem>();
    public IReadOnlyList<PortalOrderRow> MyOrders { get; private set; } = Array.Empty<PortalOrderRow>();

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? studentId)
    {
        try
        {
            await LoadAsync(studentId);
        }
        catch (Exception ex)
        {
            Error = $"No se pudo cargar el catálogo: {ex.Message}";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostPlaceOrderAsync(Guid studentId, Guid accountId, DateTime? requestedForDate)
    {
        var guardianId = User.GetGuardianId();
        if (!await _guardians.OwnsStudentAsync(guardianId, studentId))
        {
            Error = "No tiene acceso a esa cuenta.";
            return RedirectToPage();
        }

        // Sin binding de modelo para el carrito: un input por artículo del catálogo (qty_<ProductId>)
        // es más simple que una lista indexada para un formulario que cambia con el catálogo de cada escuela.
        var lines = new List<PortalOrderLineRequest>();
        foreach (var key in Request.Form.Keys)
        {
            if (!key.StartsWith("qty_", StringComparison.Ordinal))
                continue;
            if (Guid.TryParse(key["qty_".Length..], out var productId) &&
                decimal.TryParse(Request.Form[key], out var qty) && qty > 0)
                lines.Add(new PortalOrderLineRequest(productId, qty));
        }

        if (lines.Count == 0)
        {
            Error = "Selecciona al menos un artículo con cantidad mayor a cero.";
            return RedirectToPage(new { studentId });
        }

        try
        {
            var order = await _orders.PlaceOrderAsync(guardianId, accountId, lines, requestedForDate);
            Message = $"Pedido realizado por {order.Total:C2}.";
        }
        catch (InsufficientBalanceException)
        {
            Error = "Saldo insuficiente para este pedido.";
        }
        catch (DailyLimitExceededException ex)
        {
            Error = $"Este pedido excede el presupuesto diario del alumno (gastado hoy: {ex.SpentToday:C2} de {ex.DailyLimit:C2}).";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        return RedirectToPage(new { studentId });
    }

    public async Task<IActionResult> OnPostCancelOrderAsync(Guid orderId, Guid? studentId)
    {
        try
        {
            await _orders.CancelOrderAsync(User.GetGuardianId(), orderId);
            Message = "Pedido cancelado y reintegrado a tu saldo.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        return RedirectToPage(new { studentId });
    }

    private async Task LoadAsync(Guid? studentId)
    {
        var guardianId = User.GetGuardianId();
        Students = await _guardians.GetLinkedStudentsAsync(guardianId);

        Selected = studentId is { } id
            ? Students.FirstOrDefault(s => s.StudentId == id) ?? Students.FirstOrDefault()
            : Students.FirstOrDefault();

        MyOrders = await _orders.GetOrdersForGuardianAsync(guardianId);

        if (Selected is null)
            return;
        Catalog = await _orders.GetCatalogAsync(User.GetSchoolId());
    }
}
