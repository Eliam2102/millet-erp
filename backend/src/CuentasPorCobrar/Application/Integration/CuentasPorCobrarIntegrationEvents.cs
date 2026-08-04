using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorCobrar.Application.Integration;

/// <summary>
/// Eventos de integración publicados por CxC (§8.1 del 01-diseño) al topic
/// <c>cuentas-por-cobrar-events</c> vía Outbox (ADR-0009). Naming canónico
/// <c>cuentas_por_cobrar.{recurso}.{accion}.vN</c>. Consumidores: write-back
/// de liberación a A+W (CXC-PR9) y Notificaciones (futuro).
/// </summary>
// ---- Alerta de cartera generada (CXC-PR8) ----
public sealed record AlertaCarteraGeneradaEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid AlertaId,
    Guid ClienteId,
    string Tipo,
    string Moneda,
    string Detalle)
    : IntegrationEvent("cuentas_por_cobrar.alerta-cartera.generada.v1", EmpresaId, OcurridoEn);

// ---- Propuesta de aplicación de pago creada (CXC-PR7) ----

/// <summary>
/// Desglose por factura de la propuesta (TES-PR7, extensión ADITIVA):
/// Tesorería lo necesita para armar el payload de
/// <c>tesoreria.pago-cliente.confirmado.v1</c> (Facturas[] con
/// <c>FacturaVentaId</c>, el id que <c>EmitirReppCommand</c> espera) sin
/// leer tablas de CxC (regla de oro). <c>Folio</c> es para display en la
/// bandeja de depósitos.
/// </summary>
public sealed record PropuestaAplicacionFacturaDetalle(
    Guid FacturaVentaId,
    string Folio,
    decimal ImporteAplicado);

// TES-PR7: extensión ADITIVA con Facturas[] (ver record de arriba).
// Aditivo = consumidores existentes no se afectan; cambios incompatibles
// bumpean a v2. Espejo del consumidor:
// backend/src/Tesoreria/Application/EventListeners/ContratosEspejoCxc.cs
public sealed record PropuestaAplicacionPagoCreadaEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid PropuestaId,
    Guid ClienteId,
    string DepositoRef,
    decimal MontoDeposito,
    string Moneda,
    decimal AjusteNoFiscal,
    int NumeroFacturas,
    IReadOnlyList<PropuestaAplicacionFacturaDetalle> Facturas)
    : IntegrationEvent("cuentas_por_cobrar.propuesta-aplicacion.creada.v1", EmpresaId, OcurridoEn);

// ---- Decisión de liberación emitida (CXC-PR4) ----
public sealed record DecisionLiberacionEmitidaEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid DecisionId,
    string PedidoRef,
    Guid ClienteId,
    string Moneda,
    decimal MontoPedido,
    string Resultado,
    string ReglaAplicada,
    decimal CreditoDisponibleSnapshot,
    Guid? OverrideId,
    Guid DecididoPor)
    : IntegrationEvent("cuentas_por_cobrar.decision-liberacion.emitida.v1", EmpresaId, OcurridoEn);
