namespace Millet.CuentasPorPagar.Application.PublicPorts;
public interface IPagosFacturaCeReadPort
{
    Task<DateOnly?> UltimoPagoAsync(Guid facturaId, CancellationToken ct);
}
