using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Ingesta;

/// <summary>
/// Copia cruda e inmutable del pedido leído del origen externo, por trazabilidad
/// (§12.1 diseño). Sólo para orígenes externos (A+W/Planta Pintura); los
/// manuales no tienen snapshot. El cajero ve el snapshot pero puede refrescar
/// antes de timbrar.
/// </summary>
public sealed class PedidoFacturableSnapshot : BaseEntity, IBelongsToAggregate, IAuditable
{
    public Guid PedidoFacturableId { get; private set; }

    /// <summary>Payload crudo (jsonb) leído del origen al momento de la ingesta.</summary>
    public string PayloadCrudo { get; private set; } = string.Empty;

    public DateTimeOffset LeidoAt { get; private set; }

    public Guid AggregateRootId => PedidoFacturableId;

    private PedidoFacturableSnapshot() { }

    private PedidoFacturableSnapshot(Guid id, Guid pedidoFacturableId, string payloadCrudo, DateTimeOffset leidoAt) : base(id)
    {
        if (string.IsNullOrWhiteSpace(payloadCrudo))
            throw new BusinessRuleException("SNAPSHOT_PAYLOAD_VACIO", "El payload del snapshot es obligatorio.");

        PedidoFacturableId = pedidoFacturableId;
        PayloadCrudo = payloadCrudo;
        LeidoAt = leidoAt;
    }

    public static PedidoFacturableSnapshot Crear(Guid pedidoFacturableId, string payloadCrudo, DateTimeOffset leidoAt) =>
        new(Guid.CreateVersion7(), pedidoFacturableId, payloadCrudo, leidoAt);
}
