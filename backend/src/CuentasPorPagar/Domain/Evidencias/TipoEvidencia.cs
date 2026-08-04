namespace Millet.CuentasPorPagar.Domain.Evidencias;

/// <summary>
/// Tipo de medio del soporte digital de autorización informal
/// (§4.10 del 00-levantamiento).
/// </summary>
public enum TipoEvidencia
{
    CapturaWhatsapp = 1,
    Audio           = 2,
    Email           = 3,
    FirmaEscaneada  = 4,
    Otro            = 99,
}

/// <summary>
/// Estado del flujo de firma física complementaria (§4.10). Aplica
/// cuando la autorización digital quedó documentada pero se espera la
/// firma física para cierre formal (ej. cheque a proveedor).
/// </summary>
public enum EstadoFirmaFisica
{
    NoAplica  = 1,
    Pendiente = 2,
    Recibida  = 3,
}
