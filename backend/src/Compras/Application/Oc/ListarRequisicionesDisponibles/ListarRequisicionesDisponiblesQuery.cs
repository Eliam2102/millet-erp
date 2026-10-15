using MediatR;

namespace Millet.Compras.Application.Oc.ListarRequisicionesDisponibles;

/// <summary>
/// Lista RQs en <c>EnSurtido</c>, no comprometidas en OC y con al menos una
/// línea de saldo de compra, de una sucursal; sirve al selector del módulo
/// OC para construir una OC con el flujo §4.2 del mapa funcional
/// (consolidación N:1).
///
/// La restricción de sucursal única (§10.5 cerrado del mapa funcional)
/// se aplica aquí: una OC solo puede consolidar RQs de la misma
/// sucursal destino. Por eso el filtro <see cref="SucursalId"/> es
/// obligatorio.
/// </summary>
public sealed record ListarRequisicionesDisponiblesQuery(
    Guid SucursalId) : IRequest<IReadOnlyList<RequisicionDisponibleResponse>>, Millet.SharedKernel.Application.ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "compras.requisiciones.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

/// <summary>
/// DTO con el shape mínimo que el FE necesita para pintar el selector
/// (folio, fechas, depto, requisitante, totalLineas) sin tener que cargar
/// la RQ completa. <see cref="FaltantePorComprar"/> y
/// <see cref="LineasConSaldo"/> son opcionales al final para no romper
/// consumidores existentes; el handler siempre los puebla.
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
    Guid? ProveedorSugeridoId,
    /// <summary>Suma de <see cref="LineaDisponibleResponse.CantidadSaldo"/> de la RQ.</summary>
    decimal FaltantePorComprar = 0m,
    /// <summary>Solo las líneas de la RQ con saldo de compra &gt; 0.</summary>
    IReadOnlyList<LineaDisponibleResponse>? LineasConSaldo = null);

/// <summary>
/// Línea de una RQ con saldo pendiente de compra (<see cref="SaldoCompraRq"/>).
/// </summary>
public sealed record LineaDisponibleResponse(
    Guid LineaId,
    Guid ArticuloId,
    string? ArticuloClave,
    string? ArticuloNombre,
    decimal CantidadSaldo,
    decimal CantidadDeAlmacen);
