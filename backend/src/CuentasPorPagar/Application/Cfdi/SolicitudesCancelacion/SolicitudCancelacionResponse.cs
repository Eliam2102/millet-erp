using Millet.CuentasPorPagar.Domain.Cfdi;

namespace Millet.CuentasPorPagar.Application.Cfdi.SolicitudesCancelacion;

public sealed record SolicitudCancelacionResponse(
    Guid Id,
    Guid CfdiRecibidoId,
    AreaSolicitudCancelacion Area,
    EstadoSolicitudCancelacion Estado,
    DateTimeOffset FechaRecepcion,
    DateTimeOffset PlazoSatVence,
    Guid? ResponsableId,
    string? Notas,
    DateTimeOffset? FechaDecision,
    Guid? DecididoPor,
    string? ComentarioDecision,
    string? EvidenciaNombre,
    string? ResponsableNombre = null,
    string? DecididoPorNombre = null)
{
    public static SolicitudCancelacionResponse De(SolicitudCancelacionCfdi s) => new(
        s.Id, s.CfdiRecibidoId, s.Area, s.Estado, s.FechaRecepcion, s.PlazoSatVence, s.ResponsableId, s.Notas,
        s.FechaDecision, s.DecididoPor, s.ComentarioDecision, s.EvidenciaNombre);
}
