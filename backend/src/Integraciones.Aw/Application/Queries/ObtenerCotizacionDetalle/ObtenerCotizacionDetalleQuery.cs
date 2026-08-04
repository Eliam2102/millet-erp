using MediatR;

namespace Millet.Integraciones.Aw.Application.Queries.ObtenerCotizacionDetalle;

/// <summary>
/// Query del endpoint <c>GET /cotizaciones/{id}</c>. Retorna el detalle
/// completo de una cotización: el aggregate raíz + correlación (si
/// existe) + envíos recientes.
///
/// <para>
/// Empresa se aplica automáticamente vía global query filter de EF
/// (<c>BaseDbContext</c> + <c>IPerteneceAEmpresa</c>) — el caller no
/// puede consultar cotizaciones de otra empresa.
/// </para>
/// </summary>
public sealed record ObtenerCotizacionDetalleQuery(
    Guid CotizacionId
) : IRequest<CotizacionDetalleResponse?>;
