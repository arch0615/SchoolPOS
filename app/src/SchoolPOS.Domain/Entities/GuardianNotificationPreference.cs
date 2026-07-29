namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Preferencias de aviso del tutor (FR-WP-10): qué eventos quiere que se le notifiquen.
/// Uno a uno con <see cref="Guardian"/>. Guardar aquí la elección es independiente del envío:
/// el reparto (correo, push) se implementa aparte y lee estas banderas.
/// </summary>
public class GuardianNotificationPreference
{
    /// <summary>Clave primaria y foránea: un registro por tutor.</summary>
    public Guid GuardianId { get; set; }
    public Guardian Guardian { get; set; } = null!;

    /// <summary>Avisar cuando el saldo de un hijo baja del umbral de la escuela.</summary>
    public bool LowBalance { get; set; } = true;

    /// <summary>Avisar cuando una recarga queda confirmada por la pasarela.</summary>
    public bool TopUpConfirmed { get; set; } = true;

    /// <summary>Avisar en cada compra hecha en la tienda escolar.</summary>
    public bool PurchaseMade { get; set; }

    /// <summary>Enviar un resumen diario de consumo en lugar de avisos sueltos.</summary>
    public bool DailySummary { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
