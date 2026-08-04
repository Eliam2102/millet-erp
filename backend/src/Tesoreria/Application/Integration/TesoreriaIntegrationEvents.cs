using Millet.SharedKernel.Application.Integration;

namespace Millet.Tesoreria.Application.Integration;

// ============================================================================
// TES-PR4: los 4 eventos que Tesorería PUBLICA al topic `tesoreria-events`
// (outbox ADR-0009). ⚠️ CONTRATO CONGELADO (levantamiento §1.4): los
// payloads son espejo EXACTO de los records que CxP deserializa en
// backend/src/CuentasPorPagar/Application/EventListeners/ContratosEspejo.cs:97-141
// — nombres y tipos byte-compatibles (cuidados-infra §2.2). El test de
// contrato TesoreriaContratosCongeladosTests hace el round-trip contra esos
// records; NO cambiar nada aquí sin bumpear a v(N+1) en ambos lados.
//
// Consumidores: CxP (TesoreriaEventListenerWorker, desplegado F9-PR1,
// subscription `cuentas-por-pagar-tesoreria-sub`) y Compras (listener
// espejo PLATFORM-TODO(<TesoreriaEventListenerCompras>), se desbloquea
// con este publisher).
// ============================================================================

/// <summary>
/// <c>tesoreria.pago-factura-proveedor.aplicado.v1</c> — un pago (o la
/// ejecución de una línea de corrida, o una liga tardía de pago a cuenta)
/// se aplicó a una factura. Se emite POR FACTURA aun en pagos
/// multi-pasivo (RN-4). <c>PagoId</c> = Id de la
/// <c>AplicacionPagoProveedor</c>. Efecto en CxP: <c>RegistrarPago()</c> —
/// pasa a <c>Pagada</c> solo con saldo 0.
/// </summary>
public sealed record PagoFacturaProveedorAplicadoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid PagoId,
    decimal Monto,
    string Moneda,
    DateOnly FechaPago,
    string? MetodoPago,
    string? ReferenciaBancaria)
    : IntegrationEvent(WireEventType, EmpresaId, OcurridoEn)
{
    public const string WireEventType = "tesoreria.pago-factura-proveedor.aplicado.v1";
}

/// <summary>
/// <c>tesoreria.pago-prestamo-viaticos.aplicado.v1</c> (GI-PR3, doc 12
/// §D3-vuelta) — pago del préstamo de viáticos al empleado (pasivo
/// interno OrigenTipo=PrestamoViaticos). Efecto en CxP: la solicitud
/// transiciona a <c>Anticipada</c> (idempotente por solicitud); cierra
/// PLATFORM-TODO(&lt;TesoreriaPagoViaticosEvent&gt;) — el endpoint
/// <c>marcar-pagado</c> queda como fallback manual (Q2).
/// Espejo: CuentasPorPagar/Application/EventListeners/ContratosEspejo.cs.
/// </summary>
public sealed record PagoPrestamoViaticosAplicadoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid SolicitudViaticosId,
    Guid PagoId,
    decimal MontoPagado,
    string Moneda,
    DateOnly FechaPago,
    string? ReferenciaBancaria)
    : IntegrationEvent(WireEventType, EmpresaId, OcurridoEn)
{
    public const string WireEventType = "tesoreria.pago-prestamo-viaticos.aplicado.v1";
}

/// <summary>
/// <c>tesoreria.pago-factura-proveedor.revertido.v1</c> — reversa de una
/// aplicación (RN-10: contramovimiento ligado, nada se borra).
/// <c>PagoOriginalId</c> correlaciona con el <c>PagoId</c> del aplicado.
/// Efecto en CxP: <c>RevertirPago()</c> — regresa a <c>Autorizada</c> si
/// saldo &gt; 0.
/// </summary>
public sealed record PagoFacturaProveedorRevertidoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid PagoOriginalId,
    decimal MontoRevertido,
    string Moneda,
    DateOnly FechaReversa,
    string Motivo)
    : IntegrationEvent(WireEventType, EmpresaId, OcurridoEn)
{
    public const string WireEventType = "tesoreria.pago-factura-proveedor.revertido.v1";
}

