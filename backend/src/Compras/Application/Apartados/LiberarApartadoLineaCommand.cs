using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Oc;
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
/// unidad de documento de la línea. Recalcula el cubrimiento: con
/// <c>MandarACompra</c> lo liberado pasa a saldo de compra; sin él deja de
/// pedirse y, si ya no falta nada por entregar, la RQ se cierra.
/// </summary>
public sealed record LiberarApartadoLineaCommand(
    Guid RequisicionId, Guid LineaId, decimal Cantidad, string Motivo, bool? MandarACompra) : IRequest;

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
        RuleFor(c => c.MandarACompra).NotNull().WithErrorCode("MANDAR_A_COMPRA_REQUERIDO")
            .WithMessage("Indica si lo liberado se manda a compra.");
    }
}

public sealed class LiberarApartadoLineaHandler(
    ComprasDbContext db,
    ICurrentUserContext currentUser,
    TransaccionApartadosRq apartadosTx,
    IConversionUnidadPort conversion,
    IPublisher publisher,
    IClock clock) : IRequestHandler<LiberarApartadoLineaCommand>
{
    public async Task Handle(LiberarApartadoLineaCommand command, CancellationToken cancellationToken)
    {
        using var _ = LogContext.PushProperty("RequisicionId", command.RequisicionId);
        using var __ = LogContext.PushProperty("LineaId", command.LineaId);

        if (currentUser.UserId is null)
            throw new UnauthorizedAccessException("Sin usuario autenticado.");

        // Mismo lock que el consumo de saldo de OC: lo que se manda a compra
        // no compite con una OC que se esté creando sobre esta RQ.
        await using var tx = await SaldoCompraRq.BloquearAsync(db, cancellationToken);

        var requisicion = await db.Requisiciones.Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}' en la empresa actual.");

        var linea = requisicion.Lineas.FirstOrDefault(l => l.Id == command.LineaId)
            ?? throw new EntityNotFoundException(
                "LINEA_NO_ENCONTRADA",
                $"La línea '{command.LineaId}' no pertenece a la requisición '{command.RequisicionId}'.");

        var motivo = command.Motivo.Trim();
        var cierre = requisicion.LiberarApartado(
            linea.Id, command.Cantidad, motivo, command.MandarACompra!.Value, clock.UtcNow);

        var c = await conversion.ConvertirAsync(linea.ArticuloId, command.Cantidad, null, linea.UnidadMedida, cancellationToken);
        var cantidadBase = command.Cantidad * c.FactorDocumentoABase;

        await using var union = await apartadosTx.UnirAsync(cancellationToken);
        await apartadosTx.Apartados.BloquearAsync(requisicion.SucursalId, requisicion.Lineas.Select(l => l.ArticuloId), cancellationToken);
        await apartadosTx.Apartados.LiberarLineaAsync(requisicion.Id, linea.Id, cantidadBase, motivo, cancellationToken);
        if (cierre is not null)
        {
            // Mismo patrón que la entrega: el cierre suelta lo que quede apartado
            // y su outbox se escribe en esta misma TX.
            await apartadosTx.Apartados.LiberarAsync(requisicion.Id, cancellationToken);
            await publisher.Publish(cierre, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
    }
}
