using Millet.ComercioExterior.Domain;
namespace Millet.ComercioExterior.Application.Ports;
public sealed record OtrEntrada(string IdOrigen, long Version, OtrContenido Contenido, string? ErrorLectura = null, bool ErrorTecnico = false);
public interface IOtrEntradaReader
{
    Task<IReadOnlyList<OtrEntrada>> LeerPendientesAsync(int max, CancellationToken ct);
    Task MarcarResultadoAsync(string idOrigen, long version, string resultado, string? motivo, CancellationToken ct);
}
