namespace Millet.Compras.Domain.Ports.Catalogos;

public sealed record ClaveSatLectura(string Descripcion, bool Activa);

/// <summary>Catálogos SAT compartidos. Incluye inactivos para describir documentos históricos.</summary>
public interface ICatalogoSatReadPort
{
    Task<ClaveSatLectura?> ObtenerUsoCfdiAsync(string clave, CancellationToken cancellationToken);
    Task<ClaveSatLectura?> ObtenerFormaPagoAsync(string clave, CancellationToken cancellationToken);
}
