using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Blob;

namespace Millet.Facturacion.Infrastructure.DatosMaestros;

/// <summary>
/// Adapter REAL de <see cref="IDatosImpresionCfdiReadPort"/> (FMT-4): domicilio y
/// logo de la empresa (FMT-1a, <c>compartido.empresas</c> + blob) y descripciones
/// de los catálogos SAT. Solo lectura. Las descripciones no filtran por activos:
/// un comprobante viejo debe seguir imprimiendo la clave que usó.
/// </summary>
public sealed class DatosImpresionCfdiReadAdapter(
    CompartidoDbContext db, IBlobStoragePort blobs, ILogger<DatosImpresionCfdiReadAdapter> logger) : IDatosImpresionCfdiReadPort
{
    public async Task<DatosImpresionCfdi> ObtenerAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var empresa = await db.Empresas.AsNoTracking()
            .Where(e => e.Id == empresaId)
            .Select(e => new
            {
                e.NombreComercial, e.Calle, e.NumeroExterior, e.NumeroInterior, e.Colonia,
                e.Ciudad, e.Municipio, e.Estado, e.Pais, e.CodigoPostal, e.LogoBlobRef,
            })
            .FirstOrDefaultAsync(cancellationToken);

        var regimenes = Catalogo(await db.RegimenesFiscales.AsNoTracking()
            .Select(r => new { Clave = r.Codigo, r.Nombre }).ToListAsync(cancellationToken), r => r.Clave, r => r.Nombre);
        var usos = Catalogo(await db.UsosCfdi.AsNoTracking()
            .Select(u => new { u.ClaveSat, u.Descripcion }).ToListAsync(cancellationToken), u => u.ClaveSat, u => u.Descripcion);
        var formas = Catalogo(await db.FormasPago.AsNoTracking()
            .Select(f => new { f.ClaveSat, f.Descripcion }).ToListAsync(cancellationToken), f => f.ClaveSat, f => f.Descripcion);

        if (empresa is null)
            return DatosImpresionCfdi.Vacio with { Regimenes = regimenes, UsosCfdi = usos, FormasPago = formas };

        var domicilio = new[]
        {
            Unir(" ", empresa.Calle, empresa.NumeroExterior,
                string.IsNullOrWhiteSpace(empresa.NumeroInterior) ? null : $"Int. {empresa.NumeroInterior}"),
            Unir(", ", string.IsNullOrWhiteSpace(empresa.Colonia) ? null : $"Col. {empresa.Colonia}", empresa.Ciudad,
                empresa.Municipio == empresa.Ciudad ? null : empresa.Municipio),
            Unir(", ", empresa.Estado, empresa.Pais,
                string.IsNullOrWhiteSpace(empresa.CodigoPostal) ? null : $"CP {empresa.CodigoPostal}"),
        }.Where(l => l.Length > 0).ToList();

        return new DatosImpresionCfdi(empresa.NombreComercial, domicilio,
            await LogoAsync(empresa.LogoBlobRef, cancellationToken), regimenes, usos, formas);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> CodigosArticuloAsync(
        IReadOnlyCollection<Guid> productoIds, CancellationToken cancellationToken)
    {
        if (productoIds.Count == 0) return new Dictionary<Guid, string>();
        return await db.ProductosAw.AsNoTracking()
            .Where(p => productoIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.ReferenciaExterna, cancellationToken);
    }

    private async Task<byte[]?> LogoAsync(string? blobRef, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobRef)) return null;
        try
        {
            await using var stream = await blobs.ObtenerStreamAsync(blobRef, cancellationToken);
            using var memoria = new MemoryStream();
            await stream.CopyToAsync(memoria, cancellationToken);
            return memoria.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin logo el PDF imprime la razón social: no se bloquea la descarga del CFDI.
            logger.LogWarning(ex, "No se pudo leer el logo de la empresa ({BlobRef}); el PDF sale sin logo.", blobRef);
            return null;
        }
    }

    /// <summary>Una descripción por clave; si el catálogo repite una clave gana la primera.</summary>
    private static Dictionary<string, string> Catalogo<T>(IEnumerable<T> filas, Func<T, string> clave, Func<T, string> descripcion) =>
        filas.GroupBy(clave).ToDictionary(g => g.Key, g => descripcion(g.First()));

    private static string Unir(string separador, params string?[] partes) =>
        string.Join(separador, partes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
