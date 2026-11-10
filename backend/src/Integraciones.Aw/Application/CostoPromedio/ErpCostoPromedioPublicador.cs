using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Application.CostoPromedio;

/// <summary>
/// Sección <c>Integraciones:CostoPromedio</c> (M-447, contrato v9). Apagado por omisión: sin <see cref="Habilitado"/> no publica.
/// <see cref="Destino"/>: <c>Simulado</c> (en memoria, por omisión) o <c>Sql</c> (<c>dbo.erp_costo_promedio</c> en la base de la app,
/// <c>ConnectionStrings:AlmacenesAppDb</c>, usuario <c>millet_erp_integracion</c> con SELECT + INSERT).
/// </summary>
public sealed class ErpCostoPromedioOptions
{
    public const string SectionName = "Integraciones:CostoPromedio";

    public bool Habilitado { get; init; }
    public string Destino { get; init; } = "Simulado";
    public int IntervaloSegundos { get; init; } = 60;
}

/// <summary>
/// Renglón de <c>dbo.erp_costo_promedio</c>. Llave: artículo A+W + sucursal (clave A+W) + tipo + línea (vacías si la sucursal no
/// tiene lista de almacenes; línea vacía en herraje) + medida (nula sin medida; lado menor en ancho, mayor en alto). Con medida va <see cref="CostoM2"/>; sin medida
/// <see cref="CostoPieza"/>. Siempre PESOSMX. <see cref="Version"/> sube de 1 en 1 por llave.
/// </summary>
public sealed record ErpCostoPromedioFila(
    int ArticuloAw, string Sucursal, string AlmacenTipo, string Linea, int? AnchoMm, int? AltoMm,
    decimal? CostoM2, decimal? CostoPieza, DateTimeOffset VigenteDesde, long Version)
{
    public const string Moneda = "PESOSMX";

    public (int, string, string, string, int?, int?) Llave => (ArticuloAw, Sucursal, AlmacenTipo, Linea, AnchoMm, AltoMm);
    public decimal Costo => CostoM2 ?? CostoPieza ?? 0m;
}

/// <summary>Tabla <c>dbo.erp_costo_promedio</c>: el ERP solo inserta y lee la última versión por llave.</summary>
public interface IErpCostoPromedioDestino
{
    Task<IReadOnlyList<ErpCostoPromedioFila>> LeerVigentesAsync(CancellationToken ct);

    /// <summary>Inserta las filas del ciclo; cada una solo si su llave + versión no existe (reintento o dos instancias no duplican).</summary>
    Task InsertarAsync(IReadOnlyList<ErpCostoPromedioFila> filas, CancellationToken ct);
}

