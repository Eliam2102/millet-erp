using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Identidad.Domain;

/// <summary>
/// Conjunto nombrado de permisos. Los roles son globales (no por empresa);
/// la asignación a usuarios sí es por empresa, ver <see cref="UsuarioEmpresaRol"/>.
/// Los roles marcados como <c>EsDelSistema</c> (ej. super-admin) no se pueden
/// editar ni eliminar — solo el bootstrap los crea.
/// Ver ADR-0007.
/// </summary>
public sealed class Rol : BaseEntity, IAuditable
{
    public string Codigo { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public string? Descripcion { get; private set; }
    public bool EsDelSistema { get; private set; }
    public bool Activo { get; private set; } = true;

    private Rol() { } // EF Core

    public Rol(Guid id, string codigo, string nombre, bool esDelSistema = false, string? descripcion = null) : base(id)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ArgumentException("Codigo es requerido.", nameof(codigo));
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("Nombre es requerido.", nameof(nombre));

        Codigo = codigo;
        Nombre = nombre;
        Descripcion = descripcion;
        EsDelSistema = esDelSistema;
    }

    public void ActualizarMetadatos(string nombre, string? descripcion)
    {
        if (EsDelSistema)
            throw new InvalidOperationException("Los roles del sistema no se pueden editar.");
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("Nombre es requerido.", nameof(nombre));

        Nombre = nombre;
        Descripcion = descripcion;
    }

    public void Desactivar()
    {
        if (EsDelSistema)
            throw new InvalidOperationException("Los roles del sistema no se pueden desactivar.");
        Activo = false;
    }

    public void Reactivar() => Activo = true;

    /// <summary>
    /// Factory para crear la asociación entre este rol y un grupo de
    /// Microsoft Entra ID (F-Admin-PR3.1, A3=a). El handler que invoca
    /// este método persiste el <see cref="RolGrupoEntraId"/> retornado
    /// vía <c>IdentidadDbContext.RolGruposEntraId</c>. Unicidad
    /// <c>(RolId, ObjectId)</c> se enforce en el índice de BD.
    /// </summary>
    public RolGrupoEntraId AsociarGrupoEntraId(string objectId, string nombre) =>
        new(Guid.CreateVersion7(), Id, objectId, nombre);
}
