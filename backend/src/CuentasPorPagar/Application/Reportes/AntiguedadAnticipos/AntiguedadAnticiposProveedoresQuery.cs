using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Reportes.AntiguedadAnticipos;

/// <summary>
/// Reporte de antigüedad de anticipos a proveedores (F8-PR1). Columnas
/// separadas para que el área vea cuánto se entregó, cuánto se
/// amortizó, y cuánto saldo amortizable queda por aplicar
/// (§13.1 punto 1 del 00-levantamiento).
/// </summary>
public sealed record AntiguedadAnticiposProveedoresQuery(
    DateOnly? FechaCorte = null,
    Guid? ProveedorId = null) : IRequest<ReporteJsonResponse>;

public sealed class AntiguedadAnticiposProveedoresHandler
    : IRequestHandler<AntiguedadAnticiposProveedoresQuery, ReporteJsonResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly SharedKernel.Application.IClock _clock;

    public AntiguedadAnticiposProveedoresHandler(
        CuentasPorPagarDbContext db, SharedKernel.Application.IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task<ReporteJsonResponse> Handle(
        AntiguedadAnticiposProveedoresQuery query, CancellationToken cancellationToken)
    {
        var fechaCorte = query.FechaCorte ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        var q = _db.AnticiposProveedor.AsNoTracking()
            .Where(a => a.Estado != EstadoAnticipo.Cancelado);

        if (query.ProveedorId is Guid p) q = q.Where(a => a.ProveedorId == p);

        var anticipos = await q
            .Select(a => new
            {
                a.Id,
                a.ProveedorId,
                a.UuidCfdi,
                a.FolioProveedor,
                a.FechaCfdi,
                a.MontoEntregado,
                a.MontoAmortizado,
                SaldoAmortizable = a.MontoEntregado - a.MontoAmortizado,
                a.Moneda,
                a.Estado,
                a.OrdenCompraId,
            })
            .ToListAsync(cancellationToken);

        var filas = anticipos
            .OrderBy(a => a.ProveedorId).ThenBy(a => a.FechaCfdi)
            .Select<dynamic, IReadOnlyDictionary<string, object?>>(a => new Dictionary<string, object?>
            {
                ["anticipo_id"]       = a.Id,
                ["proveedor_id"]      = a.ProveedorId,
                ["uuid_cfdi"]         = a.UuidCfdi,
                ["folio_proveedor"]   = a.FolioProveedor,
                ["fecha_cfdi"]        = a.FechaCfdi,
                ["moneda"]            = a.Moneda,
                ["monto_entregado"]   = a.MontoEntregado,
                ["monto_amortizado"]  = a.MontoAmortizado,
                ["saldo_amortizable"] = a.SaldoAmortizable,
                ["estado"]            = a.Estado.ToString(),
                ["orden_compra_id"]   = a.OrdenCompraId,
                ["dias_desde_cfdi"]   = fechaCorte.DayNumber - DateOnly.FromDateTime(((DateTimeOffset)a.FechaCfdi).UtcDateTime).DayNumber,
            })
            .ToList();

        var totales = new Dictionary<string, object?>
        {
            ["monto_entregado"]   = anticipos.Sum(a => a.MontoEntregado),
            ["monto_amortizado"]  = anticipos.Sum(a => a.MontoAmortizado),
            ["saldo_amortizable"] = anticipos.Sum(a => a.SaldoAmortizable),
            ["numero_anticipos"]  = anticipos.Count,
        };

        return new ReporteJsonResponse(
            Titulo: "Antigüedad de anticipos a proveedores",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: ConstruirFiltros(fechaCorte, query.ProveedorId),
            Columnas: new[]
            {
                new ColumnaReporte("proveedor_id",      "Proveedor",            TipoColumnaReporte.Texto,   AlineacionColumna.Izquierda),
                new ColumnaReporte("uuid_cfdi",         "UUID CFDI",            TipoColumnaReporte.Texto,   AlineacionColumna.Izquierda),
                new ColumnaReporte("folio_proveedor",   "Folio proveedor",      TipoColumnaReporte.Texto,   AlineacionColumna.Izquierda),
                new ColumnaReporte("fecha_cfdi",        "Fecha CFDI",           TipoColumnaReporte.Fecha,   AlineacionColumna.Centro),
                new ColumnaReporte("dias_desde_cfdi",   "Días",                 TipoColumnaReporte.Entero,  AlineacionColumna.Derecha),
                new ColumnaReporte("moneda",            "Moneda",               TipoColumnaReporte.Texto,   AlineacionColumna.Centro),
                new ColumnaReporte("monto_entregado",   "Entregado",            TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("monto_amortizado",  "Amortizado",           TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("saldo_amortizable", "Saldo amortizable",    TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("estado",            "Estado",               TipoColumnaReporte.Enum,    AlineacionColumna.Centro),
                new ColumnaReporte("orden_compra_id",   "OC",                   TipoColumnaReporte.Texto,   AlineacionColumna.Izquierda),
            },
            Filas: filas,
            Totales: totales);
    }

    private static List<FiltroAplicado> ConstruirFiltros(DateOnly fechaCorte, Guid? proveedorId)
    {
        var lista = new List<FiltroAplicado>
        {
            new("Fecha de corte", fechaCorte.ToString("yyyy-MM-dd")),
        };
        if (proveedorId is Guid p) lista.Add(new("Proveedor", p.ToString()));
        return lista;
    }
}
