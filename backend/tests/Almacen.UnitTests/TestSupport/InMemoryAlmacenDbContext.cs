using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.TestSupport;

/// <summary>
/// Almacén-por-línea PR6a: DbContext de test sobre EF InMemory. Le da a la
/// entidad keyless <see cref="MovimientoSubAlmacen"/> una <c>ToInMemoryQuery</c>
/// con la misma derivación que en PostgreSQL hace la vista
/// <c>v_movimiento_sub_almacen</c> (línea → ubicación → sub-almacén, primera
/// línea por movimiento). Así los lectores que consultan la vista funcionan en
/// unit tests sin meter el paquete InMemory a producción.
///
/// <para>Usa opciones tipadas a sí mismo (<c>DbContextOptions&lt;InMemoryAlmacenDbContext&gt;</c>)
/// vía el ctor protegido no-genérico de <see cref="AlmacenDbContext"/>, para
/// pasar la validación de tipo de contexto de EF.</para>
/// </summary>
public sealed class InMemoryAlmacenDbContext : AlmacenDbContext
{
    public InMemoryAlmacenDbContext(
        DbContextOptions<InMemoryAlmacenDbContext> options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    protected override void OnModelCreatingProviderSpecific(ModelBuilder modelBuilder)
    {
        // Una fila por movimiento vía Join + Distinct (no GroupBy/First: el
        // translator de InMemory no traduce el agregado ordenado dentro de la
        // proyección). Como todas las líneas de un movimiento comparten sub
        // (invariante MOVIMIENTO_MULTI_SUBALMACEN), el par (movimiento, sub) es
        // único → Distinct da exactamente una fila, igual que el DISTINCT ON de
        // la vista en PostgreSQL.
        modelBuilder.Entity<MovimientoSubAlmacen>().ToInMemoryQuery(() =>
            Set<LineaMovimiento>()
                .Join(Set<Ubicacion>(),
                      l => l.UbicacionId, u => (Guid?)u.Id,
                      (l, u) => new { l.MovimientoId, u.SubAlmacenId })
                .Distinct()
                .Select(x => new MovimientoSubAlmacen(x.MovimientoId, x.SubAlmacenId)));
    }
}
