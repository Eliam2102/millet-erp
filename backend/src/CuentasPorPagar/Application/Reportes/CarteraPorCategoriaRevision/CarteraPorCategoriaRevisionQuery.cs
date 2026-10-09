using FluentValidation;
using MediatR;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.Reportes.CarteraPorCategoriaRevision;

public sealed record CarteraPorCategoriaRevisionQuery(DateOnly? FechaCorte = null, Guid? ProveedorId = null,
    Guid? SucursalId = null, bool? SoloEnRevision = null) : IRequest<ReporteJsonResponse>;
public sealed class CarteraPorCategoriaRevisionValidator : AbstractValidator<CarteraPorCategoriaRevisionQuery>
{
    public CarteraPorCategoriaRevisionValidator()
    {
        RuleFor(q => q.FechaCorte).Must(f => f is null || f < DateOnly.MaxValue).WithMessage("Fecha de corte inválida.");
        RuleFor(q => q.ProveedorId).Must(id => id is null || id != Guid.Empty).WithMessage("Proveedor inválido.");
        RuleFor(q => q.SucursalId).Must(id => id is null || id != Guid.Empty).WithMessage("Sucursal inválida.");
    }
}
public sealed class CarteraPorCategoriaRevisionHandler(SaldosHistoricos lector, IClock clock)
    : IRequestHandler<CarteraPorCategoriaRevisionQuery, ReporteJsonResponse>
{
    public async Task<ReporteJsonResponse> Handle(CarteraPorCategoriaRevisionQuery query, CancellationToken cancellationToken)
    {
        var corte = query.FechaCorte ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var saldos = await lector.LeerAsync(corte, query.ProveedorId, query.SucursalId, null, cancellationToken);
        if (query.SoloEnRevision is bool revision) saldos = saldos.Where(s => s.EnRevision == revision).ToArray();
        var filas = ReporteHistoricoBuilder.Agrupar(saldos, corte, true);
        return new("Cartera de proveedores por revisión", clock.UtcNow, ReporteHistoricoBuilder.Filtros(corte, query.ProveedorId, query.SucursalId, nombreProveedor: (saldos.Count > 0 ? saldos[0].Nombre : null)),
            ReporteHistoricoBuilder.Columnas(true, false), filas,
            ReporteHistoricoBuilder.Totales(filas, [.. ReporteHistoricoBuilder.Buckets, "total", "numero_facturas"]));
    }
}
