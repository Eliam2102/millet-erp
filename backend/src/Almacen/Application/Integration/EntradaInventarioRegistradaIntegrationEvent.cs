using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.entrada_inventario.registrada.v1</c> (Fernando Alejos, 10-nov-2026). Entrada manual de la app de Almacenes
/// (ENTRADA_MANUAL: sobrante, sin compra, devolución de venta, regreso de préstamo) que antes no avisaba a Contabilidad. Una por
/// movimiento, al costo promedio del ERP. Solo se publica con una <see cref="ClavesContables"/> conocida: sin clave (o con otra, como
/// CARGA_INICIAL) el movimiento sigue sin póliza, igual que antes. Consumidor: <b>Contabilidad</b> (cargo inventario · abono por motivo).
/// Reusa <see cref="LineaSalidaInventarioPayload"/> como línea.
/// </summary>
public sealed record EntradaInventarioRegistradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid MovimientoId,
    string FolioMovimiento,
    DateOnly FechaMovimiento,
    string MotivoClave,
    string? Motivo,
    Guid? AlmacenId,
    Guid? SucursalId,
    decimal MontoTotalMxn,
    IReadOnlyList<LineaSalidaInventarioPayload> Lineas)
    : IntegrationEvent("almacen.entrada_inventario.registrada.v1", EmpresaId, OcurridoEn)
{
    /// <summary>Motivos de entrada manual que tienen póliza; el resto de entradas manuales no publica.</summary>
    public static readonly IReadOnlySet<string> ClavesContables = new HashSet<string>(StringComparer.Ordinal)
    {
        "SOBRANTE", "SIN_COMPRA", "DEVOLUCION_VENTA", "REGRESO_PRESTAMO",
    };

    /// <summary>Encola el evento del movimiento recién agregado (aún sin guardar); lo drena el mismo <c>SaveChanges</c> que el movimiento.</summary>
    internal static async Task PublicarAsync(IIntegrationEventPublisher events, AlmacenDbContext db, Guid empresaId,
        Guid movimientoId, Guid subAlmacenId, CancellationToken ct)
    {
        var mov = db.Movimientos.Local.Single(m => m.Id == movimientoId);
        if (mov.MotivoClave is not { } clave || !ClavesContables.Contains(clave)) return;
        var (almacenId, sucursalId) = await AlmacenSucursalResolver.ResolverAsync(db, subAlmacenId, ct);
        var lineas = mov.Lineas.Select(l => new LineaSalidaInventarioPayload(
            l.Id, l.ArticuloId, l.UnidadMedida, l.Cantidad, l.CostoUnitarioMxn, l.MontoTotalMxn, l.CentroCostoId)).ToList();
        await events.PublishAsync(new EntradaInventarioRegistradaIntegrationEvent(
            empresaId, DateTimeOffset.UtcNow, mov.Id, mov.Folio!, mov.FechaMovimiento, clave, mov.Motivo,
            almacenId, sucursalId, lineas.Sum(l => l.MontoMxn), lineas), ct);
    }
}
