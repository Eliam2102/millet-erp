namespace Millet.Identidad.Application.Ports;

/// <summary>
/// Puerto out-port al directorio de Microsoft Entra ID para el alta
/// unificada de colaboradores (plan 15, F2). Cubre los dos caminos con
/// cuenta Microsoft del wizard:
///
/// <list type="bullet">
///   <item><b>"Ya tiene cuenta Microsoft"</b>: <see cref="BuscarPorCorreoAsync"/>
///         confirma que la cuenta existe y devuelve su OID.</item>
///   <item><b>"Cuenta Microsoft nueva"</b>: <see cref="CrearCuentaAsync"/>
///         crea la cuenta con contraseña temporal (D6).</item>
/// </list>
///
/// <para>
/// Hoy solo existe <c>DirectorioEntraSimulado</c> (en memoria, ADR-0015).
/// El adaptador real a Microsoft Graph (<c>GET /users</c>,
/// <c>POST /users</c>) se conecta cuando TI entregue los permisos de la
/// App Registration (plan 15 §8); el contrato de este puerto no cambia.
/// </para>
/// </summary>
public interface IEntraDirectorioPort
{
    /// <summary>
    /// Busca una cuenta por UPN o correo principal. <c>null</c> si no existe.
    /// </summary>
    Task<CuentaEntra?> BuscarPorCorreoAsync(string correo, CancellationToken ct);

    /// <summary>
    /// Crea la cuenta en el directorio. Idempotente por
    /// <see cref="SolicitudCuentaEntra.ClaveIdempotencia"/>: repetir la
    /// misma solicitud devuelve la cuenta ya creada con una contraseña
    /// temporal nueva, en lugar de duplicarla (reintentos del worker, F4).
    /// </summary>
    /// <exception cref="Millet.SharedKernel.Application.Exceptions.ConflictException">
    /// <c>ENTRA_UPN_EN_USO</c> si el UPN ya pertenece a otra cuenta.
    /// </exception>
    /// <exception cref="Millet.SharedKernel.Application.Exceptions.BusinessRuleException">
    /// <c>ENTRA_DOMINIO_NO_PERMITIDO</c> si el dominio no está en
    /// <c>Entra:DominiosPermitidos</c>.
    /// </exception>
    Task<CuentaEntraCreada> CrearCuentaAsync(SolicitudCuentaEntra solicitud, CancellationToken ct);

    /// <summary>
    /// Asigna una contraseña temporal nueva, con cambio obligatorio en el
    /// siguiente inicio de sesión ("Reenviar acceso", F4). Devuelve la
    /// contraseña; como en <see cref="CuentaEntraCreada"/>, solo viaja en
    /// memoria hasta el correo.
    /// </summary>
    /// <exception cref="Millet.SharedKernel.Application.Exceptions.EntityNotFoundException">
    /// <c>ENTRA_CUENTA_NO_ENCONTRADA</c> si no hay cuenta con ese OID.
    /// </exception>
    Task<string> RestablecerContrasenaTemporalAsync(string objectId, CancellationToken ct);
}

/// <summary>
/// Cuenta del directorio. <see cref="ObjectId"/> es el OID que se guarda
/// en <c>Usuario.EntraOid</c>.
/// </summary>
public sealed record CuentaEntra(
    string ObjectId,
    string Upn,
    string NombreMostrado,
    bool Habilitada);

/// <summary>
/// Datos para crear una cuenta. <see cref="ClaveIdempotencia"/> es estable
/// por colaborador (el id del empleado) para que un reintento no cree una
/// segunda cuenta.
/// </summary>
public sealed record SolicitudCuentaEntra(
    string Upn,
    string NombreMostrado,
    string CorreoContacto,
    string ClaveIdempotencia);

/// <summary>
/// Resultado de crear una cuenta. La <see cref="ContrasenaTemporal"/> solo
/// viaja en memoria hasta el correo de acceso: nunca se persiste ni se
/// registra en logs o auditoría (plan 15, D6).
/// </summary>
public sealed record CuentaEntraCreada(CuentaEntra Cuenta, string ContrasenaTemporal)
{
    // El record no debe filtrar la contraseña si alguien lo loguea.
    public override string ToString() => $"CuentaEntraCreada {{ Cuenta = {Cuenta} }}";
}
