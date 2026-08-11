using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SchoolPOS.Portal.Web.Pages;

/// <summary>
/// Página de error de marca. La sirve UseExceptionHandler("/Error") ante excepciones y
/// UseStatusCodePagesWithReExecute ante códigos como 404, pasando el código en ?code=.
/// </summary>
[AllowAnonymous]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ErrorModel : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Code { get; set; }

    public int HttpStatus { get; private set; } = 500;
    public string Title { get; private set; } = "Algo salió mal";
    public string Message { get; private set; } = "Ocurrió un error inesperado. Intenta de nuevo en un momento.";
    public string Emoji { get; private set; } = "⚠️";

    public void OnGet()
    {
        HttpStatus = Code ?? 500;
        (Title, Message, Emoji) = HttpStatus switch
        {
            404 => ("Página no encontrada", "La página que buscas no existe o cambió de dirección.", "🔎"),
            403 => ("Acceso denegado", "No tienes permiso para ver esta página.", "🔒"),
            401 => ("Inicia sesión", "Necesitas ingresar para ver esta página.", "🔑"),
            >= 500 => ("Algo salió mal", "Ocurrió un error inesperado. Intenta de nuevo en un momento.", "⚠️"),
            _ => ("Ocurrió un problema", "No se pudo completar la solicitud. Intenta de nuevo.", "⚠️"),
        };
        Response.StatusCode = HttpStatus;
    }
}
