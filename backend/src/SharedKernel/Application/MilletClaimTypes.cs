namespace Millet.SharedKernel.Application;

/// <summary>
/// Constantes con los nombres de los claims que el API JWT incluye. El issuer
/// (<c>JwtTokenService</c>) y los readers (<c>CurrentUserContext</c>,
/// <c>CurrentEmpresaContext</c>) referencian estas constantes para evitar
/// strings duplicados/desincronizados.
///
/// El bearer middleware se configura con <c>MapInboundClaims = false</c>
/// para preservar los nombres originales del JWT (en lugar de remapearlos
/// a <c>ClaimTypes.NameIdentifier</c> y derivados). Por eso definimos aquí
/// también los claims estándar (<c>sub</c>, <c>email</c>, <c>name</c>) que
/// de otro modo vendrían de <c>JwtRegisteredClaimNames</c> en
/// <c>System.IdentityModel.Tokens.Jwt</c>: SharedKernel no debe depender de
/// ese paquete.
/// Ver ADR-0007.
/// </summary>
public static class MilletClaimTypes
{
    /// <summary>Subject del JWT — el ID del usuario (Guid).</summary>
    public const string Sub = "sub";

    /// <summary>Email del usuario.</summary>
    public const string Email = "email";

    /// <summary>Nombre del usuario para mostrar en UI.</summary>
    public const string Name = "name";

    /// <summary>
    /// Empresa actualmente seleccionada por el usuario en su sesión.
    /// Tipo: GUID en string. Puede no estar presente si el usuario aún no
    /// tiene asignaciones (recién auto-provisionado) o no ha seleccionado.
    /// </summary>
    public const string CurrentEmpresaId = "current_empresa_id";

    /// <summary>
    /// Tipo de autenticación del request actual. Valores conocidos:
    /// <c>"user"</c> (humano via API JWT clásico) y <c>"service_principal"</c>
    /// (M2M via token Entra ID directo).
    /// </summary>
    public const string AuthType = "auth_type";

    /// <summary>
    /// Valores posibles del claim <see cref="AuthType"/>.
    /// </summary>
    public static class AuthTypes
    {
        public const string User = "user";
        public const string ServicePrincipal = "service_principal";
    }

    /// <summary>
    /// Cada permiso efectivo del SP se emite como un claim individual con
    /// este tipo (el JWT Entra trae multi-valor; el handler los explota a
    /// claims separados). Los humanos NO usan este claim — sus permisos
    /// se cargan vía <c>IPermissionLoader</c> + cache.
    /// </summary>
    public const string Permission = "permission";

    /// <summary>
    /// AppId del service principal (claim <c>appid</c> del token Entra).
    /// Útil para correlación de logs y auditoría diferenciada de SPs.
    /// Solo presente cuando <see cref="AuthType"/> = <see cref="AuthTypes.ServicePrincipal"/>.
    /// </summary>
    public const string EntraAppId = "entra_appid";
}
