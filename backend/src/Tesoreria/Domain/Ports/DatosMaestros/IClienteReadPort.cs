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

    /// <summary>
    /// Búsqueda por razón social, clave o RFC (texto libre) para el selector
    /// de cliente de Tesorería. Devuelve como máximo <paramref name="limite"/> resultados.
    /// </summary>
    Task<IReadOnlyList<ClienteRefDto>> BuscarAsync(string texto, int limite, CancellationToken cancellationToken);
}
