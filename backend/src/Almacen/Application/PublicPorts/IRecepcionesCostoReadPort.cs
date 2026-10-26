namespace Millet.Almacen.Application.PublicPorts;
public sealed record RecepcionCostoLectura(Guid PartidaId, Guid OcId, Guid RecepcionId, Guid ArticuloId, Guid UbicacionId,
    string Descripcion, decimal Cantidad, decimal Remanente, decimal ExistenciaTotal, decimal CostoPromedioMxn);
public interface IRecepcionesCostoReadPort
{
    Task<IReadOnlyList<RecepcionCostoLectura>> ListarAsync(Guid empresa, Guid sucursal, Guid oc, CancellationToken ct);
}
