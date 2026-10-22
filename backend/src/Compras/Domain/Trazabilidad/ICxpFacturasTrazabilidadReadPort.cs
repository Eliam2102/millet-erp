namespace Millet.Compras.Domain.Trazabilidad;

public interface ICxpFacturasTrazabilidadReadPort
{
    Task<IReadOnlyList<NodoArbolDocumento>> ObtenerPorReferenciaAsync(Guid? facturaId, Guid? cfdiId, CancellationToken ct);
    Task<IReadOnlyList<FacturaOcAnalisisLectura>> LeerAnalisisAsync(Guid empresa,
        IReadOnlyCollection<Guid> ordenesIds, DateOnly corte, CancellationToken ct);
    Task<Guid?> ObtenerCfdiIdAsync(Guid facturaId, CancellationToken ct);
}

public sealed record FacturaOcAnalisisLectura(Guid Id, Guid OrdenCompraId, string Moneda, decimal Total,
    decimal Saldo, IReadOnlyList<LineaFacturaOcAnalisisLectura> Lineas);
public sealed record LineaFacturaOcAnalisisLectura(Guid LineaOcId, decimal Cantidad, decimal ImporteNeto);
