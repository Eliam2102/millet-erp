using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Identidad.Domain;

/// <summary>
/// Relación M:N entre <see cref="Rol"/> y <see cref="Permiso"/>. Junction
/// global (no por empresa); la asignación de roles a usuarios por empresa
/// vive en <see cref="UsuarioEmpresaRol"/>.
/// Unique index en (RolId, PermisoId) garantiza unicidad.
/// </summary>
public sealed class RolPermiso : BaseEntity, IAuditable
{
    public Guid RolId { get; private set; }
    public Guid PermisoId { get; private set; }

    private RolPermiso() { } // EF Core

    public RolPermiso(Guid id, Guid rolId, Guid permisoId) : base(id)
    {
        if (rolId == Guid.Empty)
            throw new ArgumentException("RolId es requerido.", nameof(rolId));
        if (permisoId == Guid.Empty)
            throw new ArgumentException("PermisoId es requerido.", nameof(permisoId));

        RolId = rolId;
        PermisoId = permisoId;
    }
}
