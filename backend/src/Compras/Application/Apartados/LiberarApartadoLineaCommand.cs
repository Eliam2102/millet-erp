using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Serilog.Context;

namespace Millet.Compras.Application.Apartados;

/// <summary>
/// Libera con motivo parte (o todo) de la existencia apartada de una línea de
/// requisición autorizada para que otra la use (86e3tferc). Cantidad en la
/// unidad de documento de la línea. No emite evento ni recalcula el cubrimiento (D3, D5).
/// </summary>
public sealed record LiberarApartadoLineaCommand(Guid RequisicionId, Guid LineaId, decimal Cantidad, string Motivo) : IRequest;

public sealed class LiberarApartadoLineaValidator : AbstractValidator<LiberarApartadoLineaCommand>
{
    public LiberarApartadoLineaValidator()
    {
        RuleFor(c => c.RequisicionId).NotEmpty().WithErrorCode("REQUISICION_REQUERIDA");
        RuleFor(c => c.LineaId).NotEmpty().WithErrorCode("LINEA_REQUERIDA");
        RuleFor(c => c.Cantidad).GreaterThan(0).WithErrorCode("CANTIDAD_INVALIDA")
            .Must(c => decimal.Round(c, 4) == c).WithErrorCode("CANTIDAD_DECIMALES_INVALIDOS");
        RuleFor(c => c.Motivo).NotEmpty().WithErrorCode("MOTIVO_REQUERIDO")
            .Must(m => !string.IsNullOrWhiteSpace(m)).WithErrorCode("MOTIVO_REQUERIDO")
            .MaximumLength(500).WithErrorCode("MOTIVO_DEMASIADO_LARGO");
    }
}

public sealed class LiberarApartadoLineaHandler(
    ComprasDbContext db,
    ICurrentUserContext currentUser,
    TransaccionApartadosRq apartadosTx,
    IConversionUnidadPort conversion) : IRequestHandler<LiberarApartadoLineaCommand>
{
    public async Task Handle(LiberarApartadoLineaCommand command, CancellationToken cancellationToken)
    {
        using var _ = LogContext.PushProperty("RequisicionId", command.RequisicionId);
        using var __ = LogContext.PushProperty("LineaId", command.LineaId);

        if (currentUser.UserId is null)
            throw new UnauthorizedAccessException("Sin usuario autenticado.");

        var requisicion = await db.Requisiciones.Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}' en la empresa actual.");

        var linea = requisicion.Lineas.FirstOrDefault(l => l.Id == command.LineaId)
            ?? throw new EntityNotFoundException(
                "LINEA_NO_ENCONTRADA",
                $"La línea '{command.LineaId}' no pertenece a la requisición '{command.RequisicionId}'.");

        if (requisicion.Estado is not (EstadoRequisicion.Autorizada or EstadoRequisicion.EnSurtido))
            throw new BusinessRuleException(
                "RQ_SIN_APARTADO_LIBERABLE",
                "Sólo se libera existencia apartada de requisiciones autorizadas o en surtido.");

        var c = await conversion.ConvertirAsync(linea.ArticuloId, command.Cantidad, null, linea.UnidadMedida, cancellationToken);
        var cantidadBase = command.Cantidad * c.FactorDocumentoABase;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await using var union = await apartadosTx.UnirAsync(cancellationToken);
        await apartadosTx.Apartados.BloquearAsync(requisicion.SucursalId, [linea.ArticuloId], cancellationToken);
        await apartadosTx.Apartados.LiberarLineaAsync(requisicion.Id, linea.Id, cantidadBase, command.Motivo.Trim(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
