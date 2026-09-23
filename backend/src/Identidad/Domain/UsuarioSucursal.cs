using Millet.Catalogos.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Identidad.Domain;

/// <summary>
/// Asignación N:M entre <see cref="Usuario"/> y
/// <c>Millet.Administracion.Domain.Sucursal</c> (<c>compartido.sucursales</c>).
/// Tabla <c>identidad.usuario_sucursales</c>. Modela el scoping de un
/// usuario a las sucursales donde puede operar (F1-ADM-01).
///
/// <para>
/// Vive en el módulo Identidad (NO Compartido) siguiendo el precedente de
/// <see cref="UsuarioServicio"/>: relaciones "Usuario ↔ scope" son dueñas
/// de Identidad, con FK física cross-schema hacia <c>compartido.*</c>.
/// </para>
///
/// <para>
/// <see cref="EmpresaId"/> es un dato de consistencia (no se valida aquí):
/// el invariante de negocio "la sucursal debe pertenecer a una empresa
/// donde el usuario tiene rol vía <see cref="UsuarioEmpresaRol"/>" se
/// valida en el Command handler (Fase 2), que sí tiene acceso a ambas
/// tablas vía el DbContext. Este constructor sólo valida no-empty.
/// </para>
///
/// <para>
/// La unicidad se enforce con UNIQUE en <c>(UsuarioId, SucursalId)</c> —
/// surrogate <see cref="BaseEntity.Id"/> sirve sólo como PK física para
/// auditoría y compatibilidad con <c>BaseDbContext</c>.
/// </para>
/// </summary>
public sealed class UsuarioSucursal : BaseEntity, IAuditable
{
    public Guid UsuarioId { get; private set; }
    public Guid SucursalId { get; private set; }
    public Guid EmpresaId { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private UsuarioSucursal() { } // EF Core

    public UsuarioSucursal(
        Guid id,
        Guid usuarioId,
        Guid sucursalId,
        Guid empresaId,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El id es obligatorio.", nameof(id));
        if (usuarioId == Guid.Empty)
            throw new ArgumentException("UsuarioId es obligatorio.", nameof(usuarioId));
        if (sucursalId == Guid.Empty)
            throw new ArgumentException("SucursalId es obligatorio.", nameof(sucursalId));
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId es obligatorio.", nameof(empresaId));

        UsuarioId = usuarioId;
        SucursalId = sucursalId;
        EmpresaId = empresaId;
        Estatus = estatus;
    }

    /// <summary>
    /// Reactiva una asignación (<see cref="EstatusCatalogo.Activo"/>).
    /// Idempotente.
    /// </summary>
    public void Activar() => Estatus = EstatusCatalogo.Activo;

    /// <summary>
    /// Desactiva la asignación (<see cref="EstatusCatalogo.Inactivo"/>).
    /// Idempotente.
    /// </summary>
    public void Desactivar() => Estatus = EstatusCatalogo.Inactivo;
}
