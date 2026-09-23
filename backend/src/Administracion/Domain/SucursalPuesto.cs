using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Asignación N:M entre <see cref="Sucursal"/> y <see cref="Puesto"/>.
/// Tabla <c>compartido.sucursal_puestos</c>. Modela el hecho de que un
/// puesto opera (o no) en una sucursal dada — mismo patrón que
/// <see cref="SucursalDepartamento"/>, análogo exacto pero para puestos.
///
/// <para>
/// F1-ADM-01: catálogo por empresa vía <see cref="EmpresaId"/> +
/// <see cref="IPerteneceAEmpresa"/> — mismo patrón que <see cref="Sucursal"/>
/// y <see cref="Puesto"/>. Invariante de negocio: el <see cref="EmpresaId"/>
/// de esta asignación debe coincidir con el <c>EmpresaId</c> de la
/// <see cref="Sucursal"/> y del <see cref="Puesto"/> que vincula. Este
/// constructor NO recibe las entidades completas (solo Guids), así que
/// esa validación cross-entity vive en el handler correspondiente
/// (Fase 2) donde sí hay acceso a ambas filas vía el DbContext.
/// </para>
///
/// <para>
/// La unicidad se enforce con UNIQUE en <c>(SucursalId, PuestoId)</c> —
/// surrogate <see cref="BaseEntity.Id"/> sirve sólo como PK física para
/// auditoría y compatibilidad con <c>BaseDbContext</c>. Mismo patrón que
/// <c>SucursalDepartamento</c>.
/// </para>
/// </summary>
public sealed class SucursalPuesto : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; } // public set requerido por IPerteneceAEmpresa

    public Guid SucursalId { get; private set; }
    public Guid PuestoId { get; private set; }
    public Guid DepartamentoId { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private SucursalPuesto() { }

    public SucursalPuesto(
        Guid id,
        Guid empresaId,
        Guid sucursalId,
        Guid puestoId,
        Guid departamentoId,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("SUCURSAL_PUESTO_ID_INVALIDO",
                "El id es obligatorio.");
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("SUCURSAL_PUESTO_EMPRESA_INVALIDA",
                "La empresa es obligatoria.");
        if (sucursalId == Guid.Empty)
            throw new BusinessRuleException("SUCURSAL_PUESTO_SUCURSAL_INVALIDA",
                "SucursalId es obligatorio.");
        if (puestoId == Guid.Empty)
            throw new BusinessRuleException("SUCURSAL_PUESTO_PUESTO_INVALIDO",
                "PuestoId es obligatorio.");
        if (departamentoId == Guid.Empty)
            throw new BusinessRuleException("SUCURSAL_PUESTO_DEPARTAMENTO_INVALIDO",
                "DepartamentoId es obligatorio.");

        EmpresaId = empresaId;
        SucursalId = sucursalId;
        PuestoId = puestoId;
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
    /// Mismo patrón que <see cref="SucursalDepartamento.Desactivar"/>.
    /// </summary>
    public void Desactivar() => Estatus = EstatusCatalogo.Inactivo;

    /// <summary>
    /// Mueve a <see cref="EstatusCatalogo.EnRevision"/> para flujos
    /// internos. El endpoint público sólo expone Activar/Desactivar.
    /// </summary>
    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;
}
