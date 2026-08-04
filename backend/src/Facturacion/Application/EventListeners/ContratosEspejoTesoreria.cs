namespace Millet.Facturacion.Application.EventListeners;

// ============================================================================
// Contratos ESPEJO de los eventos que Facturación CONSUME de Tesorería
// (PR gemelo de TES-PR7). Fuente de verdad:
// backend/src/Tesoreria/Application/Integration/TesoreriaIntegrationEvents.cs
// (PagoClienteConfirmadoIntegrationEvent). Nombres y tipos byte-compatibles;
// test de contrato round-trip en TesoreriaPagoConfirmadoContratoTests
// (patrón TesoreriaContratosCongeladosTests). Cambios incompatibles
// bumpean a v2 en ambos lados.
//
// El payload NO trae datos bancarios del cliente (cuidados-infra §4 de
// Tesorería): `Referencia` es la referencia del movimiento en el banco de
// Millet. Tampoco trae SucursalId — la sucursal emisora del REPP
// automático se resuelve por configuración (ReppAutomaticoOptions).
// ============================================================================

/// <summary>Factura cubierta por el depósito confirmado (espejo de <c>PagoClienteFacturaAplicada</c>).</summary>
public sealed record PagoClienteFacturaAplicadaPayload(
    Guid FacturaVentaId,
    decimal ImporteAplicado);

/// <summary>
/// Espejo de <c>tesoreria.pago-cliente.confirmado.v1</c> (TES-PR7, §3.3
/// del levantamiento de Tesorería): el hecho bancario de un cobro de
/// cliente quedó confirmado contra la propuesta de CxC. Facturación lo
/// consume para emitir el REPP — cierra
/// PLATFORM-TODO(&lt;PagoClienteConfirmado&gt;); CxC aplica a cartera al
/// consumir <c>recibo-pago.timbrado.v1</c>.
/// </summary>
public sealed record PagoClienteConfirmadoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid? PropuestaId,
    Guid ClienteId,
    Guid MovimientoBancarioId,
    Guid CuentaBancariaId,
    decimal Monto,
    string Moneda,
    DateOnly FechaValor,
    string? Referencia,
    IReadOnlyList<PagoClienteFacturaAplicadaPayload> Facturas)
{
    public const string EventType = "tesoreria.pago-cliente.confirmado.v1";
}
