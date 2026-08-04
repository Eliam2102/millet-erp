namespace Millet.Catalogos.Application.Monedas;

/// <summary>
/// DTO de respuesta para Moneda (F-Admin-PR5.1).
/// </summary>
public sealed record MonedaResponse(
    Guid Id,
    string Codigo,
    string Nombre,
    int Decimales,
    bool Activa,
    int Version);

/// <summary>
/// DTO de respuesta para TipoCambio (F-Admin-PR5.1).
/// </summary>
public sealed record TipoCambioResponse(
    Guid Id,
    Guid MonedaId,
    DateOnly Fecha,
    decimal ValorEnMxn,
    Millet.Catalogos.Domain.OrigenTipoCambio Origen,
    int Version);
