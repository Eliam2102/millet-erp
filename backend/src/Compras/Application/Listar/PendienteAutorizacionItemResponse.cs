using Millet.Compras.Domain;

namespace Millet.Compras.Application.Listar;

/// <summary>
/// Item de la bandeja de pendientes de autorización (S2). Repite los campos
/// de <see cref="RequisicionListItemResponse"/> (no se deriva porque ese
/// record es sealed y lo usan otras bandejas) y agrega los campos para
/// priorizar la aprobación:
/// <list type="bullet">
///   <item><see cref="Disponibilidad"/>: cuánto de la RQ cubriría el almacén hoy (estimación, igual que el preview de cubrimiento).</item>
///   <item><see cref="EsRecurrente"/>: el requisitante tiene ≥ 3 RQ en 60 días en la misma clasificación.</item>
/// </list>
/// </summary>
public sealed record PendienteAutorizacionItemResponse(
    Guid Id,
    string Folio,
    short FolioAnio,
    EstadoRequisicion Estado,
    Clasificacion Clasificacion,
    Prioridad Prioridad,
    Guid SucursalId,
    Guid DepartamentoId,
    Guid RequisitanteId,
    string? Descripcion,
    DateTimeOffset FechaSolicitud,
    DateOnly? FechaEntregaDeseada,
    string? RequisitanteNombre,
    string? DepartamentoNombre,
    string? DepartamentoClave,
    SituacionSurtido? SituacionSurtido,
    NivelAutorizacion? NivelPendiente,
    OrigenRequisicion Origen,
    DisponibilidadPendiente Disponibilidad,
    bool EsRecurrente);

/// <summary>
/// Disponibilidad de almacén de una RQ pendiente. <c>Total</c>: todas las
/// líneas cubren desde almacén; <c>Parcial</c>: alguna sí; <c>Nula</c>: ninguna;
/// <c>SinDato</c>: la RQ no tiene líneas para evaluar.
/// </summary>
public enum DisponibilidadPendiente
{
    Total = 0,
    Parcial = 1,
    Nula = 2,
    SinDato = 3,
}
