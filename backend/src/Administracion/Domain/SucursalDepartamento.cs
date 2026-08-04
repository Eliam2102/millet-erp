using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Asignación N:M entre <see cref="Sucursal"/> y <see cref="Departamento"/>.
/// Tabla <c>compartido.sucursal_departamentos</c>. Modela el hecho de que
/// un departamento opera (o no) en una sucursal dada — la realidad
/// operativa de Millet donde una sucursal puede tener Sistemas y otra no.
///
/// <para>
/// Catálogo cross-empresa (sin <c>EmpresaId</c>), mismo patrón que
/// <see cref="Sucursal"/> y <see cref="Departamento"/>. La FK por
/// <see cref="SucursalId"/> y <see cref="DepartamentoId"/> apunta a las
/// tablas del mismo schema <c>compartido</c>.
/// </para>
///
/// <para>
/// La unicidad se enforce con UNIQUE en <c>(SucursalId, DepartamentoId)</c>
/// — surrogate <see cref="BaseEntity.Id"/> sirve sólo como PK física para
/// auditoría y compatibilidad con <c>BaseDbContext</c>. Mismo patrón que
/// <c>SecuenciaFolio</c>.
/// </para>
///
/// <para>
/// El consumidor de validación (Compras / Requisiciones, PR-A2) lee via
/// <c>ISucursalDepartamentoReadPort</c>; sólo las filas con
/// <see cref="EstatusCatalogo.Activo"/> habilitan creación de RQs.
/// </para>
/// </summary>
public sealed class SucursalDepartamento : BaseEntity, IAuditable
{
    public Guid SucursalId { get; private set; }
    public Guid DepartamentoId { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private SucursalDepartamento() { }

    public SucursalDepartamento(
        Guid id,
        Guid sucursalId,
        Guid departamentoId,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("SUCURSAL_DEPARTAMENTO_ID_INVALIDO",
                "El id es obligatorio.");
        if (sucursalId == Guid.Empty)
            throw new BusinessRuleException("SUCURSAL_DEPARTAMENTO_SUCURSAL_INVALIDA",
                "SucursalId es obligatorio.");
        if (departamentoId == Guid.Empty)
            throw new BusinessRuleException("SUCURSAL_DEPARTAMENTO_DEPARTAMENTO_INVALIDO",
                "DepartamentoId es obligatorio.");

        SucursalId = sucursalId;
        DepartamentoId = departamentoId;
        Estatus = estatus;
    }

    /// <summary>
    /// Reactiva una asignación (<see cref="EstatusCatalogo.Activo"/>).
    /// Idempotente.
    /// </summary>
    public void Activar() => Estatus = EstatusCatalogo.Activo;

    /// <summary>
    /// Desactiva la asignación (<see cref="EstatusCatalogo.Inactivo"/>).
    /// El consumidor PR-A2 (Compras) bloquea nuevas RQs con la combinación
    /// inactiva pero NO afecta RQs/OCs vivas, siguiendo el patrón de
    /// <see cref="Sucursal.Desactivar"/> y <see cref="Departamento.Desactivar"/>.
    /// </summary>
    public void Desactivar() => Estatus = EstatusCatalogo.Inactivo;

    /// <summary>
    /// Mueve a <see cref="EstatusCatalogo.EnRevision"/> para flujos
    /// internos. El endpoint público sólo expone Activar/Desactivar.
    /// </summary>
    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;
}
