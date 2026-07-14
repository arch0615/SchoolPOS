using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Portal.Web.Pages.Payments;

/// <summary>
/// Checkout de simulación (solo desarrollo). Al aprobar, reproduce lo que haría el webhook de la
/// pasarela: verifica la notificación y aplica la recarga server-side (NFR-3).
/// </summary>
public class SandboxModel : PageModel
{
    private readonly IPaymentGateway _gateway;
    private readonly ITopUpService _topUps;

    public SandboxModel(IPaymentGateway gateway, ITopUpService topUps)
    {
        _gateway = gateway;
        _topUps = topUps;
    }

    public string Reference { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }

    public void OnGet(string @ref, decimal amount)
    {
        Reference = @ref;
        Amount = amount;
    }

    public async Task<IActionResult> OnPostApproveAsync(string reference)
    {
        // Simula la notificación server-side de la pasarela.
        var notification = await _gateway.VerifyWebhookAsync(signature: "sandbox", rawPayload: $"{reference}|approved");
        if (notification is { Status: PaymentStatus.Approved })
        {
            var topUp = await _topUps.ConfirmAsync(notification.GatewayRef);
            await _topUps.ApplyConfirmedAsync(topUp.Id);
        }
        return RedirectToPage("/Payments/Result", new { status = "approved" });
    }

    public IActionResult OnPostReject(string reference) =>
        RedirectToPage("/Payments/Result", new { status = "rejected" });
}
