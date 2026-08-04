using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Application.Commands.RegistrarCotizacionEdi;

/// <summary>
/// Respuesta Application-level del comando <see cref="RegistrarCotizacionEdiCommand"/>.
/// PR D mapea esto a <c>SubmitCotizacionResponse</c> (shape HTTP del
/// contrato §4.1) cuando el endpoint exista — esta capa NO conoce
/// la representación HTTP.
/// </summary>
public sealed record RegistrarCotizacionEdiResponse(
    Guid Id,
    string QuoteReference,
    string Sucursal,
    EstadoEntidad Estado,
    DateTimeOffset SubmittedAt);
