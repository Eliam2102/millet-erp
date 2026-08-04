namespace Millet.Integraciones.Aw.Domain;

/// <summary>
/// Estado de un <see cref="Envio"/> (intento individual de drop al
/// servicio on-prem). Cada drop empieza en <see cref="Started"/> y
/// transiciona a terminal (Success/Failed/Timeout) una sola vez.
/// </summary>
public enum EstadoEnvio : short
{
    Started = 0,
    Success = 1,
    Failed = 2,
    Timeout = 3,
}
