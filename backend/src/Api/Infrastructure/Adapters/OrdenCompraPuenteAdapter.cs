using Millet.Compras.Application.PublicPorts;
using Millet.Contabilidad.Application.CuentaPuente;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>K10.5: folio y proveedor de las OC de la cuenta puente, por el puerto público de lectura de Compras.</summary>
public sealed class OrdenCompraPuenteAdapter(IOcImportacionReadPort compras) : IOrdenCompraPuentePort
{
    public async Task<IReadOnlyDictionary<Guid, OrdenCompraPuente>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? new Dictionary<Guid, OrdenCompraPuente>()
            : (await compras.ObtenerVariasAsync(ids, ct)).ToDictionary(o => o.Id, o => new OrdenCompraPuente(o.Id, o.Folio, o.ProveedorId));
}
