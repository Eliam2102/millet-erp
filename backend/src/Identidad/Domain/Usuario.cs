using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
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

    private Usuario() { } // EF Core

    public Usuario(Guid id, string entraOid, string email, string nombre) : base(id)
    {
        if (string.IsNullOrWhiteSpace(entraOid))
            throw new ArgumentException("EntraOid es requerido.", nameof(entraOid));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email es requerido.", nameof(email));
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("Nombre es requerido.", nameof(nombre));

        EntraOid = entraOid;
        Email = email;
        Nombre = nombre;
    }

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
