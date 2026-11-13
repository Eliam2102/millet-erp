using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Application.Reportes;

/// <summary>
/// M-339 (Costos, 10-nov): consumo de insumos por línea. Las salidas por consumo y por vale (PVB, silicón, Ködispace, butilo, argón)
/// bajan el inventario a costo y se cargan a «costo por insumos de producción»; aquí se agrupan por línea (centro de costo de la
/// línea de salida) y artículo. Las devoluciones internas ligadas a esas salidas restan cantidad y costo. El consumo por norma
/// (estadístico) no se muestra.
/// </summary>
public sealed record ConsumoInsumosLineaQuery(
    DateOnly Desde,
    DateOnly Hasta,
    Guid? SucursalId = null,
    Guid? CentroCostoId = null,
    Guid? ArticuloId = null) : IRequest<ReporteResponse<ConsumoInsumosLineaFila>>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "almacen.reportes.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class ConsumoInsumosLineaQueryValidator : AbstractValidator<ConsumoInsumosLineaQuery>
{
    public ConsumoInsumosLineaQueryValidator()
    {
        RuleFor(x => x).Must(x => x.Desde <= x.Hasta)
            .WithMessage("La fecha inicial no puede ser posterior a la final.")
            .WithName("Desde");
    }
}

public sealed record ConsumoInsumosLineaFila(
    Guid? CentroCostoId,
    string Linea,
    Guid ArticuloId,
    string ArticuloClave,
    string ArticuloDescripcion,
    string Unidad,
    decimal Cantidad,
    decimal CostoUnitarioPromedioMxn,
    decimal CostoTotalMxn);

