using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>Pieza física de producto terminado. Sin estatus de A+W: PT solo conoce su propio estado (D7).</summary>
public sealed class PiezaPt : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid PedidoPtId { get; private set; }
    public Guid AggregateRootId => PedidoPtId;
    public int Posicion { get; private set; }
    public int NumeroPieza { get; private set; }
    public string Etiqueta { get; private set; } = "";
    public Guid SucursalId { get; private set; }
    public EstadoPieza Estado { get; private set; }
    public DateTimeOffset EscaneadaAt { get; private set; }
    public Guid EscaneoId { get; private set; }

    private PiezaPt() { }

    private PiezaPt(Guid empresa, Guid pedidoPtId, int posicion, int numeroPieza, string etiqueta, Guid sucursalId, DateTimeOffset escaneadaAt, Guid escaneoId) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa;
        PedidoPtId = pedidoPtId;
        Posicion = posicion;
        NumeroPieza = numeroPieza;
        Etiqueta = etiqueta.Trim();
        SucursalId = sucursalId;
        Estado = EstadoPieza.EnPT;
        EscaneadaAt = escaneadaAt;
        EscaneoId = escaneoId;
    }

    public static PiezaPt Alta(Guid empresa, PedidoPt pedido, int posicion, int numeroPieza, string etiqueta, Guid sucursalId, DateTimeOffset escaneadaAt, Guid escaneoId)
        => new(empresa, pedido.Id, posicion, numeroPieza, etiqueta, sucursalId, escaneadaAt, escaneoId);
}
