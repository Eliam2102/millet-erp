using MediatR;

namespace Millet.Compras.Application.Oc.ListarRequisicionesDisponibles;

/// <summary>
/// Lista RQs Autorizadas y no comprometidas de una sucursal, filtradas
/// para que el selector del módulo OC pueda construir una nueva OC con
/// el flujo §4.2 del mapa funcional (consolidación N:1).
///
/// La restricción de sucursal única (§10.5 cerrado del mapa funcional)
/// se aplica aquí: una OC solo puede consolidar RQs de la misma
/// sucursal destino. Por eso el filtro <see cref="SucursalId"/> es
/// obligatorio.
/// </summary>
public sealed record ListarRequisicionesDisponiblesQuery(
    Guid SucursalId) : IRequest<IReadOnlyList<RequisicionDisponibleResponse>>;

/// <summary>
/// DTO con el shape mínimo que el FE necesita para pintar el selector
/// (folio, fechas, depto, requisitante, totalLineas) sin tener que cargar
/// la RQ completa.
/// </summary>
public sealed record RequisicionDisponibleResponse(
    Guid Id,
    string Folio,
    short FolioAnio,
    Guid DepartamentoId,
    Guid RequisitanteId,
    DateTimeOffset FechaSolicitud,
    DateOnly? FechaEntregaDeseada,
    int TotalLineas,
    Guid? ProveedorSugeridoId);
