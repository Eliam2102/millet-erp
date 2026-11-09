using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Movimientos;

public enum EstadoReclasificacion : short
{
    Pendiente = 1,
    Aprobada = 2,
    Rechazada = 3,
}

/// <summary>
/// Solicitud de reclasificar el concepto de un movimiento (TES-09/10 paso R, M-204). Mientras está
/// pendiente el movimiento conserva su concepto; al aprobarse queda como rastro: antes, después,
/// quién pidió, quién firmó, fecha y motivo. Nada se borra.
/// </summary>
public sealed class MovimientoReclasificacion : BaseEntity, IAuditable
{
    public Guid MovimientoId { get; private set; }
    public Guid? ConceptoAnteriorId { get; private set; }
    public Guid ConceptoNuevoId { get; private set; }
    public string Motivo { get; private set; } = default!;
    public Guid SolicitadoPor { get; private set; }
    public DateTimeOffset SolicitadoEn { get; private set; }
    public EstadoReclasificacion Estado { get; private set; }
    public Guid? ResueltoPor { get; private set; }
    public DateTimeOffset? ResueltoEn { get; private set; }
    public string? MotivoResolucion { get; private set; }

    private MovimientoReclasificacion() { }

    public static MovimientoReclasificacion Solicitar(
        Guid movimientoId, Guid? conceptoActualId, Guid conceptoNuevoId, string motivo, Guid usuarioId, DateTimeOffset ahora)
    {
        if (conceptoNuevoId == Guid.Empty || string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("MOV_RECLASIFICACION_INVALIDA", "Selecciona el concepto e indica el motivo de la reclasificación.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("MOV_USUARIO_VACIO", "El usuario que solicita es obligatorio.");
        if (conceptoActualId == conceptoNuevoId)
            throw new BusinessRuleException("MOV_RECLASIFICACION_MISMO_CONCEPTO", "El movimiento ya tiene ese concepto.");
        return new MovimientoReclasificacion(Guid.CreateVersion7())
        {
            MovimientoId = movimientoId, ConceptoAnteriorId = conceptoActualId, ConceptoNuevoId = conceptoNuevoId,
            Motivo = motivo.Trim(), SolicitadoPor = usuarioId, SolicitadoEn = ahora, Estado = EstadoReclasificacion.Pendiente,
        };
    }

    private MovimientoReclasificacion(Guid id) : base(id) { }

    public void Aprobar(Guid usuarioId, DateTimeOffset ahora) => Resolver(EstadoReclasificacion.Aprobada, usuarioId, null, ahora);

    public void Rechazar(Guid usuarioId, string motivo, DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("MOV_RECLASIFICACION_MOTIVO_RECHAZO", "Indica el motivo del rechazo.");
        Resolver(EstadoReclasificacion.Rechazada, usuarioId, motivo.Trim(), ahora);
    }

    private void Resolver(EstadoReclasificacion estado, Guid usuarioId, string? motivo, DateTimeOffset ahora)
    {
        if (Estado != EstadoReclasificacion.Pendiente)
            throw new BusinessRuleException("MOV_RECLASIFICACION_YA_RESUELTA", "La reclasificación ya fue resuelta.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("MOV_USUARIO_VACIO", "El usuario que firma es obligatorio.");
        if (usuarioId == SolicitadoPor)
            throw new BusinessRuleException("MOV_RECLASIFICACION_MISMO_USUARIO", "Quien reclasifica no puede autorizar.");
        Estado = estado;
        ResueltoPor = usuarioId;
        ResueltoEn = ahora;
        MotivoResolucion = motivo;
    }
}
