using MediatR;

namespace Millet.Compras.Domain.Events;

/// <summary>
/// Evento de dominio in-proc emitido cuando una requisición transiciona
/// a <see cref="EstadoRequisicion.Eliminada"/> (terminal pre-autorización).
/// Lo emite <see cref="Requisicion.Eliminar"/>.
/// </summary>
public sealed record RequisicionEliminadaEvent(
    Guid RequisicionId,
    Guid EmpresaId,
    Guid MotivoId,
    string? MotivoTexto,
    Guid ActorId,
    DateTimeOffset OcurridoEn) : INotification;