/// <summary>
/// Publica el costo promedio del ERP para que la app de Almacenes valúe ENTRADA_MANUAL y SALIDA_MANUAL (contrato v9, correo de Jorge
/// del 10-nov). Cada ciclo calcula el promedio vigente por llave (<see cref="CostoPromedioAppLector"/>), lo compara con la última
/// versión publicada y, si cambió o no existe, inserta la versión siguiente. Sin cambio no escribe nada; con existencia en cero la
/// última fila se conserva. Solo artículos ligados a A+W con clave numérica.
/// </summary>
public sealed class ErpCostoPromedioPublicador(
    AlmacenDbContext almacen, CompartidoDbContext maestros, IErpCostoPromedioDestino destino, ILogger<ErpCostoPromedioPublicador> logger)
{
    public const int Decimales = 6;

    public async Task<int> PublicarAsync(CancellationToken ct)
    {
        var costos = await CostoPromedioAppLector.LeerAsync(almacen, ct);
        if (costos.Count == 0) return 0;

        var articuloIds = costos.Select(c => c.ArticuloId).Distinct().ToList();
        var articulos = (await (from a in maestros.Articulos.AsNoTracking()
                                join p in maestros.ProductosAw.AsNoTracking() on a.ProductoAwId equals p.Id
                                where articuloIds.Contains(a.Id)
                                select new { a.Id, p.ReferenciaExterna }).ToListAsync(ct))
            .Where(x => int.TryParse(x.ReferenciaExterna, out _))
            .ToDictionary(x => x.Id, x => int.Parse(x.ReferenciaExterna));
        var varianteIds = costos.Where(c => c.VarianteId is not null).Select(c => c.VarianteId!.Value).Distinct().ToList();
        var variantes = await maestros.ProductosAwVariantes.AsNoTracking().Where(v => varianteIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => (v.AnchoMm, v.AltoMm), ct);
        var sucursalIds = costos.Select(c => c.SucursalId).Distinct().ToList();
        var sucursales = await maestros.Sucursales.AsNoTracking().Where(s => sucursalIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.ClaveAw ?? s.Clave, ct);

        var porLlave = new List<(ErpCostoPromedioFila Fila, decimal Existencia)>();
        foreach (var c in costos)
        {
            if (!articulos.TryGetValue(c.ArticuloId, out var articuloAw) || !sucursales.TryGetValue(c.SucursalId, out var sucursal)) continue;
            int? ancho = null, alto = null;
            if (c.VarianteId is { } v)
            {
                if (!variantes.TryGetValue(v, out var m) || m.AnchoMm is null || m.AltoMm is null) continue;
                (ancho, alto) = ((int)m.AnchoMm.Value, (int)m.AltoMm.Value);
            }
            porLlave.Add((new ErpCostoPromedioFila(articuloAw, sucursal.Trim().ToUpperInvariant(), c.TipoAlmacen, c.Linea, ancho, alto,
                ancho is null ? null : c.CostoMxn, ancho is null ? c.CostoMxn : null, c.Fecha, 0), c.Existencia));
        }

        var nuevas = Pendientes(Consolidar(porLlave), await destino.LeerVigentesAsync(ct));
        if (nuevas.Count == 0) return 0;
        await destino.InsertarAsync(nuevas, ct);
        logger.LogInformation("[ErpCostoPromedio] {Filas} costos publicados.", nuevas.Count);
        return nuevas.Count;
    }

    /// <summary>
    /// Una fila por llave del contrato. La medida va sin orientación (lado menor en ancho, mayor en alto: 2600×1800 = 1800×2600) y
    /// varias variantes del ERP del mismo artículo A+W y medida se juntan en un solo promedio ponderado por existencia (con
    /// existencia en cero, el último). Sin esto cada variante pisaría a la otra y saldría una versión nueva en cada ciclo.
    /// </summary>
    public static IReadOnlyList<ErpCostoPromedioFila> Consolidar(IEnumerable<(ErpCostoPromedioFila Fila, decimal Existencia)> filas) =>
        filas
            .Select(x => x.Fila.AnchoMm is int a && x.Fila.AltoMm is int b && a > b
                ? (Fila: x.Fila with { AnchoMm = b, AltoMm = a }, x.Existencia)
                : x)
            .GroupBy(x => x.Fila.Llave)
            .Select(g =>
            {
                var (_, promedio) = CostoMedio.Vigente(g.Select(x => (x.Existencia, x.Fila.Costo, x.Fila.VigenteDesde)).ToList());
                var costo = decimal.Round(promedio!.Value, Decimales, MidpointRounding.AwayFromZero);
                var f = g.First().Fila;
                return f with
                {
                    CostoM2 = f.AnchoMm is null ? null : costo,
                    CostoPieza = f.AnchoMm is null ? costo : null,
                    VigenteDesde = g.Max(x => x.Fila.VigenteDesde),
                };
            })
            .ToList();

    /// <summary>Filas a insertar: llaves nuevas (versión 1) y llaves cuyo costo cambió (última versión + 1).</summary>
    public static IReadOnlyList<ErpCostoPromedioFila> Pendientes(
        IEnumerable<ErpCostoPromedioFila> actuales, IEnumerable<ErpCostoPromedioFila> vigentes)
    {
        var ultimas = vigentes.GroupBy(f => f.Llave).ToDictionary(g => g.Key, g => g.MaxBy(f => f.Version)!);
        return actuales
            .Where(a => !ultimas.TryGetValue(a.Llave, out var u) || u.Costo != a.Costo)
            .Select(a => a with { Version = ultimas.TryGetValue(a.Llave, out var u) ? u.Version + 1 : 1 })
            .ToList();
    }
}
