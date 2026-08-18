namespace SchoolPOS.Pos.Desktop.Infrastructure;

/// <summary>
/// Texto de error legible para el operador. EF envuelve las fallas de base de datos en un
/// "An error occurred while saving the entity changes. See the inner exception for details.",
/// que no dice absolutamente nada de lo que pasó; lo útil está en la excepción interna.
/// </summary>
public static class ExceptionText
{
    public static string Describe(this Exception ex)
    {
        // La causa raíz suele ser la más informativa (violación de índice, columna faltante…).
        var root = ex;
        while (root.InnerException is not null)
            root = root.InnerException;

        return ReferenceEquals(root, ex)
            ? ex.Message
            : $"{ex.Message} — {root.Message}";
    }
}
