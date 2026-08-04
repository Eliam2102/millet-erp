namespace Millet.Tesoreria.Domain.Ports.DatosMaestros;

/// <summary>Referencia mínima del cliente para mostrar en bandejas (ADR-0042).</summary>
public sealed record ClienteRefDto(
    Guid Id,
    string Clave,
    string RazonSocial);

/// <summary>
/// Puerto de lectura del master de clientes de DatosMaestros (TES-PR7):
/// la bandeja de depósitos por confirmar resuelve nombres de cliente por
/// aquí, nunca JOIN físico (ADR-0042).
/// </summary>
public interface IClienteReadPort
{
    /// <summary>
    /// Resolución en lote para una página de bandeja: devuelve solo los
    /// encontrados, indexados por id.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ClienteRefDto>> ObtenerVariosAsync(
        IReadOnlyCollection<Guid> clienteIds, CancellationToken cancellationToken);
}
