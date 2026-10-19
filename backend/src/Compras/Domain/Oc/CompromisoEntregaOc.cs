using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Oc;

/// <summary>Fecha prometida por el proveedor para una cantidad de una línea (parcialidad).</summary>
public sealed record CompromisoEntregaPropuesto(Guid LineaOrdenCompraId, decimal Cantidad, DateOnly FechaPrometida);

/// <summary>Compromiso de entrega persistido dentro de una respuesta del proveedor (inmutable).</summary>
public sealed class CompromisoEntregaOc : BaseEntity, IAuditable, IBelongsToAggregate
{
    public Guid RespuestaProveedorOcId { get; private set; }
    public Guid OrdenCompraId { get; private set; }
    public Guid AggregateRootId => OrdenCompraId;
    public Guid LineaOrdenCompraId { get; private set; }
    public decimal Cantidad { get; private set; }
    public DateOnly FechaPrometida { get; private set; }

    private CompromisoEntregaOc() { }

    internal CompromisoEntregaOc(Guid id, Guid respuestaId, Guid ordenCompraId, Guid lineaId,
        decimal cantidad, DateOnly fechaPrometida) : base(id)
    {
        RespuestaProveedorOcId = respuestaId;
        OrdenCompraId = ordenCompraId;
        LineaOrdenCompraId = lineaId;
        Cantidad = cantidad;
        FechaPrometida = fechaPrometida;
    }
}
