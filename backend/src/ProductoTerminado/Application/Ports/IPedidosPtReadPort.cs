using Millet.ProductoTerminado.Domain;
namespace Millet.ProductoTerminado.Application.Ports;

public interface IPedidosPtReadPort
{
    Task<PtOrderSource?> GetAsync(Guid empresaId, string orderNumber, CancellationToken ct);
}
