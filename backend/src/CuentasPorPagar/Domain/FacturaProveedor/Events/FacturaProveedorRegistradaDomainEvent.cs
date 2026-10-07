using MediatR;
using Millet.CuentasPorPagar.Domain.Cfdi;

namespace Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;

/// <summary>
/// Evento in-process emitido tras una captura exitosa de
/// <see cref="FacturaProveedor"/> contra una OC (estado
/// <see cref="EstadoPasivo.Capturada"/>). El mapper de Integration
/// lo traduce a <c>cuentas_por_pagar.factura.registrada.v1</c> que
/// **Compras** suscribe (actualiza <c>CantidadFacturada</c> y
/// <c>SubEstadoFacturacion</c> de OC) y **Almacén** (variante B:
/// concilia recepción pendiente).
///
/// <para>
/// <b>LineasAcumuladasOc</b> (PR D): acumulado total facturado por
/// <c>LineaOcId</c> tras aplicar esta factura. El handler de captura
/// suma facturas previas vigentes + el delta de la factura corriente
/// antes de emitir el evento — Compras lo usa directo como
/// <c>CantidadFacturadaAcumulada</c> en
/// <c>OrdenCompra.RegistrarFacturacionLinea</c>.
/// </para>
/// </summary>
public sealed record FacturaProveedorRegistradaDomainEvent(
    Guid EmpresaId,
    Guid FacturaProveedorId,
    Guid OrdenCompraId,
    decimal TotalFactura,
    IReadOnlyList<LineaFacturada> Lineas,
    IReadOnlyList<LineaOcAcumulada> LineasAcumuladasOc,
    DateTimeOffset OcurridoEn,
    // G1.6: bloque contable opcional (al final, compatible con constructores existentes).
    Guid? ProveedorId = null,
    string? Uuid = null,
    decimal? Subtotal = null,
    decimal? Iva = null,
    decimal? RetencionesTotal = null,
    IReadOnlyList<RetencionCfdi>? Retenciones = null,
    string? Moneda = null,
    decimal? TipoCambio = null,
    Guid? SucursalId = null,
    Guid? CentroCostoId = null) : INotification;

public sealed record LineaFacturada(
    Guid LineaFacturaId,
    Guid? LineaOcId,
    decimal Cantidad,
    decimal Importe,
    Guid? CentroCostoId = null);

public sealed record LineaOcAcumulada(
    Guid LineaOcId,
    decimal CantidadAcumulada);
