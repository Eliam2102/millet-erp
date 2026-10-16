using Millet.SharedKernel.Domain;

namespace Millet.ComercioExterior.Domain;

/// <summary>Una fila por pedido y expediente; K5 se impone en PostgreSQL.</summary>
public sealed class VinculoPedidoExpediente : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid PedidoFacturableId { get; private set; }
    public bool Activo { get; private set; } = true;
    public Guid AggregateRootId => ExpedienteId;
    private VinculoPedidoExpediente() { }
    internal VinculoPedidoExpediente(Guid empresa, Guid expediente, Guid pedido) : base(Guid.CreateVersion7())
    { EmpresaId = empresa; ExpedienteId = expediente; PedidoFacturableId = pedido; }
    internal void Liberar() => Activo = false;
    internal void Activar() => Activo = true;
}