/// <summary>
/// <c>tesoreria.repp-proveedor.recibido.v1</c> — el proveedor emitió su
/// complemento de pago (REPP) a Millet (se publica desde PR-8). Nota del
/// contrato: <c>UuidComplementoPago</c> viaja como string y
/// <c>FechaComplemento</c> como DateTimeOffset (así los espera CxP).
/// Efecto en CxP: <c>MarcarReppRecibido()</c> — libera el motivo de
/// revisión <c>FALTA_REPP</c>.
/// </summary>
public sealed record ReppProveedorRecibidoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    string UuidComplementoPago,
    DateTimeOffset FechaComplemento)
    : IntegrationEvent(WireEventType, EmpresaId, OcurridoEn)
{
    public const string WireEventType = "tesoreria.repp-proveedor.recibido.v1";
}

/// <summary>
/// <c>tesoreria.cancelacion-pasivo.solicitada.v1</c> — Tesorería pide a
/// CxP cancelar/revisar un pasivo de la bandeja. Efecto en CxP:
/// <c>EnviarARevision()</c> con motivo "Tesorería solicita cancelar".
/// </summary>
public sealed record CancelacionPasivoSolicitadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid UsuarioSolicitanteId,
    string Motivo)
    : IntegrationEvent(WireEventType, EmpresaId, OcurridoEn)
{
    public const string WireEventType = "tesoreria.cancelacion-pasivo.solicitada.v1";
}

// ============================================================================
// TES-PR7: eventos del lado INGRESOS (§3.3, TES-9). NO son parte del
// contrato congelado con CxP — el consumidor de `pago-cliente.confirmado`
// es Facturación (record espejo + test de contrato round-trip en el PR
// gemelo facturacion/tesoreria-repp-listener); el de
// `propuesta-aplicacion.rechazada` es CxC [T-G7], que aún no tiene el
// consumer. Cambios incompatibles bumpean a v(N+1) en ambos lados.
// ============================================================================

/// <summary>Factura cubierta por el depósito confirmado, con el importe aplicado.</summary>
public sealed record PagoClienteFacturaAplicada(
    Guid FacturaVentaId,
    decimal ImporteAplicado);

/// <summary>
/// <c>tesoreria.pago-cliente.confirmado.v1</c> — Tesorería confirmó el
/// hecho BANCARIO de un depósito de cliente contra una propuesta de
/// aplicación de CxC (RN-6: solo desde movimiento de ingreso
/// identificado). Facturación lo consume para invocar
/// <c>EmitirReppCommand</c>; CxC aplica a cartera al consumir el timbrado
/// del REPP — cierra PLATFORM-TODO(&lt;PagoClienteConfirmado&gt;) [TES-9].
/// Sin datos bancarios del CLIENTE a propósito (cuidados-infra §4):
/// <c>Referencia</c> es la referencia del movimiento en el banco de
/// Millet, no una cuenta de terceros.
/// </summary>
public sealed record PagoClienteConfirmadoIntegrationEvent(
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
    IReadOnlyList<PagoClienteFacturaAplicada> Facturas)
    : IntegrationEvent(WireEventType, EmpresaId, OcurridoEn)
{
    public const string WireEventType = "tesoreria.pago-cliente.confirmado.v1";
}

/// <summary>
/// <c>tesoreria.propuesta-aplicacion.rechazada.v1</c> — Tesorería rechazó
/// la propuesta (depósito no aparece, monto no coincide) para que CxC
/// re-proponga [T-G7]. PLATFORM-TODO(&lt;PropuestaRechazadaConsumerCxC&gt;):
/// CxC aún NO consume `tesoreria-events` — mientras tanto el rechazo se
/// resuelve espejo en CxC vía su endpoint interino A2 (el agregado
/// <c>PropuestaAplicacionPago</c> ya contempla <c>Rechazada</c>).
/// </summary>
public sealed record PropuestaAplicacionRechazadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid PropuestaId,
    Guid? ClienteId,
    string Motivo,
    Guid RechazadaPor)
    : IntegrationEvent(WireEventType, EmpresaId, OcurridoEn)
{
    public const string WireEventType = "tesoreria.propuesta-aplicacion.rechazada.v1";
}
