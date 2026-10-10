namespace Millet.Compras.Domain.Trazabilidad;

public interface ICxpFacturasTrazabilidadReadPort
{
    Task<IReadOnlyList<NodoArbolDocumento>> ObtenerPorReferenciaAsync(Guid? facturaId, Guid? cfdiId, CancellationToken ct);
    Task<Guid?> ObtenerCfdiIdAsync(Guid facturaId, CancellationToken ct);
}
