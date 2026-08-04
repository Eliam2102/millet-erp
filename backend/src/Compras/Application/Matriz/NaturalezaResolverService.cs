using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compras.Application.Matriz;

/// <summary>
/// Resuelve la naturaleza "más restrictiva" entre las líneas de una
/// requisición consultando <c>compartido.articulos</c> (F9-PR2,
/// §3.bis.2). "Más restrictiva" se define por orden del enum
/// <see cref="Naturaleza"/>: <c>Riesgo (3) &gt; Critico (2) &gt;
/// Servicio (1) &gt; Estandar (0)</c>.
///
/// <para>
/// Una sola query a <c>compartido.articulos</c> con <c>WHERE id IN
/// (...)</c> resuelve toda la requisición. Si una línea apunta a un
/// artículo que ya no existe en el catálogo (caso patológico — la
/// validación cross-table de F7-PR1 lo previene al crear/editar
/// líneas), se asume <c>Estandar</c> para no bloquear la autorización.
/// </para>
/// </summary>
public sealed class NaturalezaResolverService
{
    private readonly CompartidoDbContext _compartido;

    public NaturalezaResolverService(CompartidoDbContext compartido)
    {
        _compartido = compartido;
    }

    public async Task<Naturaleza> ResolverMasRestrictivaAsync(
        Requisicion requisicion,
        CancellationToken cancellationToken = default)
    {
        if (requisicion.Lineas.Count == 0)
        {
            // Sin líneas no debería llegarse a evaluar (TRANSMITIR_SIN_LINEAS
            // 422 lo bloquea), pero defensivo.
            return Naturaleza.Estandar;
        }

        var articuloIds = requisicion.Lineas
            .Select(l => l.ArticuloId)
            .Distinct()
            .ToArray();

        // Single query con MAX agregado: el planner usa el index de pk.
        var maxNaturaleza = await _compartido.Articulos
            .AsNoTracking()
            .Where(a => articuloIds.Contains(a.Id))
            .Select(a => (Naturaleza?)a.Naturaleza)
            .MaxAsync(cancellationToken);

        return maxNaturaleza ?? Naturaleza.Estandar;
    }
}
