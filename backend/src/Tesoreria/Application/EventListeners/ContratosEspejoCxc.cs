namespace Millet.Tesoreria.Application.EventListeners;

// ============================================================================
// Contratos ESPEJO de los eventos que Tesorería CONSUME de CxC (TES-PR7).
//
// Fuente de verdad:
// backend/src/CuentasPorCobrar/Application/Integration/CuentasPorCobrarIntegrationEvents.cs
// (PropuestaAplicacionPagoCreadaEvent). Nombres y tipos byte-compatibles
// (cuidados-infra §2.2); el test de contrato TesoreriaContratosConsumidosTests
// hace el round-trip contra el record real de CxC.
//
// `Facturas` es la extensión ADITIVA de este PR (el evento original solo
// traía NumeroFacturas): sin el detalle (FacturaVentaId, ImporteAplicado),
// Tesorería no puede armar el payload de pago-cliente.confirmado.v1 que
// Facturación necesita para EmitirReppCommand — y leer las tablas de CxC
// está prohibido (regla de oro). Nullable a propósito: eventos publicados
// antes de la extensión deserializan con Facturas=null.
// ============================================================================

/// <summary>Detalle por factura de la propuesta (espejo de <c>PropuestaAplicacionFacturaDetalle</c> de CxC).</summary>
public sealed record PropuestaFacturaPayload(
    Guid FacturaVentaId,
    string Folio,
    decimal ImporteAplicado);

/// <summary>
/// Espejo de <c>cuentas_por_cobrar.propuesta-aplicacion.creada.v1</c>
/// (CXC-PR7 + extensión aditiva TES-PR7). CxC propone el matching
/// depósito↔facturas; Tesorería lo proyecta a su bandeja de depósitos por
/// confirmar (§3.3, TES-9).
/// </summary>
public sealed record PropuestaAplicacionCreadaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid PropuestaId,
    Guid ClienteId,
    string DepositoRef,
    decimal MontoDeposito,
    string Moneda,
    decimal AjusteNoFiscal,
    int NumeroFacturas,
    IReadOnlyList<PropuestaFacturaPayload>? Facturas)
{
    public const string EventType = "cuentas_por_cobrar.propuesta-aplicacion.creada.v1";
}
