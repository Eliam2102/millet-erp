using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Identidad.Domain;

/// <summary>
/// Identidad de un usuario del sistema. Una fila por persona — no por
/// asignación a empresa (ver <see cref="UsuarioEmpresaRol"/>). El
/// <c>EntraOid</c> es la referencia lógica a Microsoft Entra ID; en modo
/// <c>FakeForLocalDev</c> (ADR-0015) acepta strings sintéticos como
/// "dev-superadmin" en lugar de un GUID real.
/// Ver ADR-0003 y ADR-0007.
/// </summary>
public sealed class Usuario : BaseEntity, IAuditable
{
    public string EntraOid { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public bool Activo { get; private set; } = true;

    /// <summary>
    /// Departamento "primario" del usuario (B.1). Nullable en MVP — los
    /// usuarios auto-provisionados via Entra ID nacen sin departamento;
    /// el seed test data y el script SQL de cutover (B.6) lo populan.
    /// El frontend lo consume en <c>/api/auth/me</c> para filtrar la
    /// bandeja por defecto. Si emerge cardinalidad N:M en UAT, additive:
    /// mantener este campo como "primario" + tabla
    /// <c>UsuarioDepartamento</c>.
    /// </summary>
    public Guid? DepartamentoId { get; private set; }

    /// <summary>
    /// Estado de la cuenta respecto de Entra ID (alta unificada,
    /// F1-ADM-01 plan 15). Ver <see cref="Domain.EstadoAcceso"/>.
    /// </summary>
    public EstadoAcceso EstadoAcceso { get; private set; } = EstadoAcceso.Activo;

    /// <summary>Primer inicio de sesión registrado; null si nunca ha entrado.</summary>
    public DateTimeOffset? PrimerAccesoEn { get; private set; }

    /// <summary>
    /// Último envío del correo de acceso con contraseña temporal (camino B y
    /// "Reenviar acceso", F4). Solo la fecha: la contraseña nunca se guarda.
    /// </summary>
    public DateTimeOffset? AccesoEnviadoEn { get; private set; }

    /// <summary>Motivo del último rechazo de Graph mientras está en <see cref="EstadoAcceso.ErrorProvision"/>.</summary>
    public string? MotivoErrorProvision { get; private set; }

    /// <summary>
    /// Contraseña temporal asignada mientras la cuenta está en <see cref="EstadoAcceso.PendientePrimerAcceso"/>.
    /// Se borra automáticamente al registrar el primer acceso (<see cref="PrimerAccesoEn"/>).
    /// </summary>
    public string? ContrasenaTemporal { get; private set; }

    /// <summary>
    /// Cuenta que no corresponde a un empleado (super-admin de bootstrap,
    /// soporte, QA). Es la única forma legítima de tener Usuario sin
    /// Empleado vinculado (decisión D4 del plan 15).
    /// </summary>
    public bool EsCuentaTecnica { get; private set; }

    /// <summary>
    /// Prefijo del OID provisional mientras la cuenta de Entra se crea o
    /// aún no se resuelve (alta unificada).
    /// </summary>
    public const string PrefijoOidPendiente = "pending:";

    /// <summary>
    /// Prefijo del placeholder histórico de <c>CrearUsuarioCommand</c>
    /// (<c>dev-{email}</c>). Solo cuenta como pendiente si trae '@', para
    /// no confundirlo con los OIDs sintéticos de ADR-0015 como
    /// <c>dev-superadmin</c>, que sí son la identidad real en modo fake.
    /// </summary>
    public const string PrefijoOidDevPorEmail = "dev-";

    public const int MotivoErrorProvisionMaxLength = 500;

    private Usuario() { } // EF Core

    public Usuario(
        Guid id,
        string entraOid,
        string email,
        string nombre,
        EstadoAcceso estadoAcceso = EstadoAcceso.Activo,
        bool esCuentaTecnica = false) : base(id)
    {
        if (string.IsNullOrWhiteSpace(entraOid))
            throw new ArgumentException("EntraOid es requerido.", nameof(entraOid));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email es requerido.", nameof(email));
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("Nombre es requerido.", nameof(nombre));
        if (estadoAcceso is EstadoAcceso.ProvisionandoCuenta && !EsOidPendiente(entraOid))
            throw new BusinessRuleException("USUARIO_OID_PROVISION_INVALIDO",
                "Una cuenta en provisión debe nacer con OID pendiente.");
        if (estadoAcceso is EstadoAcceso.ErrorProvision)
            throw new BusinessRuleException("USUARIO_ESTADO_INICIAL_INVALIDO",
                "Un usuario no puede nacer en error de provisión.");

        EntraOid = entraOid;
        Email = email;
        Nombre = nombre;
        EstadoAcceso = estadoAcceso;
        EsCuentaTecnica = esCuentaTecnica;
    }

    /// <summary>True si el OID aún no es el definitivo de Entra ID.</summary>
    public bool TieneOidPendiente => EsOidPendiente(EntraOid);

    public static bool EsOidPendiente(string entraOid) =>
        entraOid.StartsWith(PrefijoOidPendiente, StringComparison.Ordinal)
        || (entraOid.StartsWith(PrefijoOidDevPorEmail, StringComparison.Ordinal)
            && entraOid.Contains('@'));

    /// <summary>
    /// Sustituye el OID pendiente por el real de Entra ID: lo usa el worker
    /// de provisión al crear la cuenta vía Graph y el login al vincular
    /// por email. Idempotente si ya tiene ese mismo OID. Deja la cuenta en
    /// <see cref="EstadoAcceso.PendientePrimerAcceso"/> salvo que ya
    /// hubiera entrado.
    /// </summary>
    public void VincularEntraOid(string entraOid)
    {
        if (string.IsNullOrWhiteSpace(entraOid) || EsOidPendiente(entraOid))
            throw new BusinessRuleException("USUARIO_OID_INVALIDO",
                "El OID a vincular debe ser el definitivo de Entra ID.");
        if (EntraOid == entraOid) return;
        if (!TieneOidPendiente)
            throw new BusinessRuleException("USUARIO_OID_YA_VINCULADO",
                "El usuario ya está vinculado a otra cuenta de Entra ID.");

        EntraOid = entraOid;
        MotivoErrorProvision = null;
        if (EstadoAcceso is not EstadoAcceso.Activo)
            EstadoAcceso = EstadoAcceso.PendientePrimerAcceso;
    }

    /// <summary>
    /// Pide al worker de provisión que cree la cuenta en Entra (camino B
    /// del alta unificada). Solo para un usuario recién dado de alta con
    /// OID pendiente que todavía no ha entrado.
    /// </summary>
    public void IniciarProvision()
    {
        if (!TieneOidPendiente || EstadoAcceso is not EstadoAcceso.PendientePrimerAcceso)
            throw new BusinessRuleException("USUARIO_NO_PROVISIONABLE",
                "Solo un usuario con OID pendiente que no ha iniciado sesión puede pedir su cuenta en Entra.");

        EstadoAcceso = EstadoAcceso.ProvisionandoCuenta;
        MotivoErrorProvision = null;
    }

    /// <summary>
    /// Se envió el correo de acceso con una contraseña temporal. Solo
    /// aplica a una cuenta ya creada en Entra que todavía no ha entrado.
    /// Si se proporciona la contraseña, se almacena temporalmente hasta el primer acceso.
    /// </summary>
    public void RegistrarEnvioAcceso(DateTimeOffset cuando, string? contrasena = null)
    {
        if (TieneOidPendiente || EstadoAcceso is not EstadoAcceso.PendientePrimerAcceso)
            throw new BusinessRuleException("USUARIO_ACCESO_NO_REENVIABLE",
                "Solo se envía el acceso a una cuenta creada en Entra que aún no inicia sesión.");

        AccesoEnviadoEn = cuando;
        if (!string.IsNullOrWhiteSpace(contrasena))
        {
            ContrasenaTemporal = contrasena;
        }
    }

    /// <summary>
    /// Guarda la contraseña temporal emitida por Entra ID para reenviarla o mostrarla al administrador.
    /// </summary>
    public void GuardarContrasenaTemporal(string contrasena)
    {
        if (string.IsNullOrWhiteSpace(contrasena))
            throw new ArgumentException("La contraseña temporal no puede estar vacía.", nameof(contrasena));
        ContrasenaTemporal = contrasena;
    }

    /// <summary>
    /// Limpia la contraseña temporal manualmente si es requerido.
    /// </summary>
    public void LimpiarContrasenaTemporal()
    {
        ContrasenaTemporal = null;
    }

    /// <summary>Graph rechazó la creación de la cuenta.</summary>
    public void MarcarErrorProvision(string motivo)
    {
        if (EstadoAcceso is not (EstadoAcceso.ProvisionandoCuenta or EstadoAcceso.ErrorProvision))
            throw new BusinessRuleException("USUARIO_NO_EN_PROVISION",
                "Solo una cuenta en provisión puede marcarse con error.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("USUARIO_MOTIVO_ERROR_REQUERIDO",
                "El motivo del error de provisión es requerido.");

        EstadoAcceso = EstadoAcceso.ErrorProvision;
        MotivoErrorProvision = motivo.Length > MotivoErrorProvisionMaxLength
            ? motivo[..MotivoErrorProvisionMaxLength]
            : motivo;
    }

    /// <summary>El admin corrigió la causa y vuelve a pedir la cuenta.</summary>
    public void ReintentarProvision()
    {
        if (EstadoAcceso is not EstadoAcceso.ErrorProvision)
            throw new BusinessRuleException("USUARIO_NO_EN_ERROR_PROVISION",
                "Solo se puede reintentar una provisión que falló.");

        EstadoAcceso = EstadoAcceso.ProvisionandoCuenta;
        MotivoErrorProvision = null;
    }

    /// <summary>
    /// Registra el inicio de sesión. La primera vez fija
    /// <see cref="PrimerAccesoEn"/>, pasa a <see cref="EstadoAcceso.Activo"/>
    /// y elimina cualquier contraseña temporal almacenada; después es no-op.
    /// </summary>
    public void RegistrarAcceso(DateTimeOffset cuando)
    {
        if (TieneOidPendiente)
            throw new BusinessRuleException("USUARIO_OID_PENDIENTE",
                "No se puede registrar el acceso de una cuenta sin OID de Entra ID.");

        PrimerAccesoEn ??= cuando;
        EstadoAcceso = EstadoAcceso.Activo;
        ContrasenaTemporal = null;
    }

    public void MarcarComoCuentaTecnica() => EsCuentaTecnica = true;

    public void DesmarcarCuentaTecnica() => EsCuentaTecnica = false;

    /// <summary>
    /// PATCH parcial sobre los campos editables del perfil
    /// (F-Admin-PR4.1). Convención del repo:
    /// <list type="bullet">
    ///   <item>Parámetros <c>null</c> ⇒ no tocar.</item>
    ///   <item>Para limpiar <see cref="DepartamentoId"/> a <c>null</c> el
    ///         caller pasa <paramref name="limpiarDepartamento"/> = true
    ///         (mismo patrón que <c>ActualizarRolCommand</c>).</item>
    /// </list>
    /// La cross-entity validation de unicidad de email vive en el handler.
    /// </summary>
    public void ActualizarPerfil(
        string? email,
        string? nombre,
        Guid? departamentoId,
        bool limpiarDepartamento)
    {
        if (email is not null)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email es requerido.", nameof(email));
            Email = email;
        }

        if (nombre is not null)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("Nombre es requerido.", nameof(nombre));
            Nombre = nombre;
        }

