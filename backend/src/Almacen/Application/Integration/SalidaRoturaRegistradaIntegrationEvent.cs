using Millet.Almacen.Domain.Roturas;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.salida_rotura.registrada.v1</c> (ALM-03). Una rotura autorizada: el movimiento ya bajó el saldo a
/// costo promedio. Se publica una vez por rotura. Consumidor previsto: <b>Contabilidad</b> (póliza de merma, C1.5), por eso
/// la rotura NO publica además <c>almacen.salida_inventario.registrada.v1</c>: se contabilizaría doble.
/// <c>CentroCostoId</c> va null mientras la rotura no capture centro de costo (aditivo: se llenará sin bump de versión).
/// </summary>
public sealed record SalidaRoturaRegistradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RoturaId,
    Guid MovimientoId,
    string FolioMovimiento,
    DateOnly FechaMovimiento,
    Guid? AlmacenId,
    Guid SucursalId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal Cantidad,
    decimal CostoUnitarioMxn,
    decimal ValorMxn,
    string MotivoCodigo,
    string Motivo,
    Guid? CentroCostoId,
    Guid CapturadoPor,
    Guid AutorizadoPor)
    : IntegrationEvent("almacen.salida_rotura.registrada.v1", EmpresaId, OcurridoEn)
{
    public static SalidaRoturaRegistradaIntegrationEvent De(SalidaRotura r, Guid? almacenId) => new(
        r.EmpresaId, DateTimeOffset.UtcNow, r.Id, r.MovimientoId!.Value, r.FolioMovimiento!, r.Fecha, almacenId, r.SucursalId,
        r.ArticuloId, r.UnidadMedida, r.Cantidad, r.CostoUnitarioMxn!.Value, r.ValorMxn!.Value, r.MotivoCodigo,
        SalidaRotura.Motivos[r.MotivoCodigo], null, r.CapturadoPor, r.DecididoPor!.Value);
}