public sealed class ConsumoInsumosLineaHandler(AlmacenDbContext db, IArticuloReadPort articulos, ICentroCostoReadPort centros)
    : IRequestHandler<ConsumoInsumosLineaQuery, ReporteResponse<ConsumoInsumosLineaFila>>
{
    public const string SinLinea = "Sin línea";

    public async Task<ReporteResponse<ConsumoInsumosLineaFila>> Handle(ConsumoInsumosLineaQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (request.SucursalId is { } s && request.SucursalesPermitidas is { } permitidas && !permitidas.Contains(s))
            throw new Millet.SharedKernel.Application.Exceptions.ForbiddenException("SUCURSAL_NO_ASOCIADA", "No tienes acceso a los datos de esta sucursal.");

        var salidas = await (
            from l in db.LineasMovimiento.AsNoTracking()
            join m in db.Movimientos.AsNoTracking() on l.MovimientoId equals m.Id
            join u in db.Ubicaciones.AsNoTracking() on l.UbicacionId equals (Guid?)u.Id
            join sa in db.SubAlmacenes.AsNoTracking() on u.SubAlmacenId equals sa.Id
            join a in db.Almacenes.AsNoTracking() on sa.AlmacenId equals a.Id
            where m.Estado == EstadoMovimiento.Registrado
               && (m.Tipo == TipoMovimiento.SalidaConsumo || m.Tipo == TipoMovimiento.SalidaPorVale)
               && m.FechaMovimiento >= request.Desde && m.FechaMovimiento <= request.Hasta
               && (request.SucursalId == null || a.SucursalId == request.SucursalId)
               && (request.SucursalesPermitidas == null || request.SucursalesPermitidas.Contains(a.SucursalId))
               && (request.CentroCostoId == null || l.CentroCostoId == request.CentroCostoId)
               && (request.ArticuloId == null || l.ArticuloId == request.ArticuloId)
            select new { l.Id, l.CentroCostoId, l.ArticuloId, l.Cantidad, l.CostoUnitarioMxn }).ToListAsync(ct);

        // Devoluciones internas registradas ligadas a esas líneas de salida: restan cantidad y costo.
        var ids = salidas.Select(x => x.Id).ToList();
        var devueltas = (await (
            from l in db.LineasMovimiento.AsNoTracking()
            join m in db.Movimientos.AsNoTracking() on l.MovimientoId equals m.Id
            where m.Estado == EstadoMovimiento.Registrado && m.Tipo == TipoMovimiento.DevolucionSalida
               && l.LineaSalidaOrigenId != null && ids.Contains(l.LineaSalidaOrigenId.Value)
            select new { Origen = l.LineaSalidaOrigenId!.Value, l.Cantidad, l.CostoUnitarioMxn }).ToListAsync(ct))
            .GroupBy(d => d.Origen)
            .ToDictionary(g => g.Key, g => (Cantidad: g.Sum(d => d.Cantidad), Costo: g.Sum(d => Math.Round(d.Cantidad * d.CostoUnitarioMxn, 2))));

        var netas = salidas.Select(x =>
        {
            var (dc, dv) = devueltas.GetValueOrDefault(x.Id);
            return (x.CentroCostoId, x.ArticuloId, Cantidad: x.Cantidad - dc, Costo: Math.Round(x.Cantidad * x.CostoUnitarioMxn, 2) - dv);
        }).ToList();

        var arts = await articulos.ObtenerPorIdsAsync(netas.Select(n => n.ArticuloId).Distinct().ToList(), ct);
        var cc = await centros.ObtenerAsync(netas.Where(n => n.CentroCostoId != null).Select(n => n.CentroCostoId!.Value).Distinct().ToList(), ct);

        var filas = netas.GroupBy(n => (n.CentroCostoId, n.ArticuloId))
            .Select(g =>
            {
                var cant = g.Sum(n => n.Cantidad);
                var costo = g.Sum(n => n.Costo);
                var art = arts.GetValueOrDefault(g.Key.ArticuloId);
                var linea = g.Key.CentroCostoId is { } id
                    ? cc.TryGetValue(id, out var c) ? $"{c.Clave} · {c.Nombre}" : "No catalogado"
                    : SinLinea;
                return new ConsumoInsumosLineaFila(g.Key.CentroCostoId, linea, g.Key.ArticuloId, art?.Clave ?? g.Key.ArticuloId.ToString(),
                    art?.Descripcion ?? string.Empty, art?.UnidadMedida ?? string.Empty, cant, cant != 0 ? Math.Round(costo / cant, 4) : 0m, costo);
            })
            .Where(f => f.Cantidad != 0 || f.CostoTotalMxn != 0)
            .OrderBy(f => f.Linea, StringComparer.Ordinal).ThenBy(f => f.ArticuloClave, StringComparer.Ordinal)
            .ToList();

        var filtros = new Dictionary<string, string?>
        {
            ["desde"] = request.Desde.ToString("yyyy-MM-dd"),
            ["hasta"] = request.Hasta.ToString("yyyy-MM-dd"),
            ["sucursal_id"] = request.SucursalId?.ToString(),
            ["centro_costo_id"] = request.CentroCostoId?.ToString(),
            ["articulo_id"] = request.ArticuloId?.ToString(),
        };
        return new ReporteResponse<ConsumoInsumosLineaFila>(
            Titulo: "CONSUMO-INSUMOS-LINEA",
            GeneradoEn: DateTimeOffset.UtcNow,
            FiltrosAplicados: filtros,
            Columnas: Columnas,
            Filas: filas,
            Totales: new Dictionary<string, decimal> { ["costoTotalMxn"] = filas.Sum(f => f.CostoTotalMxn) });
    }

    public static readonly IReadOnlyList<ColumnaDescriptor> Columnas =
    [
        new("linea", "Línea", "texto"),
        new("articuloClave", "Artículo", "texto"),
        new("articuloDescripcion", "Descripción", "texto"),
        new("unidad", "Unidad", "texto"),
        new("cantidad", "Cantidad", "numero", "right"),
        new("costoUnitarioPromedioMxn", "Costo unit. prom.", "moneda", "right"),
        new("costoTotalMxn", "Costo total", "moneda", "right"),
    ];
}
