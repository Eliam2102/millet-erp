using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.CuentasPorPagar.Application.Inicio;

public sealed record ResumenInicioQuery(Guid? SucursalId = null)
    : IRequest<ResumenInicioResponse>, ISucursalSeleccionadaQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.documentos.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}
public sealed class ResumenInicioHandler(CuentasPorPagarDbContext db)
    : IRequestHandler<ResumenInicioQuery, ResumenInicioResponse>
{
    public async Task<ResumenInicioResponse> Handle(ResumenInicioQuery request, CancellationToken cancellationToken)
    {
        var facturas = db.FacturasProveedor.AsNoTracking()
            .Where(f => request.SucursalesPermitidas == null || (request.SucursalesPermitidas ?? Array.Empty<Guid>()).Contains(f.SucursalId))
            .Where(f => f.Estado == EstadoPasivo.Capturada || f.Estado == EstadoPasivo.EnRevision);
        var total = await facturas.CountAsync(cancellationToken);
        var diferencias = await facturas.CountAsync(f => f.DiferenciaContraOc > 0, cancellationToken);
        return new(total, total, $"{total} facturas sin autorizar · {diferencias} con diferencia", diferencias);
    }
}

public sealed class ResumenInicioValidator : FluentValidation.AbstractValidator<ResumenInicioQuery>
{
    public ResumenInicioValidator()
    {
        RuleFor(request => request.SucursalId).NotEqual(Guid.Empty).When(request => request.SucursalId.HasValue);
    }
}
