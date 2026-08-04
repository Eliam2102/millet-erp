using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Pedidos;

/// <summary>
/// Adapter de <see cref="ICanalVentaPorClaveAwResolver"/> sobre el catálogo
/// compartido de canales de venta (FAC-ING-PR2). Solo machea canales
/// activos: ingestar hacia un canal desactivado debe caer a la bandeja, no
/// pasar en silencio. Mismo patrón que <see cref="SucursalPorClaveAwResolver"/>;
/// el upper() de ambos lados corre en Postgres y respeta acentos (los GRUPPE
/// reales traen espacios y acentos, p.ej. "CC Mérida").
/// </summary>
public sealed class CanalVentaPorClaveAwResolver : ICanalVentaPorClaveAwResolver
{
    private readonly CompartidoDbContext _db;

    public CanalVentaPorClaveAwResolver(CompartidoDbContext db) => _db = db;

    public async Task<short?> ResolverAsync(
        string claveAw, CancellationToken cancellationToken)
    {
        var clave = claveAw.Trim().ToUpperInvariant();

        // ToUpper() dentro de la query se traduce a upper() en Postgres —
        // no corre en .NET, por eso se silencian los analyzers de cultura.
#pragma warning disable CA1304, CA1311, CA1862
        return await _db.CanalesVenta.AsNoTracking()
            .Where(c => c.ClaveAw != null
                     && c.ClaveAw.ToUpper() == clave
                     && c.Estatus == EstatusCatalogo.Activo)
            .Select(c => (short?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862
    }
}
