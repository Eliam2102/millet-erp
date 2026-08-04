namespace Millet.SharedKernel.Domain;

/// <summary>
/// Marca una entidad como hija de un agregado raíz, para que el
/// <c>AuditSaveChangesInterceptor</c> capture el id del root junto con
/// la fila de audit (B.2). Esto permite consultar el histórico de
/// auditoría de un agregado completo (root + hijos) con un solo
/// <c>WHERE aggregate_root_id = X</c> en lugar de joins o queries
/// múltiples.
///
/// <para>
/// Las entidades que NO implementan esta interface se asumen como
/// aggregate roots por convención: <c>AggregateRootId = Id</c>. Solo
/// hay que implementar <c>IBelongsToAggregate</c> en las hijas (ej.
/// <c>LineaRequisicion</c> retorna <c>RequisicionId</c>;
/// <c>Autorizacion</c> también).
/// </para>
/// </summary>
public interface IBelongsToAggregate
{
    /// <summary>
    /// Id del agregado raíz al que pertenece esta entidad.
    /// </summary>
    Guid AggregateRootId { get; }
}
