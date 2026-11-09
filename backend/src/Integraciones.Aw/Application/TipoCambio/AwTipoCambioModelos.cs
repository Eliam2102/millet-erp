namespace Millet.Integraciones.Aw.Application.TipoCambio;

/// <summary>
/// Fila destinada a la inserción en la tabla puente dbo.erp_tipo_cambio (I13.2 / flujo 14).
/// Inmutable; cualquier corrección posterior se publica como versión N+1.
/// </summary>
public sealed record AwTipoCambioFila(
    string Moneda,
    DateOnly FechaAplicacion,
    int Version,
    decimal Valor,
    DateOnly? FechaFix,
    string Origen);

/// <summary>
/// Representa el acuse de aplicación y estado de una fila en dbo.erp_tipo_cambio (I13.2).
/// Actualizado exclusivamente por la tarea de TI de Millet al interactuar con A+W.
/// </summary>
public sealed record AwTipoCambioAcuse(
    string Moneda,
    DateOnly FechaAplicacion,
    int Version,
    decimal Valor,
    DateTimeOffset? AplicadaAt,
    byte? Resultado,
    string? Motivo,
    int Intentos);

/// <summary>
/// Resumen del ciclo de publicación hacia la tabla puente dbo.erp_tipo_cambio.
/// </summary>
public sealed record ResultadoPublicacionAw(
    int Insertadas,
    int Omitidas,
    bool Habilitado,
    string? Mensaje = null);
