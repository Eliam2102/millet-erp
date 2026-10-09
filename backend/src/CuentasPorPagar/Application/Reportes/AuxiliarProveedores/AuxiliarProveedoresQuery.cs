using FluentValidation;
using MediatR;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.Reportes.AuxiliarProveedores;

public sealed record AuxiliarProveedoresQuery(DateOnly? FechaCorte = null, Guid? ProveedorId = null,
    Guid? SucursalId = null) : IRequest<ReporteJsonResponse>;
public sealed class AuxiliarProveedoresValidator : AbstractValidator<AuxiliarProveedoresQuery>
{
    public AuxiliarProveedoresValidator()
    {
        RuleFor(q => q.FechaCorte).Must(f => f is null || f < DateOnly.MaxValue).WithMessage("Fecha de corte inválida.");
        RuleFor(q => q.ProveedorId).Must(id => id is null || id != Guid.Empty).WithMessage("Proveedor inválido.");
        RuleFor(q => q.SucursalId).Must(id => id is null || id != Guid.Empty).WithMessage("Sucursal inválida.");
    }
}
public sealed class AuxiliarProveedoresHandler(SaldosHistoricos lector, IClock clock)
    : IRequestHandler<AuxiliarProveedoresQuery, ReporteJsonResponse>
{
    public async Task<ReporteJsonResponse> Handle(AuxiliarProveedoresQuery query, CancellationToken cancellationToken)
    {
        var corte = query.FechaCorte ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var saldos = await lector.LeerAsync(corte, query.ProveedorId, query.SucursalId, null, cancellationToken);

        var filas = ReporteHistoricoBuilder.Agrupar(saldos, corte, false);
        return new("Auxiliar de proveedores a una fecha", clock.UtcNow, ReporteHistoricoBuilder.Filtros(corte, query.ProveedorId, query.SucursalId, nombreProveedor: (saldos.Count > 0 ? saldos[0].Nombre : null)),
            ReporteHistoricoBuilder.Columnas(false, true), filas,
            ReporteHistoricoBuilder.Totales(filas, [.. ReporteHistoricoBuilder.Buckets, "total", "numero_facturas"]));
    }
}
