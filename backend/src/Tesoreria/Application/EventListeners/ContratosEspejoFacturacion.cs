namespace Millet.Tesoreria.Application.EventListeners;

// ============================================================================
// Contratos ESPEJO de los eventos que Tesorería CONSUME de Facturación
// (TES-PR7). Fuente de verdad:
// backend/src/Facturacion/Application/Integration/FacturacionIntegrationEvents.cs
// (CajaSesionCerradaIntegrationEvent, ReciboPagoTimbradoIntegrationEvent).
//
// Los espejos son SUBCONJUNTOS deliberados del payload publicado — la
// deserialización System.Text.Json ignora propiedades extra, así que un
// campo aditivo del publisher no rompe nada aquí. Nombres y tipos de lo
// que SÍ se consume deben coincidir (cuidados-infra §2.2); test de
// contrato en TesoreriaContratosConsumidosTests.
// ============================================================================

/// <summary>
/// Espejo (subset) de <c>facturacion.caja-sesion.cerrada.v1</c>
/// (CAJAS-PR3). Genera la expectativa de depósito Caja→Banco (§3.3 paso
/// 5); cierra PLATFORM-TODO(&lt;TesoreriaCajaSesion&gt;).
/// </summary>
public sealed record CajaSesionCerradaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid CajaSesionId,
    Guid CajaId,
    Guid SucursalId,
    DateOnly DiaOperacion,
    decimal EfectivoDeclarado,
    Guid? ResponsableUsuarioId = null,
    // Faltante (−) o sobrante (+) del efectivo; de él nace la diferencia pendiente de Tesorería.
    decimal Diferencia = 0m)
{
    public const string EventType = "facturacion.caja-sesion.cerrada.v1";
}

/// <summary>
/// Espejo (subset) de <c>facturacion.caja-faltante.devuelto.v1</c>: el cajero devolvió (todo o parte de)
/// el faltante de un corte anterior como depósito en Mi caja. Tesorería lo abona a la diferencia.
/// </summary>
public sealed record CajaFaltanteDevueltoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid CajaMovimientoId,
    Guid CajaSesionId,
    Guid CorteOrigenSesionId,
    Guid CajeroId,
    decimal Importe)
{
    public const string EventType = "facturacion.caja-faltante.devuelto.v1";
}

/// <summary>Desglose por factura del REPP (espejo de <c>ReppFacturaPagadaDetalle</c>).</summary>
public sealed record ReppFacturaPagadaPayload(
    Guid FacturaVentaId,
    decimal ImportePagado);

/// <summary>
/// Espejo (subset) de <c>facturacion.recibo-pago.timbrado.v1</c> (F10-PR1).
/// Cierra el ciclo de ingresos: la confirmación cuyo desglose de facturas
/// coincide se marca <c>repp_timbrado=true</c> (fiscalmente cubierta).
/// La mayoría de los REPP (manuales, mostrador) no corresponden a una
/// confirmación de Tesorería — eso es normal, no error.
/// </summary>
public sealed record ReciboPagoTimbradoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid ReciboPagoId,
    string Uuid,
    decimal ImporteTotalPago,
    IReadOnlyList<ReppFacturaPagadaPayload> FacturasPagadas,
    Guid? MovimientoBancarioId = null,
    Guid? PropuestaId = null)
{
    public const string EventType = "facturacion.recibo-pago.timbrado.v1";
}

/// <summary>
/// Espejo (subset) de <c>facturacion.nota-credito.reembolso-solicitado.v1</c>
/// (T9.6 · CA6.17): Facturación pide devolver en dinero una NC timbrada y
/// Tesorería crea la <c>DevolucionCliente</c> de origen NC.
/// </summary>
public sealed record NotaCreditoReembolsoSolicitadoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid NotaCreditoId,
    string Folio,
    string? Uuid,
    Guid ClienteId,
    decimal Importe,
    decimal TotalNotaCredito,
    string Moneda,
    Guid SucursalId,
    string Motivo,
    Guid SolicitadoPor)
{
    public const string EventType = "facturacion.nota-credito.reembolso-solicitado.v1";
}
