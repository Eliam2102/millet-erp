using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Recepciones;

/// <summary>
/// Anula las líneas de activos fijos de una recepción (espejo del 102 de SAP). Solo mientras todos
/// los activos creados siguen «por capitalizar» y sin factura conciliada; después la salida es la
/// baja en Activos Fijos. La fila se conserva (AnuladaEn) y el evento
/// <c>almacen.recepcion_activo.anulada.v1</c> da de baja los activos y baja lo recibido en la OC.
/// Idempotente: anular de nuevo una recepción ya anulada no hace nada.
/// </summary>
public sealed record AnularRecepcionActivoCommand(Guid OrdenCompraId, Guid RecepcionId, string Motivo) : IRequest<AnularRecepcionActivoResponse>;

public sealed record AnularRecepcionActivoResponse(Guid RecepcionId, string? Folio, int LineasAnuladas, bool YaAnulada);

public sealed class AnularRecepcionActivoValidator : AbstractValidator<AnularRecepcionActivoCommand>
{
    public AnularRecepcionActivoValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEqual(Guid.Empty);
        RuleFor(c => c.RecepcionId).NotEqual(Guid.Empty);
        RuleFor(c => c.Motivo).NotEmpty().WithMessage("Indica el motivo de la anulación.")
            .MaximumLength(500).WithMessage("El motivo no puede exceder 500 caracteres.");
    }
}

public sealed class AnularRecepcionActivoHandler(
    AlmacenDbContext db, IActivoRecepcionReadPort activos, IIntegrationEventPublisher events, ICurrentUserContext user)
    : IRequestHandler<AnularRecepcionActivoCommand, AnularRecepcionActivoResponse>
{
    public async Task<AnularRecepcionActivoResponse> Handle(AnularRecepcionActivoCommand request, CancellationToken cancellationToken)
    {
        // Mismo lock por OC que el registro y las devoluciones: serializa contra recepciones concurrentes de la línea.
        await using var transaccion = await DevolucionesProveedor.DevolucionOrigenLock.AbrirAsync(db, request.OrdenCompraId, cancellationToken);
        var filas = await db.RecepcionesActivos
            .Where(x => x.RecepcionId == request.RecepcionId && x.OrdenCompraId == request.OrdenCompraId).ToListAsync(cancellationToken);
        if (filas.Count == 0)
            throw new EntityNotFoundException("RECEPCION_ACTIVO_NO_ENCONTRADA", "La orden de compra no tiene una recepción de activos con ese identificador.");
        var folio = filas[0].Folio;
        var pendientes = filas.Where(x => !x.EstaAnulada).ToList();
        if (pendientes.Count == 0)
            return new AnularRecepcionActivoResponse(request.RecepcionId, folio, 0, true);

        var anulaPor = user.UserId ?? throw new BusinessRuleException("RECEPCION_SIN_USUARIO", "Se requiere usuario autenticado.");
        foreach (var fila in pendientes)
        {
            var estado = await activos.ConsultarAsync(fila.EmpresaId, fila.RecepcionId, fila.LineaOcId, cancellationToken);
            if (estado.Capitalizados > 0)
                throw new BusinessRuleException("RECEPCION_ACTIVO_CAPITALIZADO",
                    "Los activos de esta recepción ya se capitalizaron; no se puede anular. Da de baja el activo en Activos Fijos.");
            if (estado.Facturados > 0)
                throw new BusinessRuleException("RECEPCION_ACTIVO_FACTURADO",
                    "Los activos de esta recepción ya están conciliados con una factura de proveedor; cancela la factura antes de anular la recepción.");
        }

        var ahora = DateTimeOffset.UtcNow;
        var motivo = request.Motivo.Trim();
        var anuladas = pendientes.Select(x => x.Id).ToList();
        foreach (var fila in pendientes)
        {
            fila.Anular(anulaPor, motivo, ahora);
            // Recibido vigente de la línea sin las filas que se anulan en esta operación (aún no guardadas).
            var acumulado = await db.RecepcionesActivos.AsNoTracking()
                .Where(x => x.OrdenCompraId == fila.OrdenCompraId && x.LineaOcId == fila.LineaOcId && x.AnuladaEn == null && !anuladas.Contains(x.Id))
                .SumAsync(x => x.Cantidad, cancellationToken);
            await events.PublishAsync(new RecepcionActivoAnuladaIntegrationEvent(fila.EmpresaId, ahora, fila.Id, fila.RecepcionId,
                fila.Folio, fila.OrdenCompraId, fila.LineaOcId, fila.Cantidad, acumulado, motivo), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        if (transaccion is not null) await transaccion.CommitAsync(cancellationToken);
        return new AnularRecepcionActivoResponse(request.RecepcionId, folio, pendientes.Count, false);
    }
}

public sealed record ListarRecepcionesActivoQuery(Guid OrdenCompraId) : IRequest<IReadOnlyList<RecepcionActivoDto>>;

public sealed record RecepcionActivoDto(
    Guid Id, Guid RecepcionId, string? Folio, Guid LineaOcId, decimal Cantidad, DateTimeOffset RecibidaEn,
    bool Anulada, DateTimeOffset? AnuladaEn, string? MotivoAnulacion);

public sealed class ListarRecepcionesActivoHandler(AlmacenDbContext db)
    : IRequestHandler<ListarRecepcionesActivoQuery, IReadOnlyList<RecepcionActivoDto>>
{
    public async Task<IReadOnlyList<RecepcionActivoDto>> Handle(ListarRecepcionesActivoQuery request, CancellationToken cancellationToken)
        => await db.RecepcionesActivos.AsNoTracking().Where(x => x.OrdenCompraId == request.OrdenCompraId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new RecepcionActivoDto(x.Id, x.RecepcionId, x.Folio, x.LineaOcId, x.Cantidad, x.CreatedAt,
                x.AnuladaEn != null, x.AnuladaEn, x.MotivoAnulacion)).ToListAsync(cancellationToken);
}
