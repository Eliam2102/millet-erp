using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Revision;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.Queries;

/// <summary>
/// Bandeja paginada de facturas en revisión asignadas a una
/// dependencia. Patrón "bandeja por área" (P2 de
/// frontend/patrones-compras.md) — el responsable del área ve solo
/// las facturas que le corresponden, ordenadas por antigüedad para
/// resaltar las que ya cumplieron SLA. Los días en revisión son hábiles
/// (<see cref="PlazoRevision"/>), cada fila trae el plazo del motivo
/// (<c>SlaDias</c>) y el nivel del último aviso emitido en el episodio
/// vigente (aviso visible para el área, P8.4a A2).
/// </summary>
public sealed record FacturasEnRevisionPorAreaQuery(
    Guid DependenciaRevisoraId,
    Guid? MotivoRevisionId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<FacturaEnRevisionResponse>>, Millet.SharedKernel.Application.ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.facturas.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed record FacturaEnRevisionResponse(
    Guid Id,
    Guid ProveedorId,
    Guid SucursalId,
    string? FolioProveedor,
    string? SerieProveedor,
    DateTimeOffset FechaDocumento,
    DateOnly FechaVencimiento,
    decimal Total,
    decimal SaldoPendiente,
    string Moneda,
    Guid MotivoRevisionId,
    DateTimeOffset FechaEntradaRevision,
    int DiasEnRevision,
    int Version,
    int? SlaDias = null,
    // 1 = aviso al gerente, 2 = plazo vencido, 3 = escalada a Dirección de Finanzas;
    // null = sin aviso en el episodio vigente.
    int? NivelAviso = null,
    // Etiquetas resueltas server-side (C-06): proveedor por IProveedorReadPort en lote,
    // motivo por catálogo local y área por IDependenciaRevisoraReadPort.
    string? ProveedorNombre = null,
    string? MotivoNombre = null,
    string? AreaNombre = null,
    // Días hábiles >= umbral de plazo vencido del motivo (PlazoRevision.Umbrales).
    bool Vencida = false);

public sealed class FacturasEnRevisionPorAreaHandler
    : IRequestHandler<FacturasEnRevisionPorAreaQuery, PagedResponse<FacturaEnRevisionResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly ICalendarioHabil _calendario;
    private readonly IProveedorReadPort _proveedores;
    private readonly IDependenciaRevisoraReadPort _dependencias;

    public FacturasEnRevisionPorAreaHandler(
        CuentasPorPagarDbContext db, IClock clock, ICalendarioHabil calendario,
        IProveedorReadPort proveedores, IDependenciaRevisoraReadPort dependencias)
    {
        _proveedores = proveedores;
        _dependencias = dependencias;
        _db = db;
        _clock = clock;
        _calendario = calendario;
    }

    public async Task<PagedResponse<FacturaEnRevisionResponse>> Handle(
        FacturasEnRevisionPorAreaQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.FacturasProveedor
            .Where(x => query.SucursalesPermitidas == null || query.SucursalesPermitidas.Contains(x.SucursalId)).AsNoTracking()
            .Where(f => f.EnRevision && f.DependenciaRevisoraId == query.DependenciaRevisoraId);

        if (query.MotivoRevisionId is Guid m)
            q = q.Where(f => f.MotivoRevisionId == m);

        var total = await q.CountAsync(cancellationToken);

        var ahora = _clock.UtcNow;
        var items = await q
            .OrderBy(f => f.FechaEntradaRevision) // más viejas primero
            .Skip(offset)
            .Take(limit)
            .Select(f => new FacturaEnRevisionResponse(
                f.Id,
                f.ProveedorId,
                f.SucursalId,
                f.FolioProveedor,
                f.SerieProveedor,
                f.FechaDocumento,
                f.FechaVencimiento,
                f.Total,
                f.Total - f.AnticipoAplicadoTotal - f.NcAplicadasTotal - f.CargosAplicadosTotal - f.ImportePagado,
                f.Moneda,
                f.MotivoRevisionId!.Value,
                f.FechaEntradaRevision!.Value,
                0, // días hábiles: se calculan después de la consulta
                f.Version))
            .ToListAsync(cancellationToken);

        var ids = items.Select(i => i.Id).ToList();
        var motivosIds = items.Select(i => i.MotivoRevisionId).Distinct().ToList();
        var motivos = await _db.MotivosRevision.AsNoTracking()
            .Where(m => motivosIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => new { m.SlaDias, m.Nombre }, cancellationToken);
        var proveedores = await _proveedores.ObtenerNombresPorIdsAsync(
            items.Select(i => i.ProveedorId).Distinct().ToList(), cancellationToken);
        var area = await _dependencias.ObtenerAsync(query.DependenciaRevisoraId, cancellationToken);
        var avisos = await _db.AvisosSlaRevision.AsNoTracking()
            .Where(a => ids.Contains(a.FacturaProveedorId))
            .Select(a => new { a.FacturaProveedorId, a.FechaEntradaRevision, a.Nivel })
            .ToListAsync(cancellationToken);

        var cache = new Dictionary<DateOnly, int>();
        var resultado = new List<FacturaEnRevisionResponse>(items.Count);
        foreach (var item in items)
        {
            var dias = await PlazoRevision.DiasHabilesAsync(_calendario, item.FechaEntradaRevision, ahora, cache, cancellationToken);
            motivos.TryGetValue(item.MotivoRevisionId, out var motivo);
            resultado.Add(item with
            {
                DiasEnRevision = dias,
                SlaDias = motivo?.SlaDias,
                MotivoNombre = motivo?.Nombre,
                AreaNombre = area?.Nombre,
                ProveedorNombre = proveedores.GetValueOrDefault(item.ProveedorId),
                Vencida = motivo?.SlaDias is int sla && dias >= PlazoRevision.Umbrales(sla).Vencido,
                // Solo cuentan los avisos del episodio vigente (misma FechaEntradaRevision).
                NivelAviso = avisos
                    .Where(a => a.FacturaProveedorId == item.Id && a.FechaEntradaRevision == item.FechaEntradaRevision)
                    .Select(a => (int?)a.Nivel)
                    .Max(),
            });
        }

        return new PagedResponse<FacturaEnRevisionResponse>(resultado, offset, limit, total);
    }
}
