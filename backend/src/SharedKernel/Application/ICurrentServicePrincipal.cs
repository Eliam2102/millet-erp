namespace Millet.SharedKernel.Application;

/// <summary>
/// Contexto del service principal autenticado en el request actual (PR A).
/// Análogo a <see cref="ICurrentUserContext"/> pero específico de SPs M2M
/// (Glass Agent y futuros).
///
/// <para>
/// <b>Coexistencia con ICurrentUserContext:</b> el handler de SP arma el
/// <c>ClaimsPrincipal</c> con los claims estándar (<c>sub</c>,
/// <c>current_empresa_id</c>, <c>name</c>) además de los específicos de
/// SP (<c>auth_type=service_principal</c>, <c>entra_appid</c>,
/// <c>permission</c>×N). Eso significa que <c>ICurrentUserContext.UserId</c>
/// devuelve el <c>UsuarioServicio.Id</c> en requests de SP — el resto del
/// stack (idempotencia, audit_log, query filters) trata al SP como un
/// "usuario" sin saberlo. Esta interfaz se usa solo cuando el endpoint
/// necesita explícitamente saber "esto vino de un SP" o el AppId.
/// </para>
///
/// <para>
/// Si el request actual NO es de SP (humano o anónimo), <see cref="IsServicePrincipal"/>
/// es <c>false</c> y los demás properties son <c>null</c>.
/// </para>
/// </summary>
public interface ICurrentServicePrincipal
{
    /// <summary>True si el request actual está autenticado como service principal.</summary>
    bool IsServicePrincipal { get; }

    /// <summary>Id del <c>UsuarioServicio</c> resuelto. Null si no es SP.</summary>
    Guid? Id { get; }

    /// <summary>Empresa fija del SP. Null si no es SP.</summary>
    Guid? EmpresaId { get; }

    /// <summary>Nombre legible del SP (ej. "Glass Agent - Production"). Null si no es SP.</summary>
    string? Nombre { get; }

    /// <summary>AppId (claim <c>appid</c>) del token Entra. Útil para correlación de logs. Null si no es SP.</summary>
    Guid? EntraAppId { get; }

    /// <summary>
    /// Permisos efectivos del SP (claims <c>permission</c>×N). Lista vacía
    /// si no es SP o si el SP fue resuelto sin permisos (válido — denegará
    /// todos los endpoints con <c>RequireAuthorization(permiso:...)</c>).
    /// </summary>
    IReadOnlyList<string> Permisos { get; }
}
