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
// bancario se resuelve al emitir manualmente por configuración (ReppAutomaticoOptions).
// ============================================================================

/// <summary>Factura cubierta por el depósito confirmado (espejo de <c>PagoClienteFacturaAplicada</c>).</summary>
public sealed record PagoClienteFacturaAplicadaPayload(
    Guid FacturaVentaId,
    decimal ImporteAplicado);

/// <summary>
/// Espejo de <c>tesoreria.pago-cliente.confirmado.v1</c> (TES-PR7, §3.3
/// del levantamiento de Tesorería): el hecho bancario de un cobro de
/// cliente quedó confirmado contra la propuesta de CxC. Facturación lo
/// consume para crear un pendiente de revisión; CxC aplica a cartera al
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
    IReadOnlyList<PagoClienteFacturaAplicadaPayload> Facturas, decimal SaldoAFavorPorIdentificar = 0)
{
    public const string EventType = "tesoreria.pago-cliente.confirmado.v1";
}

/// <summary>
/// Espejo de <c>tesoreria.reembolso-nota-credito.actualizado.v1</c> (F6.3): cambió el estado de la devolución de dinero de
/// una NC (Autorizada, Rechazada, Cancelada o Pagada). En «Pagada» trae la fecha valor y la referencia bancaria.
/// Fuente: Tesoreria/Application/Integration/ReembolsoNotaCreditoIntegrationEvents.cs.
/// </summary>
public sealed record ReembolsoNotaCreditoActualizadoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid DevolucionId,
    string DevolucionFolio,
    Guid NotaCreditoId,
    string Estado,
    decimal Importe,
    string Moneda,
    DateOnly? FechaPago,
    string? ReferenciaBancaria,
    string? Motivo)
{
    public const string EventType = "tesoreria.reembolso-nota-credito.actualizado.v1";
}
