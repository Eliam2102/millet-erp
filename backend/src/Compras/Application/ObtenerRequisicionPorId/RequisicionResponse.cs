using Millet.Compras.Domain;

namespace Millet.Compras.Application.ObtenerRequisicionPorId;

/// <summary>
/// DTO con cabecera + líneas + autorizaciones de <see cref="Requisicion"/>.
/// El campo <see cref="Version"/> se expone también vía header
/// <c>ETag</c> (cuidado §2.4 [P1]) para que el cliente envíe
/// <c>If-Match</c> en mutaciones futuras.
///
/// <para>
/// <see cref="Lineas"/> y <see cref="Autorizaciones"/> agregados en B.0
/// para que el frontend pinte <c>&lt;CubrimientoBar&gt;</c> y la timeline
/// de firmas sin queries adicionales. Siempre poblados (al menos lista
/// vacía).
/// </para>
/// </summary>
public sealed record RequisicionResponse(
    Guid Id,
    Guid EmpresaId,
    string Folio,
    short FolioAnio,
    Clasificacion Clasificacion,
    Guid SucursalId,
    Guid DepartamentoId,
    // Almacén-por-línea PR3: nullable. RQ manual = null (el requisitante ya no
    // captura almacén); solo el reorden (RQ Sistema) lo pobla.
    Guid? AlmacenDestinoId,
    Guid RequisitanteId,
    Guid CreadorId,
    // Nombres resueltos en backend (ADR-0042). Nullables → el FE cae al id
    // si la resolución no encuentra la clave. Mapster los deja en null al
    // mapear el agregado; el handler los puebla tras el map.
    string? RequisitanteNombre,
    string? DepartamentoNombre,
    string? DepartamentoClave,
    string? Descripcion,
    Prioridad Prioridad,
    DateTimeOffset FechaSolicitud,
    DateOnly? FechaEntregaDeseada,
    Guid? ProveedorSugeridoId,
    // Etiqueta del proveedor sugerido resuelta en backend (ADR-0042 addendum).
    // Nullable → el FE cae al id. Mapster las deja en null; el handler las
    // puebla tras el map (batch IProveedorReadPort).
    string? ProveedorSugeridoRazonSocial,
    string? ProveedorSugeridoClave,
    EstadoRequisicion Estado,
    Guid? MotivoTerminacionId,
    string? MotivoTerminacionTexto,
    Guid? ActorTerminacionId,
    DateTimeOffset? FechaTerminacion,
    Guid? ComprometidaEnOcId,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<LineaResponse> Lineas,
    IReadOnlyList<AutorizacionResponse> Autorizaciones,
    // ADR-0043: situación calculada dentro de EnSurtido (null en otros estados).
    // Mapster la deja null al mapear; el handler la setea post-map (igual que los
    // nombres) con la MISMA función que la bandeja (fuente única, sin drift).
    SituacionSurtido? SituacionSurtido = null,
    // ADR-0047 PR5.F: origen de la RQ — Manual (default) o Sistema si la creó
    // el motor de reabasto. Mapster lo mapea por convención desde Requisicion.Origen;
    // el FE condiciona el aviso del diálogo de eliminar a Origen == Sistema.
    OrigenRequisicion Origen = OrigenRequisicion.Manual);
