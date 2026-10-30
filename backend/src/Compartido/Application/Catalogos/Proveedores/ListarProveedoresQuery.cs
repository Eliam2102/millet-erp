using MediatR;
using Millet.Compartido.Application.Adjuntos;
using Millet.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Proveedores;

/// <summary>
/// Lista paginada enriquecida de proveedores (F-Admin-PR4.5). Filtros
/// avanzados respecto al endpoint legacy de F7-PR1: rfc (substring),
/// razonSocial (substring sobre razón social o nombre comercial), q (caja única:
/// clave, RFC, razón social o nombre comercial), tipoPersona, estatus. La query original en
/// CatalogosEndpoints solo soporta <c>clave</c>+<c>estatus</c>.
/// </summary>
public sealed record ListarProveedoresQuery(
    string? Rfc = null,
    string? RazonSocial = null,
    TipoPersonaProveedor? TipoPersona = null,
    EstatusCatalogo? Estatus = null,
    int Offset = 0,
    int Limit = 50,
    string? Q = null,
    bool SoloExpedienteIncompleto = false,
    bool? Bloqueado = null) : IRequest<ListarProveedoresResponse>;

public sealed record ProveedorItem(
    Guid Id,
    string Clave,
    string RazonSocial,
    string? NombreComercial,
    string Rfc,
    TipoPersonaProveedor TipoPersona,
    short? CondicionesPagoDias,
    Guid? MonedaPreferidaId,
    EstatusCatalogo Estatus,
    DateTimeOffset? ValidadoEn = null,
    string? MotivoRechazo = null,
    int? ClaveAw = null,
    bool ExpedienteCompleto = true,
    int DocumentosFaltantes = 0,
    bool Bloqueado = false,
    string? MotivoBloqueo = null);

public sealed record ListarProveedoresResponse(
    IReadOnlyList<ProveedorItem> Items,
    int Offset,
    int Limit,
    int Total);

public sealed class ListarProveedoresHandler
    : IRequestHandler<ListarProveedoresQuery, ListarProveedoresResponse>
{
    private const int LimitMax = 200;
    private readonly CompartidoDbContext _db;

    private readonly IClock _clock;

    public ListarProveedoresHandler(CompartidoDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ListarProveedoresResponse> Handle(
        ListarProveedoresQuery query, CancellationToken cancellationToken)
    {
        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit is <= 0 or > LimitMax
            ? Math.Min(50, LimitMax)
            : query.Limit;

        IQueryable<Proveedor> q = _db.Proveedores.AsNoTracking();
        // Rfc = código → case-insensitive con lower() en ambos lados. RazonSocial
        // = texto libre → folding case + acentos (ADR-0045), igual que el nombre
        // de artículo, reusando el mapa centralizado en PostgresFunctions.
#pragma warning disable CA1304, CA1311, CA1862
        if (!string.IsNullOrWhiteSpace(query.Rfc))
            q = q.Where(p => p.Rfc.ToLower().Contains(query.Rfc.ToLower()));
        if (!string.IsNullOrWhiteSpace(query.RazonSocial))
        {
            var aguja = query.RazonSocial;
            q = q.Where(p =>
                PostgresFunctions.Translate(p.RazonSocial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                    .Contains(PostgresFunctions.Translate(aguja.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino))
                || (p.NombreComercial != null &&
                    PostgresFunctions.Translate(p.NombreComercial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                        .Contains(PostgresFunctions.Translate(aguja.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino))));
        }
        // Q = caja única: códigos (clave, RFC) con lower(); textos libres con
        // folding (ADR-0045). AND con los demás filtros.
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var busqueda = query.Q.Trim();
            q = q.Where(p =>
                p.Clave.ToLower().Contains(busqueda.ToLower())
                || p.Rfc.ToLower().Contains(busqueda.ToLower())
                || PostgresFunctions.Translate(p.RazonSocial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                    .Contains(PostgresFunctions.Translate(busqueda.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino))
                || (p.NombreComercial != null &&
                    PostgresFunctions.Translate(p.NombreComercial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                        .Contains(PostgresFunctions.Translate(busqueda.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino))));
        }
#pragma warning restore CA1304, CA1311, CA1862
        if (query.TipoPersona is TipoPersonaProveedor tp)
            q = q.Where(p => p.TipoPersona == tp);
        if (query.Estatus is EstatusCatalogo e)
            q = q.Where(p => p.Estatus == e);
        if (query.Bloqueado is bool b)
            q = q.Where(p => p.Bloqueado == b);

        var pendientes = ExpedienteAdjuntos.PendientesProveedores(_db, _clock);
        var resumen = q.Select(p => new { Proveedor = p, Pendientes = pendientes.Count(x => x.ProveedorId == p.Id) });
        if (query.SoloExpedienteIncompleto) resumen = resumen.Where(x => x.Pendientes > 0);
        var total = await resumen.CountAsync(cancellationToken);
        var items = await resumen
            .OrderBy(x => x.Proveedor.Clave)
            .Skip(offset).Take(limit)
            .Select(x => new ProveedorItem(
                x.Proveedor.Id, x.Proveedor.Clave, x.Proveedor.RazonSocial, x.Proveedor.NombreComercial, x.Proveedor.Rfc,
                x.Proveedor.TipoPersona, x.Proveedor.CondicionesPagoDias, x.Proveedor.MonedaPreferidaId, x.Proveedor.Estatus,
                x.Proveedor.ValidadoEn, x.Proveedor.MotivoRechazo, x.Proveedor.ClaveAw, x.Pendientes == 0, x.Pendientes,
                x.Proveedor.Bloqueado, x.Proveedor.MotivoBloqueo))
            .ToListAsync(cancellationToken);

        return new ListarProveedoresResponse(items, offset, limit, total);
    }
}
