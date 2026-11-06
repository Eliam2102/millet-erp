namespace Millet.Facturacion.Domain.Anticipos;

/// <summary>
/// U-4 (86e3yzemw · flujo 3): constancia de que se liberó a mano un importe apartado (vinculado sin NC) del
/// anticipo: a qué factura estaba apartado, cuánto, quién, cuándo y por qué. Hija del agregado
/// <see cref="Anticipo"/>; inmutable. Tabla <c>facturacion.anticipo_liberacion</c>.
/// </summary>
public sealed class AnticipoLiberacion
{
    public Guid Id { get; private set; }
    public Guid AnticipoId { get; private set; }
    public Guid FacturaVentaId { get; private set; }
    public decimal Importe { get; private set; }
    public string Motivo { get; private set; } = string.Empty;
    public Guid LiberadoPorId { get; private set; }
    public string LiberadoPorNombre { get; private set; } = string.Empty;
    public DateTimeOffset LiberadoEn { get; private set; }

    private AnticipoLiberacion() { }

    internal AnticipoLiberacion(Guid id, Guid anticipoId, Guid facturaVentaId, decimal importe, string motivo,
        Guid liberadoPorId, string liberadoPorNombre, DateTimeOffset liberadoEn)
    {
        Id = id;
        AnticipoId = anticipoId;
        FacturaVentaId = facturaVentaId;
        Importe = importe;
        Motivo = motivo;
        LiberadoPorId = liberadoPorId;
        LiberadoPorNombre = liberadoPorNombre;
        LiberadoEn = liberadoEn;
    }
}
