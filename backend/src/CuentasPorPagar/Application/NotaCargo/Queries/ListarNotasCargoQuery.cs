using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.NotaCargo.Queries;

public sealed record ListarNotasCargoQuery(
    EstadoNotaCargo? Estado = null,
    Guid? ProveedorId = null,
    Guid? FacturaOrigenId = null,
    int Offset = 0,
    int Limit = 50,
    string? Q = null) : IRequest<PagedResponse<NotaCargoListItemResponse>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.documentos.leer-todas-sucursales";
    public string TipoDocumento => "nota_cargo";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed record NotaCargoListItemResponse(
    Guid Id,
    string Folio,
    short FolioAnio,
    Guid ProveedorId,
    Guid? SucursalId,
    string Concepto,
    decimal Monto,
    string Moneda,
    Guid? FacturaOrigenId,
    Guid? DevolucionAProveedorId,
    EstadoNotaCargo Estado,
    DateTimeOffset FechaCreacion,
    int Version,
    // Etiqueta resuelta server-side vía read port (ADR-0042).
    string? ProveedorNombre = null, string? ProveedorRfc = null);

/// <summary>
/// Filtros de la bandeja salvo el estado, compartidos por el listado y por los conteos
/// por estado (UX-F3-05) para que ambos coincidan siempre.
/// </summary>
internal static class NotasCargoFiltro
{
    public static IQueryable<Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo> Aplicar(CuentasPorPagarDbContext db, ListarNotasCargoQuery request)
    {
        var q = db.NotasCargo
            .Where(x => request.DocumentosPermitidos == null || (request.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id)).AsNoTracking();
        if (request.ProveedorId is Guid p) q = q.Where(n => n.ProveedorId == p);
        if (request.FacturaOrigenId is Guid f) q = q.Where(n => n.FacturaOrigenId == f);

        // q se aplica DESPUÉS del filtro de documentos permitidos (alcance por sucursal): solo acota más.
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var patron = LikePatron.Contiene(request.Q);
            // Folio es un VO con value converter: EF no traduce ILike sobre esa columna. Se resuelven los ids
            // por SQL parametrizado y se intersectan con `q`, que ya trae el alcance.
            var idsPorFolio = db.Database.SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM cuentas_por_pagar.notas_cargo WHERE folio ILIKE {patron}");
            q = q.Where(n => idsPorFolio.Contains(n.Id) || EF.Functions.ILike(n.Concepto, patron, LikePatron.Escape));
        }

        return q;
    }
}

public sealed record ConteosNotasCargoPorEstadoQuery(Guid? ProveedorId, Guid? FacturaOrigenId, string? Q = null)
    : IRequest<IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.documentos.leer-todas-sucursales";
    public string TipoDocumento => "nota_cargo";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class ConteosNotasCargoPorEstadoHandler(CuentasPorPagarDbContext db)
    : IRequestHandler<ConteosNotasCargoPorEstadoQuery, IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>>
{
    public async Task<IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>> Handle(
        ConteosNotasCargoPorEstadoQuery request, CancellationToken cancellationToken)
    {
        var lista = new ListarNotasCargoQuery(null, request.ProveedorId, request.FacturaOrigenId, 0, 1, request.Q) { DocumentosPermitidos = request.DocumentosPermitidos };
        return await NotasCargoFiltro.Aplicar(db, lista).GroupBy(x => x.Estado)
            .Select(g => new Millet.SharedKernel.Application.ConteoPorEstadoItem((int)g.Key, g.Count()))
            .ToListAsync(cancellationToken);
    }
}

public sealed class ListarNotasCargoHandler
    : IRequestHandler<ListarNotasCargoQuery, PagedResponse<NotaCargoListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IProveedorReadPort _proveedores;

    public ListarNotasCargoHandler(CuentasPorPagarDbContext db, IProveedorReadPort proveedores)
    {
        _db = db;
        _proveedores = proveedores;
    }

    public async Task<PagedResponse<NotaCargoListItemResponse>> Handle(
        ListarNotasCargoQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = NotasCargoFiltro.Aplicar(_db, query);
        if (query.Estado is EstadoNotaCargo e) q = q.Where(n => n.Estado == e);

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(n => n.FechaCreacion)
            .Skip(offset)
            .Take(limit)
            .Select(n => new NotaCargoListItemResponse(
                n.Id,
                n.Folio.Valor,
                n.FolioAnio,
                n.ProveedorId,
                n.SucursalId,
                n.Concepto,
                n.Monto,
                n.Moneda,
                n.FacturaOrigenId,
                n.DevolucionAProveedorId,
                n.Estado,
                n.FechaCreacion,
                n.Version,
                // Null explícito: expression trees no aceptan args opcionales
                // omitidos (CS0854). Se puebla abajo vía read port.
                null, null))
            .ToListAsync(cancellationToken);

        // Etiqueta del proveedor en batch sobre los ids distintos de la
        // página — mismo molde que Compras (ADR-0042).
        var proveedorIds = items.Select(i => i.ProveedorId).Distinct().ToArray();
        if (proveedorIds.Length > 0)
        {
            var nombres = await _proveedores.ObtenerNombresPorIdsAsync(proveedorIds, cancellationToken);
            var rfcs = await _proveedores.ObtenerRfcsPorIdsAsync(proveedorIds, cancellationToken);
            items = items
                .Select(i => i with
                {
                    ProveedorNombre = nombres.GetValueOrDefault(i.ProveedorId),
                    ProveedorRfc = rfcs.GetValueOrDefault(i.ProveedorId),
                })
                .ToList();
        }

        return new PagedResponse<NotaCargoListItemResponse>(items, offset, limit, total);
    }
}
