using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
namespace Millet.Contabilidad.Application.PublicPorts;
public interface IPolizaContableReadPort
{
    Task<PolizaContableLectura?> ObtenerAsync(Guid id, CancellationToken ct);
}
public sealed record PolizaContableLectura(Guid Id, EstadoPoliza Estado, IReadOnlyList<PartidaRequest> Partidas);
