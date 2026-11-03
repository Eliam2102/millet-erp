using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

public sealed class PedidoPtPosicion : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid PedidoPtId { get; private set; }
    public Guid AggregateRootId => PedidoPtId;
    public int Posicion { get; private set; }
    public Guid? ProductoId { get; private set; }
    public string Descripcion { get; private set; } = "";
    public int PiezasEsperadas { get; private set; }
    public bool CantidadNoEntera { get; private set; }

    private PedidoPtPosicion() { }

    internal PedidoPtPosicion(Guid empresa, Guid pedidoPtId, int posicion, Guid? productoId, string descripcion, int piezasEsperadas, bool cantidadNoEntera) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa;
        PedidoPtId = pedidoPtId;
        Posicion = posicion;
        Actualizar(productoId, descripcion, piezasEsperadas, cantidadNoEntera);
    }

    internal void Actualizar(Guid? productoId, string descripcion, int piezasEsperadas, bool cantidadNoEntera)
    {
        ProductoId = productoId;
        Descripcion = descripcion.Length > 500 ? descripcion[..500] : descripcion;
        PiezasEsperadas = piezasEsperadas;
        CantidadNoEntera = cantidadNoEntera;
    }
}
