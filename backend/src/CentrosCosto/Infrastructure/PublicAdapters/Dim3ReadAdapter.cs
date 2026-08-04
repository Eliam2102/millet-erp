using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.CentrosCosto.Infrastructure.Persistence;

namespace Millet.CentrosCosto.Infrastructure.PublicAdapters;

/// <summary>
/// Adaptador productivo de <see cref="IDim3ReadPort"/>. CentrosCosto es el
/// owner del dato, así que el adaptador vive AQUÍ (no en el consumidor ni en
/// Compartido) — se registra en <c>AddCentrosCostoModule</c>. Lectura batch de
/// <c>centros_costo.dim3</c> con <c>AsNoTracking</c>; <b>SIN</b> filtro de
/// alcance e <b>incluyendo inactivas</b> (ADR-0050, ADR-0049): resolver el
/// nombre de una máquina que el documento ya trae no es elegirla.
/// </summary>
public sealed class Dim3ReadAdapter : IDim3ReadPort
{
    private readonly CentrosCostoDbContext _db;

    public Dim3ReadAdapter(CentrosCostoDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<Guid, Dim3Lectura>> ObtenerAsync(
        IReadOnlyCollection<Guid> dim3Ids,
        CancellationToken cancellationToken)
    {
        if (dim3Ids.Count == 0)
            return new Dictionary<Guid, Dim3Lectura>();

        var ids = dim3Ids.Distinct().ToArray();

        return await _db.Dim3s
            .AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .Select(d => new Dim3Lectura(
                d.Id, d.Clave, d.Nombre, d.Estatus == EstatusCatalogo.Activo))
            .ToDictionaryAsync(d => d.Id, cancellationToken);
    }
}
