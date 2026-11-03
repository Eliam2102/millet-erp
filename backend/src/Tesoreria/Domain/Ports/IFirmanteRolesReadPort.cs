namespace Millet.Tesoreria.Domain.Ports;

/// <summary>
/// Códigos de rol (Identidad) que tiene un usuario en la empresa actual. Los usa la corrida de pagos para
/// exigir «al menos una firma con el rol X» (T9.1-A). Solo lectura; nunca JOIN físico (ADR-0042).
/// </summary>
public interface IFirmanteRolesReadPort
{
    Task<IReadOnlyCollection<string>> CodigosRolAsync(Guid usuarioId, Guid empresaId, CancellationToken cancellationToken);
}
