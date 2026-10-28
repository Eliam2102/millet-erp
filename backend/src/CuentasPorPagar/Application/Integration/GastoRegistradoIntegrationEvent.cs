using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.gasto.registrado.v1</c> (P8.6). Un evento por
/// gasto firme que no pasa por <c>factura.registrada.v1</c> (ese es solo de OC):
/// movimiento de tarjeta (con o sin CFDI), línea de caja chica al aprobar el pasivo,
/// línea de viáticos al liberar y captura retroactiva desde el estado de cuenta.
/// Consumidor previsto: Contabilidad (póliza C1.5). Sin reversa: no se publica
/// al rechazar ni cancelar, solo en el punto firme.
///
/// <para>
/// <see cref="Origen"/>: <c>tarjeta</c>, <c>caja_chica</c>, <c>viaticos</c> o
/// <c>estado_cuenta</c>. <see cref="DocumentoId"/> = movimiento de tarjeta,
/// comprobación de caja chica o solicitud de viáticos; <see cref="LineaId"/> =
/// línea de la comprobación o solicitud (null en tarjeta). Sin CFDI no hay
/// desglose: <see cref="IvaTrasladado"/> e <see cref="IvaNoAcreditable"/> van en 0.
/// <see cref="IvaNoAcreditable"/> = <see cref="IvaTrasladado"/> si el gasto es no
/// deducible, 0 si es deducible.
/// </para>
/// </summary>
public sealed record GastoRegistradoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    string Origen,
    Guid DocumentoId,
    Guid? LineaId,
    Guid? FacturaProveedorId,
    DateOnly Fecha,
    Guid? ProveedorId,
    string? ProveedorRfc,
    string? Uuid,
    bool Deducible,
    string? ClasificacionNoDeducible,
    decimal Subtotal,
    decimal IvaTrasladado,
    decimal IvaNoAcreditable,
    decimal Total,
    string Moneda,
    decimal? TipoCambio = null,
    string? ConceptoContable = null)
    : IntegrationEvent("cuentas_por_pagar.gasto.registrado.v1", EmpresaId, OcurridoEn)
{
    public const string OrigenTarjeta = "tarjeta";
    public const string OrigenCajaChica = "caja_chica";
    public const string OrigenViaticos = "viaticos";
    public const string OrigenEstadoCuenta = "estado_cuenta";

    /// <summary>IVA no acreditable según la clasificación: null = deducible → 0.</summary>
    public static decimal IvaNoAcred(string? clasificacion, decimal iva) => clasificacion is null ? 0m : iva;
}
