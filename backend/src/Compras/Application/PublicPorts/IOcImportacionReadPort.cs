using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Application.PublicPorts;
public sealed record OcImportacionLectura(Guid Id, string Folio, Guid ProveedorId, string Moneda, Guid SucursalId, EstadoOrdenCompra Estado, bool EsImportacion, InformacionImportacion? InformacionImportacion);
/// <summary>Lectura pública de origen CE; no copia los datos de importación al expediente.</summary>
public interface IOcImportacionReadPort
{
    Task<OcImportacionLectura?> ObtenerAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<OcImportacionLectura>> ListarAsync(IReadOnlyList<Guid>? sucursales, string? q, CancellationToken ct);
}
