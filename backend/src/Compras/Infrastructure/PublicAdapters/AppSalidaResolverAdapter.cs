using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter de <see cref="IAppSalidaResolverPort"/>: misma resolución que
/// <see cref="ComprasCrearRqAppAdapter"/> (sucursal por <c>Clave</c>/<c>ClaveAw</c>, artículo por
/// <c>ProductoAw.ReferenciaExterna</c>, medida por variante; la girada es la misma). Vive aquí porque
/// Almacén no puede referenciar Compartido; <c>Bypass()</c> acotado a las lecturas de maestros.
/// </summary>
public sealed class AppSalidaResolverAdapter : IAppSalidaResolverPort
{
    private readonly CompartidoDbContext _maestros;
    private readonly ICurrentEmpresaContext _empresa;

    public AppSalidaResolverAdapter(CompartidoDbContext maestros, ICurrentEmpresaContext empresa)
    {
        _maestros = maestros; _empresa = empresa;
    }

    public async Task<ResolverSalidaResultado> ResolverAsync(
        string sucursalCodigo, IReadOnlyList<GrupoSalidaApp> grupos, CancellationToken ct)
    {
        using var bypass = _empresa.Bypass();
        var suc = sucursalCodigo.Trim();
        var sucursalId = await _maestros.Sucursales.AsNoTracking()
            .Where(x => x.Clave == suc || x.ClaveAw == suc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (sucursalId is null) return Fallo($"No existe la sucursal '{suc}'.");

        var resueltos = new List<GrupoSalidaResuelto>(grupos.Count);
        foreach (var g in grupos)
        {
            var clave = g.ArticuloAw.Trim();
            var arts = await (from art in _maestros.Articulos.AsNoTracking()
                              join p in _maestros.ProductosAw.AsNoTracking() on art.ProductoAwId equals p.Id
                              where p.ReferenciaExterna == clave
                              select new { art.Id, art.UnidadMedidaDefault, ProductoAwId = p.Id }).ToListAsync(ct);
            if (arts.Count == 0) return Fallo($"No existe un artículo ligado al artículo A+W {clave}.");
            if (arts.Count > 1) return Fallo($"El artículo A+W {clave} está ligado a más de un artículo del ERP.");
            var articulo = arts[0];

            if (g.AnchoMm is null)
            {
                resueltos.Add(new(articulo.Id, articulo.UnidadMedidaDefault, null, null, null));
                continue;
            }
            var ancho = g.AnchoMm;
            var alto = g.AltoMm;
            var variante = await _maestros.ProductosAwVariantes.AsNoTracking()
                .Where(v => v.ProductoAwId == articulo.ProductoAwId
                    && ((v.AnchoMm == ancho && v.AltoMm == alto) || (v.AnchoMm == alto && v.AltoMm == ancho)))
                .Select(v => new { v.Id, v.AnchoMm, v.AltoMm }).FirstOrDefaultAsync(ct);
            if (variante is null)
                return Fallo($"No existe la medida {ancho:0.##}x{alto:0.##} del artículo A+W {clave}.");
            resueltos.Add(new(articulo.Id, articulo.UnidadMedidaDefault, variante.Id, variante.AnchoMm, variante.AltoMm));
        }
        return new(null, sucursalId.Value, resueltos);
    }

    private static ResolverSalidaResultado Fallo(string error) => new(error, Guid.Empty, []);
}
