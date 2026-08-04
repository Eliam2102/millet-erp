using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.pasivo.autorizado-para-pago.v1</c>
/// (F9-PR1). Publicado al outbox cuando una <c>FacturaProveedor</c>
/// transiciona a <see cref="Domain.FacturaProveedor.EstadoPasivo.Autorizada"/>.
/// Consumidor: módulo Tesorería (futuro o externo) — usa el payload
/// para programar el pago al proveedor.
///
/// <para>
/// <b>MVP F9-PR1</b>: el payload lleva los datos disponibles en CxP
/// (factura_id, proveedor_id, monto, vencimiento, saldo pendiente).
/// Los datos bancarios + el array completo de evidencias_autorizacion
/// los resuelve Tesorería vía sus propias queries a <c>DatosMaestros</c>
/// y al endpoint de evidencias polimórficas (F4-PR2).
/// PLATFORM-TODO(<c>PayloadEnriquecido</c>): si la API de Tesorería
/// requiere bank data inline, agregar <c>IProveedorBancoReadPort</c>
/// y resolver al publicar.
/// </para>
///
/// <para>
/// TES-PR8 [T-G11]: extensión ADITIVA con <c>MetodoPago</c> (PUE/PPD, del
/// CFDI de la factura) — Tesorería lo necesita para el read model de
/// "pagos PPD sin REPP recibido". Aditivo y nullable = compatible por
/// convención; el espejo en Tesorería lo deserializa opcional. Espejo:
/// backend/src/Tesoreria/Application/EventListeners/ContratosEspejoCxp.cs
/// </para>
///
/// <para>
/// GI-PR1 [doc 12 §D1-b]: extensión ADITIVA para <b>pasivos internos</b>
/// (reposición de caja chica y préstamo de viáticos). Ausente/
/// <c>"Proveedor"</c> = comportamiento actual (pasivo de factura). Para
/// internos, <c>FacturaProveedorId</c> y <c>ProveedorId</c> viajan como
/// <c>Guid.Empty</c> y la correlación es <c>OrigenTipo + OrigenId</c>;
/// el listener de Tesorería los ignora hasta GI-PR2 (bandeja de
/// beneficiarios internos).
/// </para>
/// </summary>
public sealed record PasivoAutorizadoParaPagoIntegrationEvent(
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
    string? TipoBeneficiario = null,
    Guid? BeneficiarioId = null,
    string? OrigenTipo = null,
    Guid? OrigenId = null)
    : IntegrationEvent("cuentas_por_pagar.pasivo.autorizado-para-pago.v1", EmpresaId, OcurridoEn)
{
    public const string BeneficiarioProveedor = "Proveedor";
    public const string BeneficiarioEmpleado = "Empleado";
    public const string BeneficiarioCajaSucursal = "CajaSucursal";

    public const string OrigenFactura = "Factura";
    public const string OrigenReposicionCajaChica = "ReposicionCajaChica";
    public const string OrigenPrestamoViaticos = "PrestamoViaticos";
    public const string OrigenLiquidacionViaticos = "LiquidacionViaticos";
}
