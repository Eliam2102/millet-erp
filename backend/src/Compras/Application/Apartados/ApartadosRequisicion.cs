using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Compras.Application.Apartados;

public sealed record LiberarApartadoCommand(Guid RequisicionId, Guid LineaId, decimal Cantidad, string Motivo) : IRequest;

public sealed class LiberarApartadoValidator : AbstractValidator<LiberarApartadoCommand>
{
    public LiberarApartadoValidator()
    {
        RuleFor(x => x.Cantidad).GreaterThan(0).PrecisionScale(14, 4, true);
        RuleFor(x => x.Motivo).NotEmpty().MaximumLength(500);
    }
}

public sealed class LiberarApartadoHandler(ComprasDbContext db, TransaccionApartadosRq apartados,
    ICurrentUserContext usuario, IClock clock, IConversionUnidadPort conversion) : IRequestHandler<LiberarApartadoCommand>
{
    public async Task Handle(LiberarApartadoCommand command, CancellationToken cancellationToken)
    {
        var actor = usuario.UserId ?? throw new UnauthorizedAccessException("Sin usuario autenticado.");
        var rq = await db.Requisiciones.Include(r => r.Lineas).SingleOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException("REQUISICION_NO_ENCONTRADA", "No se encontró la requisición.");
        var linea = rq.Lineas.SingleOrDefault(l => l.Id == command.LineaId)
            ?? throw new EntityNotFoundException("LINEA_NO_ENCONTRADA", "No se encontró la partida en esta requisición.");
        var unidades = await conversion.ConvertirAsync(linea.ArticuloId, command.Cantidad, null, linea.UnidadMedida, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await using var union = await apartados.UnirAsync(cancellationToken);
        await apartados.Apartados.BloquearAsync(rq.SucursalId, [linea.ArticuloId], cancellationToken);
        await db.Entry(rq).ReloadAsync(cancellationToken);
        if (rq.Estado is not (EstadoRequisicion.Autorizada or EstadoRequisicion.EnSurtido))
            throw new BusinessRuleException("RQ_LIBERACION_ESTADO_INVALIDO", "Solo puedes liberar apartados de una requisición autorizada o en surtido.");
        await apartados.Apartados.LiberarLineaAsync(rq.Id, linea.Id, unidades.CantidadBase, command.Motivo, actor, clock.UtcNow, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}

public sealed record ConsultarApartadosQuery(Guid RequisicionId) : IRequest<IReadOnlyList<ApartadoResponse>>;
public sealed record ApartadoResponse(Guid LineaId, decimal Pendiente, string UnidadMedida);

public sealed class ConsultarApartadosHandler(ComprasDbContext db, TransaccionApartadosRq apartados,
    IConversionUnidadPort conversion) : IRequestHandler<ConsultarApartadosQuery, IReadOnlyList<ApartadoResponse>>
{
    public async Task<IReadOnlyList<ApartadoResponse>> Handle(ConsultarApartadosQuery query, CancellationToken cancellationToken)
    {
        var rq = await db.Requisiciones.AsNoTracking().Include(r => r.Lineas).SingleOrDefaultAsync(r => r.Id == query.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException("REQUISICION_NO_ENCONTRADA", "No se encontró la requisición.");
        var pendientes = await apartados.Apartados.ConsultarAsync(rq.Id, cancellationToken);
        var resultado = new List<ApartadoResponse>();
        foreach (var linea in rq.Lineas.Where(l => pendientes.ContainsKey(l.Id)))
        {
            var unidades = await conversion.ConvertirAsync(linea.ArticuloId, linea.Cantidad, null, linea.UnidadMedida, cancellationToken);
            resultado.Add(new(linea.Id, pendientes[linea.Id] / unidades.FactorDocumentoABase, linea.UnidadMedida));
        }
        return resultado;
    }
}
