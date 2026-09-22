using System.Threading;
using System.Threading.Tasks;

namespace Millet.Api.Auth.Provisioning;

public record ProvisioningResult(bool Success, string? EntraOid, string? Mensaje);

/// <summary>
/// Contrato para el provisionamiento de cuentas B2E (Business-to-Employee)
/// en Microsoft Entra ID vía Microsoft Graph API.
/// </summary>
public interface IEntraProvisioningService
{
    /// <summary>
    /// Crea un usuario nuevo en Azure Entra ID, le asigna una contraseña temporal
    /// y envía un correo de bienvenida.
    /// </summary>
    Task<ProvisioningResult> CrearNuevoUsuarioAsync(string nombre, string apellido, string upn, string correoContacto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida que un UPN ya exista en el Entra ID de la organización y obtiene su OID.
    /// Envía un correo de invitación informando que tiene acceso al ERP.
    /// </summary>
    Task<ProvisioningResult> VincularUsuarioExistenteAsync(string upn, string correoContacto, CancellationToken cancellationToken = default);
}
