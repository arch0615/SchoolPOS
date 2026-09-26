using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.School;

/// <summary>
/// Pedidos anticipados pendientes de entregar (FR-WP): el tutor ya pagó desde el portal, la caja
/// ya lo bajó a su libro mayor local — esta pantalla solo marca la entrega física, del lado de la
/// nube (no hay sincronización de vuelta a la caja para el estado de entrega). Limitado por el
/// claim school_id.
/// </summary>
[Authorize(Policy = "School")]
public class OrdersModel : PageModel
{
    private readonly IPortalOrderService _orders;
    public OrdersModel(IPortalOrderService orders) => _orders = orders;

    public IReadOnlyList<PortalOrderRow> Pending { get; private set; } = Array.Empty<PortalOrderRow>();

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task OnGetAsync()
    {
        Pending = await _orders.GetPendingOrdersForSchoolAsync(User.GetSchoolId());
    }

    public async Task<IActionResult> OnPostMarkFulfilledAsync(Guid orderId)
    {
        try
        {
            await _orders.MarkFulfilledAsync(User.GetSchoolId(), orderId);
            Message = "Pedido marcado como entregado.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        return RedirectToPage();
    }
}
