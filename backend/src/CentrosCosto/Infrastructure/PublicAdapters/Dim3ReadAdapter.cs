using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.CentrosCosto.Infrastructure.Persistence;

namespace Millet.CentrosCosto.Infrastructure.PublicAdapters;

/// <remarks>
/// ADM08 / ADR-0062: el nombre histórico Dim3 se conserva por compatibilidad;
/// este contrato admite centros Dim1/Dim2 y máquinas Dim3.
/// </remarks>
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

        var nodos = await CentroCostoCatalogoLectura.ObtenerAsync(_db, cancellationToken);
        return nodos.Where(x => ids.Contains(x.Id)).ToDictionary(x => x.Id,
            x => new Dim3Lectura(x.Id, x.Clave, x.Nombre, x.Activo));
    }
}
