using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Domain.Depositos;

namespace Millet.Tesoreria.Application.Integration;

/// <summary>
/// <c>tesoreria.diferencia-corte.autorizada.v1</c> — Tesorería autorizó el faltante o sobrante de un
/// corte (Millet 23-oct). Contabilidad lo contabiliza contra «deudores funcionarios y empleados» con el
/// cajero como auxiliar: faltante = cargo al deudor, sobrante = abono a favor del cajero. <c>Importe</c>
/// va en positivo; <c>Tipo</c> dice el signo. La plantilla de la póliza es de C1.5.
/// </summary>
public sealed record DiferenciaCorteAutorizadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid DiferenciaId,
    Guid CajaSesionId,
    DateOnly DiaCorte,
    Guid SucursalId,
    Guid CajeroId,
    string Tipo,
    decimal Importe,
    string Moneda,
    Guid AutorizadaPor)
    : IntegrationEvent(WireEventType, EmpresaId, OcurridoEn)
{
    public const string WireEventType = "tesoreria.diferencia-corte.autorizada.v1";
    public const string Faltante = "Faltante";
    public const string Sobrante = "Sobrante";
}

/// <summary>
/// <c>tesoreria.diferencia-corte.devolucion-aplicada.v1</c> — el cajero devolvió (todo o parte de) un
/// faltante autorizado; Contabilidad abona al deudor (cajero como auxiliar). <c>CajaMovimientoId</c> es el
/// depósito «Devolución de faltante» de Mi caja que lo originó (null si es el abono acumulado al autorizar).
/// </summary>
public sealed record DiferenciaCorteDevolucionAplicadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid DiferenciaId,
    Guid CajaSesionOrigenId,
    DateOnly DiaCorteOrigen,
    Guid SucursalId,
    Guid CajeroId,
    Guid? CajaMovimientoId,
    decimal Importe,
    decimal ImporteDevueltoAcumulado,
    decimal SaldoPendiente,
    bool Saldada,
    string Moneda)
    : IntegrationEvent(WireEventType, EmpresaId, OcurridoEn)
{
    public const string WireEventType = "tesoreria.diferencia-corte.devolucion-aplicada.v1";
}

internal static class DiferenciaCorteEventos
{
    public static DiferenciaCorteAutorizadaIntegrationEvent Autorizada(DiferenciaCorte d, Guid usuarioId, DateTimeOffset ahora) =>
        new(d.EmpresaId, ahora, d.Id, d.CajaSesionId, d.DiaCorte, d.SucursalId, d.CajeroId,
            d.EsFaltante ? DiferenciaCorteAutorizadaIntegrationEvent.Faltante : DiferenciaCorteAutorizadaIntegrationEvent.Sobrante,
            Math.Abs(d.Importe), "MXN", usuarioId);

    public static DiferenciaCorteDevolucionAplicadaIntegrationEvent Devolucion(
        DiferenciaCorte d, decimal importe, Guid? cajaMovimientoId, DateTimeOffset ahora) =>
        new(d.EmpresaId, ahora, d.Id, d.CajaSesionId, d.DiaCorte, d.SucursalId, d.CajeroId, cajaMovimientoId,
            importe, d.ImporteDevuelto, d.PendientePorDevolver, d.Estado == EstadoDiferenciaCorte.Saldada, "MXN");
}
