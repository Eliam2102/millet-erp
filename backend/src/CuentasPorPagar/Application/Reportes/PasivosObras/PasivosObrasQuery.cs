using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Reportes.PasivosObras;

/// <summary>
/// Reporte de pasivos para el módulo Obras (F8-PR2). El módulo Obras
/// (no implementado todavía) lo consumirá vía HTTP para integrar el
/// costeo de proyectos. F8-PR2 entrega el endpoint funcional y
/// estandarizado vía ADR-0036; cuando Obras se conecte, su
/// frontend/backend lo invoca igual que el resto del ERP.
///
/// <para>
/// <b>Modelo provisional</b>: el reporte filtra facturas por
/// <see cref="SucursalId"/> (la "obra" se materializa como la sucursal
/// destino de las recepciones de Almacén) y opcionalmente por
/// proveedor. Devuelve facturas vivas (no canceladas) con su saldo
/// pendiente + bucket de antigüedad — la misma cruz que el reporte
/// de antigüedad pero orientada a costos por sucursal/obra.
/// </para>
///
/// <para>
/// PLATFORM-TODO(<c>ObrasReadPort</c>): cuando llegue el módulo Obras,
/// agregar un puerto que mapee SucursalId+ProyectoId a "obra" y filtrar
/// también por proyecto.
/// </para>
/// </summary>
public sealed record PasivosObrasQuery(
    Guid SucursalId,
    DateOnly? FechaCorte = null,
    Guid? ProveedorId = null) : IRequest<ReporteJsonResponse>;

public sealed class PasivosObrasHandler
    : IRequestHandler<PasivosObrasQuery, ReporteJsonResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly SharedKernel.Application.IClock _clock;

    public PasivosObrasHandler(CuentasPorPagarDbContext db, SharedKernel.Application.IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task<ReporteJsonResponse> Handle(
        PasivosObrasQuery query, CancellationToken cancellationToken)
    {
        var fechaCorte = query.FechaCorte ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        var q = _db.FacturasProveedor.AsNoTracking()
            .Where(f =>
                f.SucursalId == query.SucursalId
                && f.Estado != EstadoPasivo.Cancelada
                && (f.Total - f.AnticipoAplicadoTotal - f.NcAplicadasTotal - f.ImportePagado) > 0);

        if (query.ProveedorId is Guid p) q = q.Where(f => f.ProveedorId == p);

        var facturas = await q
            .OrderBy(f => f.FechaVencimiento)
            .Select(f => new
            {
                f.Id,
                f.ProveedorId,
                f.FolioProveedor,
                f.SerieProveedor,
                f.UuidCfdi,
                f.FechaDocumento,
                f.FechaVencimiento,
                f.OrdenCompraId,
                f.Moneda,
                f.Total,
                f.AnticipoAplicadoTotal,
                f.NcAplicadasTotal,
                f.ImportePagado,
                SaldoPendiente = f.Total - f.AnticipoAplicadoTotal - f.NcAplicadasTotal - f.ImportePagado,
                f.Estado,
            })
            .ToListAsync(cancellationToken);

        var filas = facturas
            .Select<dynamic, IReadOnlyDictionary<string, object?>>(f => new Dictionary<string, object?>
            {
                ["factura_id"]            = f.Id,
                ["proveedor_id"]          = f.ProveedorId,
                ["folio_proveedor"]       = f.FolioProveedor,
                ["serie_proveedor"]       = f.SerieProveedor,
                ["uuid_cfdi"]             = f.UuidCfdi,
                ["fecha_documento"]       = f.FechaDocumento,
                ["fecha_vencimiento"]     = f.FechaVencimiento,
                ["dias_vencimiento"]      = fechaCorte.DayNumber - f.FechaVencimiento.DayNumber,
                ["bucket"]                = BucketsAntiguedad.CalcularBucket(f.FechaVencimiento, fechaCorte),
                ["orden_compra_id"]      = f.OrdenCompraId,
                ["moneda"]                = f.Moneda,
                ["total"]                 = f.Total,
                ["anticipo_aplicado"]     = f.AnticipoAplicadoTotal,
                ["nc_aplicadas"]          = f.NcAplicadasTotal,
                ["importe_pagado"]        = f.ImportePagado,
                ["saldo_pendiente"]       = f.SaldoPendiente,
                ["estado"]                = f.Estado.ToString(),
            })
            .ToList();

        var totales = new Dictionary<string, object?>
        {
            ["numero_facturas"]        = facturas.Count,
            ["total_facturado"]        = facturas.Sum(f => f.Total),
            ["total_anticipo_aplicado"] = facturas.Sum(f => f.AnticipoAplicadoTotal),
            ["total_nc_aplicadas"]     = facturas.Sum(f => f.NcAplicadasTotal),
            ["total_pagado"]           = facturas.Sum(f => f.ImportePagado),
            ["total_saldo_pendiente"]  = facturas.Sum(f => f.SaldoPendiente),
        };

        return new ReporteJsonResponse(
            Titulo: $"Pasivos por sucursal/obra — {query.SucursalId}",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: ConstruirFiltros(query.SucursalId, fechaCorte, query.ProveedorId),
            Columnas: new[]
            {
                new ColumnaReporte("proveedor_id",      "Proveedor",         TipoColumnaReporte.Texto,   AlineacionColumna.Izquierda),
                new ColumnaReporte("folio_proveedor",   "Folio",             TipoColumnaReporte.Texto,   AlineacionColumna.Izquierda),
                new ColumnaReporte("serie_proveedor",   "Serie",             TipoColumnaReporte.Texto,   AlineacionColumna.Centro),
                new ColumnaReporte("uuid_cfdi",         "UUID CFDI",         TipoColumnaReporte.Texto,   AlineacionColumna.Izquierda),
                new ColumnaReporte("fecha_documento",   "Fecha documento",   TipoColumnaReporte.Fecha,   AlineacionColumna.Centro),
                new ColumnaReporte("fecha_vencimiento", "Vencimiento",       TipoColumnaReporte.Fecha,   AlineacionColumna.Centro),
                new ColumnaReporte("dias_vencimiento",  "Días",              TipoColumnaReporte.Entero,  AlineacionColumna.Derecha),
                new ColumnaReporte("bucket",            "Bucket",            TipoColumnaReporte.Enum,    AlineacionColumna.Centro),
                new ColumnaReporte("orden_compra_id",   "OC",                TipoColumnaReporte.Texto,   AlineacionColumna.Izquierda),
                new ColumnaReporte("moneda",            "Moneda",            TipoColumnaReporte.Texto,   AlineacionColumna.Centro),
                new ColumnaReporte("total",             "Total",             TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("anticipo_aplicado", "Anticipo apl.",     TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("nc_aplicadas",      "NC apl.",           TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("importe_pagado",    "Pagado",            TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("saldo_pendiente",   "Saldo",             TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("estado",            "Estado",            TipoColumnaReporte.Enum,    AlineacionColumna.Centro),
            },
            Filas: filas,
            Totales: totales);
    }

    private static List<FiltroAplicado> ConstruirFiltros(Guid sucursalId, DateOnly fechaCorte, Guid? proveedorId)
    {
        var lista = new List<FiltroAplicado>
        {
            new("Sucursal/obra", sucursalId.ToString()),
            new("Fecha de corte", fechaCorte.ToString("yyyy-MM-dd")),
        };
        if (proveedorId is Guid p) lista.Add(new("Proveedor", p.ToString()));
        return lista;
    }
}
