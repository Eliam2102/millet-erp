namespace Millet.Almacen.Application.PublicPorts;
public sealed record RecepcionCostoLectura(Guid PartidaId, Guid OcId, Guid RecepcionId, Guid ArticuloId, Guid UbicacionId,
    string Descripcion, decimal Cantidad, decimal Remanente, decimal ExistenciaTotal, decimal CostoPromedioMxn);
public interface IRecepcionesCostoReadPort
{
    Task<IReadOnlyList<MovimientoOcLectura>> LeerMovimientosAsync(Guid empresa,
        IReadOnlyCollection<PartidaOcLectura> partidas, DateOnly corte, CancellationToken ct);
    Task<IReadOnlyList<RecepcionCostoLectura>> ListarAsync(Guid empresa, Guid sucursal, Guid oc, CancellationToken ct);
}

public sealed record PartidaOcLectura(Guid OrdenCompraId, Guid LineaId, Guid ArticuloId, string Unidad);
public sealed record MovimientoOcLectura(Guid LineaOcId, Guid DocumentoId, string Folio,
    DateOnly Fecha, decimal Cantidad, bool EsDevolucion);
