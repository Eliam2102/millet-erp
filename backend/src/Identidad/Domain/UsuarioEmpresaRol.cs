using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Identidad.Domain;

/// <summary>
/// Asignación de un <see cref="Rol"/> a un <see cref="Usuario"/> dentro del
/// scope de una empresa específica. Único multi-tenant del módulo Identidad
/// — implementa <see cref="IPerteneceAEmpresa"/> para que el global query
/// filter del <c>BaseDbContext</c> aplique automáticamente cuando un usuario
/// pregunte por sus roles en la empresa actual.
///
/// Unique index en (UsuarioId, EmpresaId, RolId) impide duplicados.
/// Ver ADR-0007 y ADR-0011.
/// </summary>
public sealed class UsuarioEmpresaRol : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid UsuarioId { get; private set; }
    public Guid EmpresaId { get; set; } // public set requerido por IPerteneceAEmpresa
    public Guid RolId { get; private set; }

    /// <summary>
    /// Quién hizo la asignación. <c>null</c> cuando fue el bootstrap del
    /// sistema (ej. el primer SuperAdmin no tiene asignador humano).
    /// </summary>
    public Guid? AsignadoPorUsuarioId { get; private set; }

    private UsuarioEmpresaRol() { } // EF Core

    public UsuarioEmpresaRol(
        Guid id,
        Guid usuarioId,
        Guid empresaId,
        Guid rolId,
        Guid? asignadoPorUsuarioId = null) : base(id)
    {
        if (usuarioId == Guid.Empty)
            throw new ArgumentException("UsuarioId es requerido.", nameof(usuarioId));
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId es requerido.", nameof(empresaId));
        if (rolId == Guid.Empty)
            throw new ArgumentException("RolId es requerido.", nameof(rolId));

        UsuarioId = usuarioId;
        EmpresaId = empresaId;
        RolId = rolId;
        AsignadoPorUsuarioId = asignadoPorUsuarioId;
    }
}
