using FluentValidation;
using MediatR;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.Reportes.PasivosObras;

public sealed record PasivosObrasQuery(Guid? SucursalId = null, DateOnly? FechaCorte = null,
    Guid? ProveedorId = null, string? Obra = null) : IRequest<ReporteJsonResponse>;
public sealed class PasivosObrasValidator : AbstractValidator<PasivosObrasQuery>
{
    public PasivosObrasValidator()
    {
        RuleFor(q => q.Obra).MaximumLength(120);
        RuleFor(q => q.FechaCorte).Must(f => f is null || f < DateOnly.MaxValue).WithMessage("Fecha de corte inválida.");
        RuleFor(q => q.SucursalId).Must(id => id is null || id != Guid.Empty).WithMessage("Sucursal inválida.");
        RuleFor(q => q.ProveedorId).Must(id => id is null || id != Guid.Empty).WithMessage("Proveedor inválido.");
    }
}
public sealed class PasivosObrasHandler(SaldosHistoricos lector, IClock clock) : IRequestHandler<PasivosObrasQuery, ReporteJsonResponse>
{
    public async Task<ReporteJsonResponse> Handle(PasivosObrasQuery q, CancellationToken cancellationToken)
    {
        var corte = q.FechaCorte ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var saldos = await lector.LeerAsync(corte, q.ProveedorId, q.SucursalId, q.Obra, cancellationToken);
        var filas = saldos.Select(s => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> {
            ["factura_id"] = s.Factura.Id, ["proveedor_nombre"] = s.Nombre, ["rfc"] = s.Rfc,
            ["obra"] = s.Factura.Obra ?? "[OBRA POR CONFIRMAR]", ["folio_proveedor"] = s.Factura.FolioProveedor,
            ["fecha_documento"] = s.Factura.FechaDocumento, ["fecha_vencimiento"] = s.Factura.FechaVencimiento,
            ["bucket"] = BucketsAntiguedad.CalcularBucket(s.Factura.FechaVencimiento, corte),
            ["moneda"] = s.Factura.Moneda, ["saldo_pendiente"] = s.Saldo }).ToArray();
        return new("Pasivos por obra", clock.UtcNow, ReporteHistoricoBuilder.Filtros(corte, q.ProveedorId, q.SucursalId, q.Obra, nombreProveedor: (saldos.Count > 0 ? saldos[0].Nombre : null)),
            [ReporteHistoricoBuilder.Texto("proveedor_nombre", "Proveedor"), ReporteHistoricoBuilder.Texto("rfc", "RFC"),
             ReporteHistoricoBuilder.Texto("obra", "Obra"), ReporteHistoricoBuilder.Texto("folio_proveedor", "Folio"),
             ReporteHistoricoBuilder.Texto("moneda", "Moneda"), ReporteHistoricoBuilder.Importe("saldo_pendiente", "Saldo")],
             filas, ReporteHistoricoBuilder.Totales(filas, "saldo_pendiente"));
    }
}
