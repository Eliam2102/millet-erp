using Millet.Tesoreria.Domain.Pasivos;

namespace Millet.Tesoreria.Domain.Ports;

/// <summary>
/// I13.3: identifica pasivos que requieren las firmas propias de Tesorería. Incluye facturas de un expediente de Comercio Exterior
/// (el tipo del expediente decide importación o exportación) o si su OC está marcada como importación.
/// Los servicios CE con firmas en su OC no repiten aquí esas firmas.
/// Solo lectura cross-módulo (ADR-0042); null = no requiere este circuito adicional.
/// </summary>
public interface IPasivoComercioExteriorReadPort
{
    Task ExigirAnticipoVigenteAsync(Guid anticipoId, CancellationToken ct) => throw new NotSupportedException();
    Task ExigirSinComprobacionAsync(Guid anticipoId, CancellationToken ct) => throw new NotSupportedException("Valide la comprobación del anticipo antes de revertir.");
    Task<TipoOperacionComercioExterior?> TipoAsync(Guid facturaProveedorId, Guid? ordenCompraId, CancellationToken cancellationToken);
}

public interface IAnticipoCeCancelacionPort
{
    Task CancelarAnticipoAsync(Guid id, string motivo, CancellationToken ct);
}
