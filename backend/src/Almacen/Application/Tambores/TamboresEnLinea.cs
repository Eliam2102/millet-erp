using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Tambores;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Tambores;

/// <summary>
/// Regla AC-030 (M-172): el silicón A/B y el Ködispace se descuentan del inventario al salir del almacén por tambor o
/// cubeta a una línea de producción. Un artículo marcado «Se consume por tambor a línea» exige línea destino de la
/// sucursal del almacén y piezas enteras; el costo sale al promedio en el mismo movimiento de salida (sin póliza nueva).
/// </summary>
internal static class TamboresEnLinea
{
    public sealed record Surtido(Guid LineaProduccionId, Guid ArticuloId, int Piezas, decimal? KgPresentacion);

    /// <summary>Valida las líneas de la salida y devuelve los tambores a registrar. Sin puerto de artículos no aplica.</summary>
    public static async Task<IReadOnlyList<Surtido>> ValidarAsync(AlmacenDbContext db, IArticuloReadPort? articulos,
        IReadOnlyList<RegistrarSalidaLineaInput> lineas, Func<int, decimal> cantidadBase, Guid? sucursalId, CancellationToken ct)
    {
        if (articulos is null) return [];
        var maestro = await articulos.ObtenerPorIdsAsync(lineas.Select(l => l.ArticuloId).Distinct().ToList(), ct);
        var surtidos = new List<Surtido>();
        for (var i = 0; i < lineas.Count; i++)
        {
            if (maestro.GetValueOrDefault(lineas[i].ArticuloId) is not { ConsumoPorTamborALinea: true } a) continue;
            if (lineas[i].LineaProduccionId is not Guid lineaId)
                throw new BusinessRuleException("SALIDA_TAMBOR_SIN_LINEA",
                    $"El artículo {a.Clave} se surte por tambor o cubeta: elige la línea de producción destino.");
            var piezas = cantidadBase(i);
            if (piezas != decimal.Truncate(piezas))
                throw new BusinessRuleException("SALIDA_TAMBOR_PIEZAS_ENTERAS",
                    $"El artículo {a.Clave} se surte en piezas enteras (tambores o cubetas); no se admite {piezas.ToString("0.####", CultureInfo.InvariantCulture)}.");
            var linea = await db.LineasProduccion.AsNoTracking().FirstOrDefaultAsync(x => x.Id == lineaId, ct);
            if (linea is null || !linea.Activa)
                throw new BusinessRuleException("SALIDA_TAMBOR_LINEA_INVALIDA", "La línea de producción elegida no existe o no está activa.");
            if (sucursalId is Guid sucursal && linea.SucursalId != sucursal)
                throw new BusinessRuleException("SALIDA_TAMBOR_LINEA_OTRA_SUCURSAL",
                    $"La línea {linea.Nombre} es de otra sucursal: surte desde un almacén de la sucursal de la línea.");
            surtidos.Add(new Surtido(lineaId, a.Id, (int)piezas, a.KgPorPresentacion));
        }
        return surtidos;
    }

    /// <summary>
    /// Cada pieza entra ACTIVA; el activo anterior del mismo artículo en la misma línea (también el surtido en esta misma
    /// salida) queda GASTADO en el mismo instante con el folio de esta salida. Corre dentro del candado por
    /// (sucursal, artículo) de la salida, así dos salidas simultáneas no dejan dos activos.
    /// </summary>
    public static async Task RegistrarAsync(AlmacenDbContext db, IReadOnlyList<Surtido> surtidos, Guid movimientoId, string folio,
        DateTimeOffset ahora, CancellationToken ct)
    {
        foreach (var grupo in surtidos.GroupBy(s => (s.LineaProduccionId, s.ArticuloId)))
        {
            var activos = await db.TamboresLinea
                .Where(t => t.LineaProduccionId == grupo.Key.LineaProduccionId && t.ArticuloId == grupo.Key.ArticuloId
                    && t.Estado == TamborEnLinea.Activo)
                .ToListAsync(ct);
            foreach (var s in grupo)
                for (var i = 0; i < s.Piezas; i++)
                {
                    foreach (var previo in activos) previo.Gastar(ahora, folio);
                    var nuevo = new TamborEnLinea(s.LineaProduccionId, s.ArticuloId, movimientoId, folio, s.KgPresentacion, ahora);
                    db.TamboresLinea.Add(nuevo);
                    activos = [nuevo];
                }
        }
    }
}
