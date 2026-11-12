namespace Millet.Compras.Application.PublicPorts;

public interface IFacturaServicioCeReadPort
{
    Task<bool> TienePasivoVigenteAsync(Guid empresaId, Guid ordenCompraId, CancellationToken ct);
}
