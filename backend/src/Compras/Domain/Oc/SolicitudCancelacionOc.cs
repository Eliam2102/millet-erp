using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain.Oc;

public sealed class SolicitudCancelacionOc : BaseEntity, IAuditable, IBelongsToAggregate
{
    public Guid OrdenCompraId { get; private set; }
    public Guid AggregateRootId => OrdenCompraId;
    public EstadoOrdenCompra EstadoAnterior { get; private set; }
    public Guid SolicitanteId { get; private set; }
    public DateTimeOffset FechaSolicitud { get; private set; }
    public Guid MotivoCancelacionId { get; private set; }
    public string MotivoSolicitud { get; private set; } = "";
    public Guid? ResolutorId { get; private set; }
    public DateTimeOffset? FechaResolucion { get; private set; }
    public bool? Confirmada { get; private set; }
    public string? MotivoResolucion { get; private set; }

    private SolicitudCancelacionOc() { }

    internal SolicitudCancelacionOc(Guid id, Guid ocId, EstadoOrdenCompra estado,
        Guid usuarioId, DateTimeOffset fecha, Guid motivoId, string motivo) : base(id)
    {
        ValidarFirma(usuarioId, motivo);
        if (motivoId == Guid.Empty)
            throw new BusinessRuleException("OC_CANCELAR_MOTIVO_REQUERIDO", "Selecciona el motivo de cancelación.");
        OrdenCompraId = ocId;
        EstadoAnterior = estado;
        SolicitanteId = usuarioId;
        FechaSolicitud = fecha;
        MotivoCancelacionId = motivoId;
        MotivoSolicitud = motivo.Trim();
    }

    internal void Resolver(Guid usuarioId, DateTimeOffset fecha, bool confirmar, string motivo)
    {
        if (usuarioId == SolicitanteId)
            throw new BusinessRuleException("OC_CANCELACION_MISMA_PERSONA",
                "La segunda firma de cancelación debe ser de una persona distinta a quien la solicitó.");
        ValidarFirma(usuarioId, motivo);
        ResolutorId = usuarioId;
        FechaResolucion = fecha;
        Confirmada = confirmar;
        MotivoResolucion = motivo.Trim();
    }

    private static void ValidarFirma(Guid usuarioId, string motivo)
    {
        if (usuarioId == Guid.Empty || string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500)
            throw new BusinessRuleException("OC_CANCELACION_FIRMA_INVALIDA",
                "La firma requiere un usuario y un motivo de entre 1 y 500 caracteres.");
    }
}
