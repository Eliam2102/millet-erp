using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Application.Ports;

/// <summary>
/// Repositorio del agregado <see cref="EntidadExterna"/>. Encapsula
/// las queries comunes para reuso entre handlers y workers + mockeo
/// trivial en tests.
///
/// <para>
/// Todas las queries respetan el global query filter del
/// <c>BaseDbContext</c> (filtro por <see cref="EntidadExterna.EmpresaId"/>
/// = empresa del JWT actual). Si el caller necesita bypass (workers
/// de background, jobs cross-empresa), debe abrir el scope de bypass
/// del <c>ICurrentEmpresaContext</c> antes de llamar al método.
/// </para>
///
/// <para>
/// <b>PR #201 — limpieza:</b> retiradas
/// <c>GetPendientesDeCorrelacionAsync</c>,
/// <c>GetExpiradasAsync</c>,
/// <c>GetAllPendientesDeCorrelacionAsync</c>,
/// <c>GetAllExpiradasAsync</c> — consumidores únicos eran el polling
/// <c>AwCorrelationWorker</c> y su handler de expiración, ambos
/// eliminados al migrar a flow callback per-EDI.
/// </para>
/// </summary>
public interface IEntidadExternaRepository
{
    Task<EntidadExterna?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<EntidadExterna?> GetByReferenciaAsync(
        TipoEntidad tipoEntidad,
        string referenciaExterna,
        Guid empresaId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Carga una entidad por id sin filtrar por empresa. <b>Requiere
    /// bypass</b> activo. Usado por <c>AwDropWorker</c> para resolver el
    /// agregado del mensaje recibido del Service Bus (cross-empresa por
    /// diseño — el message pertenece a una empresa específica, encapsulada
    /// en el payload).
    /// </summary>
    Task<EntidadExterna?> GetByIdCrossEmpresaAsync(
        Guid id,
        CancellationToken cancellationToken);
}
