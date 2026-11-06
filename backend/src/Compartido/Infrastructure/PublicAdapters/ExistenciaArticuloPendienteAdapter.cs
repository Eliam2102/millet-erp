using Millet.Almacen.Domain.Ports.Externos;
using Millet.Compartido.Application.Ports;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>Implementa <see cref="IPendientesMaestroReadPort"/> avisando si el artículo aún tiene existencia en almacén.</summary>
public sealed class ExistenciaArticuloPendienteAdapter(IAlmacenSaldoQueryPort saldos) : IPendientesMaestroReadPort
{
    public async Task<IReadOnlyList<string>> ObtenerAsync(TipoMaestro tipo, Guid id, CancellationToken cancellationToken)
        => tipo == TipoMaestro.Articulo && await saldos.ArticuloTieneExistenciaAsync(id, cancellationToken)
            ? ["Tiene existencia en almacén."]
            : [];
}
