using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.anticipo-agente.comprobacion-revertida.v1</c> (M-327). Se publica cuando Cuentas por pagar recibe
/// la reversa autorizada de la comprobación de un anticipo al agente aduanal y crea su saldo a favor. Consumidor: Contabilidad (póliza
/// de reversa): cargo al saldo del agente (anticipo a proveedores) contra abono a Anticipo al agente aduanal por
/// <see cref="SaldoAFavorMxn"/>; y cargo al saldo del agente contra abono a Proveedores por <see cref="GastosLiberadosMxn"/> (las
/// facturas del agente que la comprobación había saldado vuelven a quedar por pagar). Llave: <see cref="SolicitudId"/>.
/// </summary>
public sealed record AnticipoAgenteComprobacionRevertidaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid AnticipoProveedorId,
    Guid AnticipoAgenteId,
    Guid SolicitudId,
    Guid ProveedorId,
    Guid? SucursalId,
    decimal SaldoAFavorMxn,
    decimal GastosLiberadosMxn,
    DateOnly Fecha,
    string FolioExpediente)
    : IntegrationEvent("cuentas_por_pagar.anticipo-agente.comprobacion-revertida.v1", EmpresaId, OcurridoEn);
