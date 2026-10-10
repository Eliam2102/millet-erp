using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.Compras.Infrastructure;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Compras.Application.ObtenerRequisicionPorId;
public sealed record ObtenerCentroCostoCapturaQuery(Guid RequisicionId) : IRequest<CentroCostoCaptura>;
public sealed class ObtenerCentroCostoCapturaValidator : AbstractValidator<ObtenerCentroCostoCapturaQuery>
{
    public ObtenerCentroCostoCapturaValidator() => RuleFor(x => x.RequisicionId).NotEmpty();
}
public sealed class ObtenerCentroCostoCapturaHandler(ComprasDbContext db, CompartidoDbContext organizacion, ICentroCostoCapturaPort captura)
    : IRequestHandler<ObtenerCentroCostoCapturaQuery, CentroCostoCaptura>
{
    public async Task<CentroCostoCaptura> Handle(ObtenerCentroCostoCapturaQuery request, CancellationToken cancellationToken)
    {
        var rq = await db.Requisiciones.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException("REQUISICION_NO_ENCONTRADA", "No se encontró la requisición.");
        return await captura.ObtenerAsync(rq.SucursalId, await CentroCostoRqResolver.DepartamentoAsync(rq, organizacion, cancellationToken), cancellationToken);
    }
}
