namespace Millet.Tesoreria.Domain.Ports;

/// <summary>
/// Nombres de cajero (Identidad) y de caja (Facturación) para las bandejas de cortes
/// (diferencias de corte y depósitos de caja). Solo lectura por lote; nunca JOIN físico (ADR-0042).
/// Los ids no encontrados no aparecen en el diccionario.
/// </summary>
public interface ICorteNombresReadPort
{
    Task<IReadOnlyDictionary<Guid, string>> NombresCajerosAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, string>> NombresCajasAsync(IReadOnlyCollection<Guid> cajaIds, CancellationToken cancellationToken);
}
