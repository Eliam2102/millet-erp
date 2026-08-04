namespace Millet.Tesoreria.Application.EventListeners;

// ============================================================================
// Contratos ESPEJO de los eventos que Tesorería CONSUME de CxP (TES-PR3).
//
// ⚠️ CONTRATO CONGELADO (levantamiento §1.4): estos records replican 1:1 el
// payload que CxP publica — fuente de verdad:
// backend/src/CuentasPorPagar/Application/Integration/PasivoAutorizadoParaPagoIntegrationEvent.cs
// No "mejorar" nombres ni tipos: la deserialización es case-insensitive pero
// nombres y tipos deben coincidir (04-cuidados-infra §2.2). Nota: UuidCfdi
// viaja como string (así lo publica CxP); Tesorería lo parsea a Guid al
// proyectar.
//
// El payload NO trae datos bancarios del proveedor
// (PLATFORM-TODO(PayloadEnriquecido) en CxP): se resuelven por
// IProveedorBancoReadPort [T-G1]. SaldoPendiente ya viene neto de
// anticipos y NC aplicadas. MetodoPago llegó en TES-PR8 como extensión
// ADITIVA [T-G11, decisión (a) 2026-07-15] — nullable: eventos previos y
// facturas sin CFDI ligado deserializan null.
// ============================================================================

/// <summary>
/// Espejo de <c>cuentas_por_pagar.pasivo.autorizado-para-pago.v1</c>
/// (F9-PR1 + extensión aditiva TES-PR8). Publicado cuando una
/// <c>FacturaProveedor</c> transiciona a <c>Autorizada</c>; Tesorería lo
/// proyecta a la bandeja <c>pasivo_pendiente_pago</c>.
/// </summary>
public sealed record PasivoAutorizadoParaPagoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid ProveedorId,
    Guid? OrdenCompraId,
    decimal MontoTotal,
    decimal SaldoPendiente,
    string Moneda,
    decimal? TipoCambio,
    DateOnly FechaVencimiento,
    string? UuidCfdi,
    string? FolioProveedor,
    string? MetodoPago = null,
    // GI-PR1 (doc 12 §D1-b): extensión aditiva para pasivos internos
    // (reposición de caja chica, préstamo de viáticos). Ausente/
    // "Proveedor" = pasivo de factura (comportamiento actual).
    string? TipoBeneficiario = null,
    Guid? BeneficiarioId = null,
    string? OrigenTipo = null,
    Guid? OrigenId = null)
{
    public const string EventType = "cuentas_por_pagar.pasivo.autorizado-para-pago.v1";

    public const string BeneficiarioProveedor = "Proveedor";

    /// <summary>True si el pasivo es interno (empleado / caja de sucursal), no de factura.</summary>
    public bool EsPasivoInterno =>
        TipoBeneficiario is not null && TipoBeneficiario != BeneficiarioProveedor;
}

/// <summary>
/// Espejo de <c>cuentas_por_pagar.deposito-viaticos.esperado.v1</c>
/// (GI-PR4, doc 12 §D4/Q3): el empleado debe devolver la diferencia
/// negativa de su liquidación — se proyecta como expectativa de depósito
/// RN-6.
/// </summary>
public sealed record DepositoViaticosEsperadoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid SolicitudViaticosId,
    Guid EmpleadoId,
    decimal MontoEsperado,
    string Moneda)
{
    public const string EventType = "cuentas_por_pagar.deposito-viaticos.esperado.v1";
}
