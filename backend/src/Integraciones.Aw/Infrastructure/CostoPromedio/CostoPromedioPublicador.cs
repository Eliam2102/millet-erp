using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Infrastructure.CostoPromedio;

/// <summary>
/// Compara el costo promedio vigente (lo calcula <see cref="CostoPromedioLector"/>) con el último publicado y escribe una fila
/// nueva, con versión + 1, por cada cambio. Sin cambio no escribe. Primero inserta en el destino y luego guarda la marca: si
/// algo falla entre las dos, el siguiente ciclo repite el mismo INSERT, que es idempotente por llave + versión.
/// </summary>
public sealed class CostoPromedioPublicador(IntegracionesAwDbContext db, ICostoPromedioTransporte transporte, TimeProvider reloj)
{
    public async Task<int> PublicarAsync(IReadOnlyList<CostoPromedioFila> actuales, CancellationToken ct)
    {
        var previos = (await db.CostosPromedioPublicados.ToListAsync(ct))
            .ToDictionary(p => (p.ArticuloAw, p.Sucursal, p.AlmacenTipo, p.Linea, p.AnchoMm, p.AltoMm));
        var publicadas = 0;
        foreach (var f in actuales)
        {
            var costo = decimal.Round(f.Costo, 6, MidpointRounding.AwayFromZero); // precisión de la columna destino
            var llave = (f.ArticuloAw, f.Sucursal, f.AlmacenTipo ?? "", f.Linea ?? "", f.AnchoMm ?? 0, f.AltoMm ?? 0); // marca interna: NULL = ""
            previos.TryGetValue(llave, out var previo);
            if (previo is not null && previo.Costo == costo) continue;

            var ahora = reloj.GetUtcNow();
            var version = (previo?.VersionCosto ?? 0) + 1;
            var fila = f with { CostoM2 = f.CostoM2 is null ? null : costo, CostoPieza = f.CostoPieza is null ? null : costo };
            await transporte.EscribirAsync(new CostoPromedioEscrito(fila, version, ahora), ct);
            if (previo is null)
                db.CostosPromedioPublicados.Add(AwCostoPromedioPublicado.Crear(
                    f.ArticuloAw, f.Sucursal, llave.Item3, llave.Item4, llave.Item5, llave.Item6, costo, ahora));
            else previo.Registrar(costo, ahora);
            await db.SaveChangesAsync(ct); // por fila: una falla no obliga a reenviar las anteriores
            publicadas++;
        }
        return publicadas;
    }
}