        if (limpiarDepartamento)
        {
            DepartamentoId = null;
        }
        else if (departamentoId is Guid d)
        {
            DepartamentoId = d;
        }
    }

    /// <summary>
    /// Asigna o limpia el departamento "primario". Visible para seed y
    /// script SQL de cutover; cuando llegue endpoint admin post-v1, será
    /// el único caller productivo.
    /// </summary>
    public void AsignarDepartamento(Guid? departamentoId) => DepartamentoId = departamentoId;

    /// <summary>
    /// Desactiva al usuario (soft delete: <c>Activo=false</c>). No valida
    /// invariantes cross-entity (ej. "último super-admin") — esa lógica
    /// vive en <c>DesactivarUsuarioCommand</c> donde hay acceso a las
    /// asignaciones <see cref="UsuarioEmpresaRol"/>.
    /// </summary>
    public void Desactivar() => Activo = false;

    public void Reactivar() => Activo = true;

    /// <summary>
    /// Factory que crea una asignación <see cref="UsuarioEmpresaRol"/>
    /// entre este usuario, una empresa y un rol (F-Admin-PR4.1). No
    /// persiste — el handler que llama esta factory hace el
    /// <c>Add</c> + <c>SaveChangesAsync</c>. Unique idx
    /// <c>(UsuarioId, EmpresaId, RolId)</c> en BD enforce no-duplicado.
    /// </summary>
    public UsuarioEmpresaRol AsignarRolEnEmpresa(
        Guid empresaId,
        Guid rolId,
        Guid? asignadoPorUsuarioId) =>
        new(Guid.CreateVersion7(), Id, empresaId, rolId, asignadoPorUsuarioId);
}
