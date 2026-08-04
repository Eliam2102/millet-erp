namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto hacia Contabilidad para registrar el asiento contable de un comprobante
/// (§8, F10-PR1). Dueño real: el módulo <c>Contabilidad</c>, que mapea el
/// comprobante a su póliza. En dev es stub NoOp con cuentas <c>TBD-*</c>
/// (<c>PLATFORM-TODO(&lt;ContabilidadAsientos&gt;)</c>); la integración fina llega
/// cuando Contabilidad exista y consuma los eventos del topic.
/// </summary>
public interface IContabilidadAsientoPort
{
    Task RegistrarAsientoAsync(AsientoContableSolicitud solicitud, CancellationToken cancellationToken);
}

/// <summary>Datos del comprobante para el asiento (mapeo de cuentas lo resuelve Contabilidad).</summary>
public sealed record AsientoContableSolicitud(
    Guid ComprobanteId,
    string TipoComprobante,
    string Concepto,
    decimal Total,
    string Moneda,
    int PeriodoAnio,
    int PeriodoMes);
