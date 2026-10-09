namespace Millet.Identidad.Application.Ports;

public interface IEntraGruposReadPort
{
    Task<IReadOnlyList<string>> ListarAsync(string objectId, CancellationToken ct);
}
