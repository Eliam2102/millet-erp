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
/// <c>Causa</c> es el motivo contable: consumo, consumo_composicion, rotura, daño, salida_manual o salida_no_planificada.
/// <c>CentroCostoId</c> (cabecera) y el de cada línea llevan el centro (equipo/máquina) que la línea del movimiento ya trae; null si no lo trae
/// (hoy ni el mensaje de la app ni el dictamen lo capturan). Ubicación (sucursal → 101-105) y área no se inventan: faltan las reglas de Millet.
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
    IReadOnlyList<LineaSalidaInventarioPayload> Lineas,
    // Campo aditivo opcional (sin bump de versión): clave fija del motivo de la salida manual de la app (FALTANTE, MUESTRA, PRESTAMO,
    // CONSUMO_INTERNO…). Contabilidad la usa para elegir la cuenta (Fernando Alejos, 10-nov-2026); null = cuenta por causa, como antes.
    string? MotivoClave = null)
    : IntegrationEvent("almacen.salida_inventario.registrada.v1", EmpresaId, OcurridoEn)
{
    public static string CausaPorDefecto(TipoMovimiento tipo) => tipo switch
    {
        TipoMovimiento.BajaPorDano => "rotura",
        TipoMovimiento.AjusteNegativo => "salida_manual",
        TipoMovimiento.SalidaNoPlanificada => "salida_no_planificada",
        // v10-v11: la venta por albarán carga costo de ventas; ningún otro punto del flujo de A+W descarga la materia prima.
        TipoMovimiento.SalidaPorVenta => "venta",
        _ => "consumo",
    };

    /// <summary>Encola el evento del movimiento recién agregado (aún sin guardar); lo drena el mismo <c>SaveChanges</c> que el movimiento.</summary>
    internal static async Task PublicarAsync(IIntegrationEventPublisher events, AlmacenDbContext db, Guid empresaId,
        Guid movimientoId, Guid subAlmacenId, string causa, CancellationToken ct)
    {
        var mov = db.Movimientos.Local.Single(m => m.Id == movimientoId);
        var (almacenId, sucursalId) = await AlmacenSucursalResolver.ResolverAsync(db, subAlmacenId, ct);
        var lineas = mov.Lineas.Select(l => new LineaSalidaInventarioPayload(
            l.Id, l.ArticuloId, l.UnidadMedida, l.Cantidad, l.CostoUnitarioMxn, l.MontoTotalMxn, l.CentroCostoId)).ToList();
        var centros = lineas.Select(l => l.CentroCostoId).Distinct().ToList();
        await events.PublishAsync(new SalidaInventarioRegistradaIntegrationEvent(
            empresaId, DateTimeOffset.UtcNow, mov.Id, mov.Folio!, mov.FechaMovimiento, causa, mov.Motivo,
            almacenId, sucursalId, centros is [{ } unico] ? unico : null, lineas.Sum(l => l.MontoMxn), lineas, mov.MotivoClave), ct);
    }
}

public sealed record LineaSalidaInventarioPayload(
    Guid LineaId, Guid ArticuloId, string UnidadMedida, decimal Cantidad, decimal CostoUnitarioMxn, decimal MontoMxn, Guid? CentroCostoId = null);
