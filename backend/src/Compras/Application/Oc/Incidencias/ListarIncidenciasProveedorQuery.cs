using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Identidad;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Oc.Incidencias;

/// <summary>
/// Historial de incidencias del proveedor (F1-COM-07, base de COM-13): abiertas y cerradas, con su
/// compromiso inicial y vigente, reprogramaciones y si se cumplió a tiempo. Alcance por la sucursal
/// destino de la OC (ADR-0051).
/// </summary>
public sealed record ListarIncidenciasProveedorQuery(Guid? ProveedorId = null, bool? SoloAbiertas = null,
    int Page = 1, int PageSize = 50) : IRequest<ListarIncidenciasProveedorResponse>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "compras.ordenes.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed record IncidenciaHistorialItem(Guid Id, Guid OrdenCompraId, string Folio, Guid ProveedorId,
    int Partida, TipoIncidenciaProveedorOc Tipo, decimal Cantidad, DateOnly FechaCompromisoInicial,
    DateOnly FechaCompromisoVigente, int Reprogramaciones, bool Abierta, bool Vencida, bool? CumplidaATiempo,
    string? ResponsableNombre, DateTimeOffset RegistradaEn, DateTimeOffset? CerradaEn, string? MotivoCierre);

/// <summary>Conteos sobre todo el filtro (no solo la página).</summary>
public sealed record IncidenciasProveedorResumen(int Total, int Abiertas, int Vencidas, int CumplidasATiempo,
    int CumplidasTarde);

public sealed record ListarIncidenciasProveedorResponse(IReadOnlyList<IncidenciaHistorialItem> Items,
    IncidenciasProveedorResumen Resumen, int Page, int PageSize, int TotalCount);

public sealed class ListarIncidenciasProveedorHandler(ComprasDbContext db, IClock clock, IUsuarioReadPort usuarios)
    : IRequestHandler<ListarIncidenciasProveedorQuery, ListarIncidenciasProveedorResponse>
{
    public async Task<ListarIncidenciasProveedorResponse> Handle(ListarIncidenciasProveedorQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var hoy = clock.HoyLocal();
        var sucursales = request.SucursalesPermitidas;

        var query =
            from i in db.IncidenciasProveedorOc.AsNoTracking()
            join oc in db.OrdenesCompra.AsNoTracking() on i.OrdenCompraId equals oc.Id
            where sucursales == null || sucursales.Contains(oc.SucursalDestinoId)
            where request.ProveedorId == null || oc.ProveedorId == request.ProveedorId
            where request.SoloAbiertas != true || i.CerradaEn == null
            select new
            {
                i.Id,
                i.OrdenCompraId,
                Folio = oc.Folio.Valor,
                oc.ProveedorId,
                Partida = oc.Lineas.Where(l => l.Id == i.LineaOrdenCompraId).Select(l => l.Posicion).First(),
                i.Tipo,
                i.Cantidad,
                Inicial = i.Compromisos.OrderBy(c => c.Numero).Select(c => c.FechaCompromiso).First(),
                Vigente = i.Compromisos.OrderByDescending(c => c.Numero).Select(c => c.FechaCompromiso).First(),
                Compromisos = i.Compromisos.Count,
                i.ResponsableId,
                i.RegistradaEn,
                i.CerradaEn,
                i.CerradaPorId,
                i.MotivoCierre,
            };

        // ponytail: el resumen se calcula en memoria sobre el filtro completo; suficiente para el volumen
        // de incidencias por proveedor. Pasar a agregados en SQL si COM-13 lo usa sobre todo el histórico.
        var todas = await query.OrderByDescending(x => x.RegistradaEn).ToListAsync(cancellationToken);
        bool? ATiempo(DateTimeOffset? cerradaEn, Guid? cerradaPor, DateOnly vigente) =>
            cerradaEn is DateTimeOffset c && cerradaPor is null ? FechaContable.HoyLocal(c) <= vigente : null;

        var resumen = new IncidenciasProveedorResumen(todas.Count,
            todas.Count(x => x.CerradaEn == null),
            todas.Count(x => x.CerradaEn == null && x.Vigente < hoy),
            todas.Count(x => ATiempo(x.CerradaEn, x.CerradaPorId, x.Vigente) == true),
            todas.Count(x => ATiempo(x.CerradaEn, x.CerradaPorId, x.Vigente) == false));

        var pagina = todas.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var nombres = await usuarios.ObtenerNombresAsync(
            pagina.Select(x => x.ResponsableId).Distinct().ToList(), cancellationToken);
        var items = pagina.Select(x => new IncidenciaHistorialItem(x.Id, x.OrdenCompraId, x.Folio, x.ProveedorId,
            x.Partida, x.Tipo, x.Cantidad, x.Inicial, x.Vigente, x.Compromisos - 1, x.CerradaEn == null,
            x.CerradaEn == null && x.Vigente < hoy, ATiempo(x.CerradaEn, x.CerradaPorId, x.Vigente),
            nombres.GetValueOrDefault(x.ResponsableId), x.RegistradaEn, x.CerradaEn, x.MotivoCierre)).ToList();

        return new ListarIncidenciasProveedorResponse(items, resumen, page, pageSize, todas.Count);
    }
}
