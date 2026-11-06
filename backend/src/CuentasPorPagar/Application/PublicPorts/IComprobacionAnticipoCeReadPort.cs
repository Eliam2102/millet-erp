namespace Millet.CuentasPorPagar.Application.PublicPorts;
public interface IComprobacionAnticipoCeReadPort
{
    Task<IReadOnlyList<string>> ErroresAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
