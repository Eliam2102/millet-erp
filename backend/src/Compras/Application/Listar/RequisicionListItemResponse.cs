using Millet.Compras.Domain;

namespace Millet.Compras.Application.Listar;

/// <summary>
/// Item de bandeja: solo campos de cabecera para mostrar en lista.
/// El detalle completo se obtiene vía <c>GET /requisiciones/{id}</c>.
///
/// <para>
/// <see cref="RequisitanteNombre"/>, <see cref="DepartamentoNombre"/> y
/// <see cref="DepartamentoClave"/> los resuelve el backend tras la query
/// (ADR-0042) para que el cliente no tenga que leer los catálogos completos
/// de usuarios/departamentos. Nullables: si la resolución no encuentra la
/// clave, el frontend cae al id.
/// </para>
/// </summary>
public sealed record RequisicionListItemResponse(
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
    string? RequisitanteNombre = null,
    string? DepartamentoNombre = null,
    string? DepartamentoClave = null,
    // ADR-0043: situación calculada dentro de EnSurtido (null en otros estados).
    // La deriva el handler con SituacionSurtidoDerivacion a partir de sumas SQL.
    SituacionSurtido? SituacionSurtido = null,
    // PR-A: nivel de autorización pendiente (N1/N2) dentro de EnAutorizacion;
    // null fuera de ese estado. Lo deriva el handler de pendientes con
    // NivelPendienteDerivacion a partir de 2 EXISTS sobre Autorizaciones.
    NivelAutorizacion? NivelPendiente = null,
    // ADR-0047 PR5.F: origen de la RQ — Manual (default) o Sistema si la creó
    // el motor de reabasto. El FE lo usa para el badge "Sistema" en la bandeja.
    OrigenRequisicion Origen = OrigenRequisicion.Manual);
