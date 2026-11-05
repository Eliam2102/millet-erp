using Millet.Tesoreria.Domain.Pasivos;

namespace Millet.Tesoreria.Domain.Ports;

/// <summary>
/// I13.3: dice si un pasivo es de comercio exterior. Lo es si su factura es gasto de un expediente de Comercio Exterior
/// (el tipo del expediente decide importación o exportación) o si su OC está marcada como importación.
/// Solo lectura cross-módulo (ADR-0042); null = no es de comercio exterior.
/// </summary>
public interface IPasivoComercioExteriorReadPort
{
    Task<TipoOperacionComercioExterior?> TipoAsync(Guid facturaProveedorId, Guid? ordenCompraId, CancellationToken cancellationToken);
}
