using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.anticipo.amortizado.v1</c> (C1.5b, 86e3vrh7y). Se publica al aplicar un anticipo a proveedor a una
/// factura del mismo proveedor (Módulo 08 R9: la amortización descuenta del saldo de la factura y del anticipo). Consumidor:
/// Contabilidad (póliza de amortización: proveedores contra anticipo a proveedores). Una aplicación = un evento;
/// <see cref="AplicacionId"/> es el movimiento de la factura y sirve de llave del documento.
///
/// <para>
/// <see cref="TipoCambio"/> es el del anticipo (el saldo del anticipo se valuó a ese tipo); <see cref="TipoCambioFactura"/> es el de la
/// factura, para que Contabilidad pueda calcular la diferencia cambiaria en CON-11 (aquí no se calcula). <see cref="SucursalId"/> es
/// la de la factura (null si la factura no tiene sucursal, p. ej. la agregada de tarjeta).
/// Contrato validado por el dueño de CxP 5-nov.
/// </para>
/// </summary>
public sealed record AnticipoProveedorAmortizadoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid AplicacionId,
    Guid AnticipoId,
    Guid FacturaProveedorId,
    Guid ProveedorId,
    decimal Monto,
    string Moneda,
    decimal? TipoCambio,
    Guid? SucursalId,
    string? UuidAnticipo,
    string? UuidFactura,
    decimal? TipoCambioFactura = null)
    : IntegrationEvent("cuentas_por_pagar.anticipo.amortizado.v1", EmpresaId, OcurridoEn);
