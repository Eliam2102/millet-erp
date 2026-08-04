namespace Millet.Tesoreria.Domain.Ports.DatosMaestros;

/// <summary>
/// Datos del proveedor que la bandeja de pasivos y el flujo de pago
/// necesitan (TES-PR3, [T-G1]): identificación para mostrar (ADR-0042) y
/// datos bancarios para ejecutar la transferencia. La CLABE viaja
/// COMPLETA dentro del server; el masking se aplica en la capa de
/// queries/DTOs salvo permiso
/// <c>tesoreria.movimientos.ver-cuenta-completa</c>.
/// </summary>
public sealed record ProveedorBancoDto(
    Guid Id,
    string Clave,
    string RazonSocial,
    string? Banco,
    string? Clabe,
    string? Beneficiario);

/// <summary>
/// Puerto de lectura del master de proveedores de DatosMaestros
/// (TES-PR3). Resuelve el <c>PLATFORM-TODO(PayloadEnriquecido)</c> de CxP:
/// el evento de pasivo no trae datos bancarios; Tesorería los lee por
/// aquí al mostrar la bandeja y al ejecutar el pago. Nunca JOIN físico
/// (ADR-0042).
/// </summary>
public interface IProveedorBancoReadPort
{
    /// <summary>Datos de un proveedor; <c>null</c> si no existe.</summary>
    Task<ProveedorBancoDto?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolución en lote para una página de bandeja: devuelve solo los
    /// encontrados, indexados por id.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(
        IReadOnlyCollection<Guid> proveedorIds, CancellationToken cancellationToken);
}
