using FluentValidation;
using MediatR;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.Reportes.AntiguedadSaldos;

public sealed record AntiguedadSaldosProveedoresQuery(DateOnly? FechaCorte = null, Guid? ProveedorId = null,
    Guid? SucursalId = null) : IRequest<ReporteJsonResponse>;
public sealed class AntiguedadSaldosProveedoresValidator : AbstractValidator<AntiguedadSaldosProveedoresQuery>
{
    public AntiguedadSaldosProveedoresValidator()
    {
        RuleFor(q => q.FechaCorte).Must(f => f is null || f < DateOnly.MaxValue).WithMessage("Fecha de corte inválida.");
        RuleFor(q => q.ProveedorId).Must(id => id is null || id != Guid.Empty).WithMessage("Proveedor inválido.");
        RuleFor(q => q.SucursalId).Must(id => id is null || id != Guid.Empty).WithMessage("Sucursal inválida.");
    }
}
public sealed class AntiguedadSaldosProveedoresHandler(SaldosHistoricos lector, IClock clock)
    : IRequestHandler<AntiguedadSaldosProveedoresQuery, ReporteJsonResponse>
{
    public async Task<ReporteJsonResponse> Handle(AntiguedadSaldosProveedoresQuery query, CancellationToken cancellationToken)
    {
        var corte = query.FechaCorte ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var saldos = await lector.LeerAsync(corte, query.ProveedorId, query.SucursalId, null, cancellationToken);

        var filas = ReporteHistoricoBuilder.Agrupar(saldos, corte, false);
        return new("Antigüedad de saldos por proveedor", clock.UtcNow, ReporteHistoricoBuilder.Filtros(corte, query.ProveedorId, query.SucursalId, nombreProveedor: (saldos.Count > 0 ? saldos[0].Nombre : null)),
            ReporteHistoricoBuilder.Columnas(false, false), filas,
            ReporteHistoricoBuilder.Totales(filas, [.. ReporteHistoricoBuilder.Buckets, "total", "numero_facturas"]));
    }
}
