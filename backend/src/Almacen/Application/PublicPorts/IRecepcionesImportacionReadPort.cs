namespace Millet.Almacen.Application.PublicPorts;
public sealed record PartidaImportacionLectura(Guid LineaOcId, string FraccionArancelaria);
public sealed record RecepcionImportacionLectura(Guid RecepcionId, string NumeroPedimento, string NumeroBl,
    string NumeroContenedor, string Aduana, string ClavePedimento, IReadOnlyList<PartidaImportacionLectura> Partidas);
public interface IRecepcionesImportacionReadPort
{
    Task<IReadOnlyList<RecepcionImportacionLectura>> ObtenerPorOcAsync(Guid ocId, CancellationToken ct);
}
