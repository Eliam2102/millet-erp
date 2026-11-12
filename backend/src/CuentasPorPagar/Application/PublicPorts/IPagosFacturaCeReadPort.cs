namespace Millet.CuentasPorPagar.Application.PublicPorts;
public interface IPagosFacturaCeReadPort
{
    Task<IReadOnlyDictionary<Guid, decimal>> ImportesVigentesAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<DateOnly?> UltimoPagoAsync(Guid facturaId, CancellationToken ct);
}
