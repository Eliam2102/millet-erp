using MediatR;

namespace Millet.Compras.Domain.Events;

/// <summary>
/// Evento de dominio in-proc emitido cuando una requisición transiciona
/// a <see cref="EstadoRequisicion.Rechazada"/> (terminal). Lo emite
/// <see cref="Requisicion.Rechazar"/>; el handler de Application lo
/// publica vía MediatR antes de <c>SaveChanges</c> para que el mapper
/// de integración (F6-PR3) pueda persistir el evento de integración
/// correspondiente en la misma transacción.
/// </summary>
public sealed record RequisicionRechazadaEvent(
    Guid RequisicionId,
    Guid EmpresaId,
    Guid MotivoId,
    string? MotivoTexto,
    Guid ActorId,
    DateTimeOffset OcurridoEn) : INotification;
