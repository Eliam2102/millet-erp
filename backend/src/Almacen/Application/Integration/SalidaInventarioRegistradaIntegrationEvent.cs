using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.salida_inventario.registrada.v1</c> (86e3yxpb3, Contraste G-1). Una salida de inventario que
/// no pasa por requisición y que hoy no avisaba: salidas de la app de Almacenes (consumo, rotura, salida manual),
/// componentes descontados por composición y baja por daño del dictamen de calidad. Una por movimiento.
/// Consumidor previsto: <b>Contabilidad</b> (póliza de salida a costo, plantilla «Contraste · U-2»).
/// <c>Causa</c> es el motivo contable: consumo, consumo_composicion, rotura, daño o salida_manual.
/// <c>CentroCostoId</c> va null mientras ni la app ni el dictamen lo capturen (aditivo: se llenará sin bump de versión).
/// </summary>
public sealed record SalidaInventarioRegistradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid MovimientoId,
    string FolioMovimiento,
    DateOnly FechaMovimiento,
    string Causa,
    string? Motivo,
    Guid? AlmacenId,
    Guid? SucursalId,
    Guid? CentroCostoId,
    decimal MontoTotalMxn,
    IReadOnlyList<LineaSalidaInventarioPayload> Lineas)
    : IntegrationEvent("almacen.salida_inventario.registrada.v1", EmpresaId, OcurridoEn)
{
    public static string CausaPorDefecto(TipoMovimiento tipo) => tipo switch
    {
        TipoMovimiento.BajaPorDano => "rotura",
        TipoMovimiento.AjusteNegativo => "salida_manual",
        _ => "consumo",
    };

    /// <summary>Encola el evento del movimiento recién agregado (aún sin guardar); lo drena el mismo <c>SaveChanges</c> que el movimiento.</summary>
    internal static async Task PublicarAsync(IIntegrationEventPublisher events, AlmacenDbContext db, Guid empresaId,
        Guid movimientoId, Guid subAlmacenId, string causa, CancellationToken ct)
    {
        var mov = db.Movimientos.Local.Single(m => m.Id == movimientoId);
        var (almacenId, sucursalId) = await AlmacenSucursalResolver.ResolverAsync(db, subAlmacenId, ct);
        var lineas = mov.Lineas.Select(l => new LineaSalidaInventarioPayload(
            l.Id, l.ArticuloId, l.UnidadMedida, l.Cantidad, l.CostoUnitarioMxn, l.MontoTotalMxn)).ToList();
        await events.PublishAsync(new SalidaInventarioRegistradaIntegrationEvent(
            empresaId, DateTimeOffset.UtcNow, mov.Id, mov.Folio!, mov.FechaMovimiento, causa, mov.Motivo,
            almacenId, sucursalId, null, lineas.Sum(l => l.MontoMxn), lineas), ct);
    }
}

public sealed record LineaSalidaInventarioPayload(
    Guid LineaId, Guid ArticuloId, string UnidadMedida, decimal Cantidad, decimal CostoUnitarioMxn, decimal MontoMxn);
