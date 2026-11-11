namespace Millet.Compras.Application.PublicPorts;

public sealed record ContenedorImportacionCeLectura(string? Numero, string? BlGuia, int? Modalidad = null);
public sealed record TransporteImportacionCeLectura(string? NumeroPedimento, IReadOnlyList<ContenedorImportacionCeLectura> Contenedores);
public interface ITransporteImportacionCeReadPort
{
    Task<TransporteImportacionCeLectura?> ObtenerAsync(Guid ocId, CancellationToken ct);
}
