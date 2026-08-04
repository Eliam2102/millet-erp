using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Application.Commands.ReintentarCotizacion;

/// <summary>
/// Respuesta del comando <see cref="ReintentarCotizacionCommand"/>.
/// El endpoint la serializa directo a JSON (camelCase, patrón Compras).
/// </summary>
public sealed record ReintentarCotizacionResponse(
    Guid Id,
    string QuoteReference,
    EstadoEntidad Estado,
    DateTimeOffset ReintentadoEn);
