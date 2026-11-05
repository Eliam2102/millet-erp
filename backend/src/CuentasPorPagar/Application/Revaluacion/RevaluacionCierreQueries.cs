using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Revaluacion;

public sealed record RevaluacionCierreListItem(Guid Id, int Anio, int Mes, DateOnly FechaCierre, DateOnly FechaReversa, decimal TotalPerdida,
    decimal TotalUtilidad, int Documentos, bool Revertida);

public sealed record ListarRevaluacionesCierreQuery : IRequest<IReadOnlyList<RevaluacionCierreListItem>>;

public sealed class ListarRevaluacionesCierreHandler(CuentasPorPagarDbContext db)
    : IRequestHandler<ListarRevaluacionesCierreQuery, IReadOnlyList<RevaluacionCierreListItem>>
{
    public async Task<IReadOnlyList<RevaluacionCierreListItem>> Handle(ListarRevaluacionesCierreQuery _, CancellationToken cancellationToken) =>
        await db.RevaluacionesCierre.AsNoTracking().OrderByDescending(r => r.FechaCierre)
            .Select(r => new RevaluacionCierreListItem(r.Id, r.Anio, r.Mes, r.FechaCierre, r.FechaReversa, r.TotalPerdida, r.TotalUtilidad,
                r.Detalles.Count, r.ReversaPublicadaEn != null)).ToListAsync(cancellationToken);
}

/// <summary>CXP-12 · reporte de la revaluación de un mes por proveedor y documento (ADR-0036).</summary>
public sealed record ReporteRevaluacionCierreQuery(int Anio, int Mes) : IRequest<ReporteJsonResponse>;

public sealed class ReporteRevaluacionCierreValidator : AbstractValidator<ReporteRevaluacionCierreQuery>
{
    public ReporteRevaluacionCierreValidator()
    {
        RuleFor(q => q.Anio).InclusiveBetween(2000, 2100).WithMessage("Año inválido.");
        RuleFor(q => q.Mes).InclusiveBetween(1, 12).WithMessage("Mes inválido.");
    }
}

public sealed class ReporteRevaluacionCierreHandler(CuentasPorPagarDbContext db, IProveedorReadPort proveedores, IClock clock)
    : IRequestHandler<ReporteRevaluacionCierreQuery, ReporteJsonResponse>
{
    public async Task<ReporteJsonResponse> Handle(ReporteRevaluacionCierreQuery q, CancellationToken cancellationToken)
    {
        var r = await db.RevaluacionesCierre.AsNoTracking().Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.Anio == q.Anio && x.Mes == q.Mes, cancellationToken)
            ?? throw new EntityNotFoundException("REVALUACION_NO_ENCONTRADA", $"El mes {q.Mes:00}/{q.Anio} no se ha revaluado.");
        var facturaIds = r.Detalles.Select(d => d.FacturaProveedorId).ToList();
        var documentos = await db.FacturasProveedor.AsNoTracking().Where(f => facturaIds.Contains(f.Id))
            .Select(f => new { f.Id, f.SerieProveedor, f.FolioProveedor }).ToDictionaryAsync(f => f.Id, cancellationToken);
        var nombres = await proveedores.ObtenerNombresPorIdsAsync([.. r.Detalles.Select(d => d.ProveedorId).Distinct()], cancellationToken);
        var rfcs = await proveedores.ObtenerRfcsPorIdsAsync([.. r.Detalles.Select(d => d.ProveedorId).Distinct()], cancellationToken);

        var filas = r.Detalles
            .Select(d => (Detalle: d, Proveedor: nombres.GetValueOrDefault(d.ProveedorId, "[PROVEEDOR POR CONFIRMAR]")))
            .OrderBy(x => x.Proveedor).ThenBy(x => x.Detalle.Moneda).ThenBy(x => x.Detalle.Id)
            .Select(x =>
            {
                var d = x.Detalle;
                documentos.TryGetValue(d.FacturaProveedorId, out var f);
                var folio = string.Join('-', new[] { f?.SerieProveedor, f?.FolioProveedor }.Where(s => !string.IsNullOrWhiteSpace(s)));
                return (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                {
                    ["proveedor_nombre"] = x.Proveedor, ["rfc"] = rfcs.GetValueOrDefault(d.ProveedorId, "[RFC POR CONFIRMAR]"),
                    ["documento"] = folio.Length > 0 ? folio : d.FacturaProveedorId.ToString(), ["divisa"] = d.Moneda, ["saldo"] = d.Saldo,
                    ["tc_factura"] = d.TcFactura, ["tc_cierre"] = d.TcCierre, ["importe_libros"] = d.ImporteLibros,
                    ["importe_cierre"] = d.ImporteCierre, ["diferencia"] = d.Diferencia,
                    ["resultado"] = d.Diferencia > 0 ? "Pérdida" : d.Diferencia < 0 ? "Utilidad" : "Sin diferencia",
                };
            }).ToArray();

        return new("Revaluación de saldos en moneda extranjera", clock.UtcNow,
            [new("Cierre", r.FechaCierre.ToString("yyyy-MM-dd")), new("Reversa automática", r.FechaReversa.ToString("yyyy-MM-dd")),
             new("Reversa", r.ReversaPublicadaEn is null ? "Pendiente" : "Publicada")],
            [ReporteHistoricoBuilder.Texto("proveedor_nombre", "Proveedor"), ReporteHistoricoBuilder.Texto("rfc", "RFC"),
             ReporteHistoricoBuilder.Texto("documento", "Documento"), ReporteHistoricoBuilder.Texto("divisa", "Moneda"),
             new("saldo", "Saldo en moneda", TipoColumnaReporte.Numerico, AlineacionColumna.Derecha),
             new("tc_factura", "TC factura", TipoColumnaReporte.Numerico, AlineacionColumna.Derecha),
             new("tc_cierre", "TC cierre", TipoColumnaReporte.Numerico, AlineacionColumna.Derecha),
             ReporteHistoricoBuilder.Importe("importe_libros", "En libros (MXN)"), ReporteHistoricoBuilder.Importe("importe_cierre", "Al cierre (MXN)"),
             ReporteHistoricoBuilder.Importe("diferencia", "Diferencia (MXN)"), ReporteHistoricoBuilder.Texto("resultado", "Resultado")],
            filas,
            new Dictionary<string, object?>
            {
                ["importe_libros"] = r.Detalles.Sum(d => d.ImporteLibros), ["importe_cierre"] = r.Detalles.Sum(d => d.ImporteCierre),
                ["diferencia"] = r.TotalPerdida - r.TotalUtilidad,
                ["perdida"] = r.TotalPerdida, ["utilidad"] = r.TotalUtilidad, ["neto"] = r.TotalPerdida - r.TotalUtilidad,
            });
    }
}
