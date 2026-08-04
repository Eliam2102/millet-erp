using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Ports.Almacen;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Infrastructure.Almacen;

/// <summary>
/// Adapter real de <see cref="IAlmacenRecepcionReadPort"/> (F5-PR1).
/// Reemplaza el <c>NoOpAlmacenRecepcionReadPort</c> del F0-PR1.
/// Lee de la **proyección local** <c>recepciones_oc_local</c> que el
/// listener <c>OcRecepcionRegistradaListener</c> mantiene actualizada
/// desde el integration event <c>almacen.oc_recepcion.registrada.v1</c>.
///
/// <para>
/// Beneficio del patrón de proyección local: no cruza DbContexts, no
/// hace lookups remote, y cuando Almacén cambie schema interno, CxP
/// sigue funcionando con su propia representación. La compatibilidad
/// de contrato se mantiene por el versionado del integration event.
/// </para>
/// </summary>
public sealed class AlmacenRecepcionReadPortAdapter : IAlmacenRecepcionReadPort
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly CuentasPorPagarDbContext _db;

    public AlmacenRecepcionReadPortAdapter(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<RecepcionOcDto?> ObtenerRecepcionDeOcAsync(Guid ordenCompraId, CancellationToken cancellationToken)
    {
        // Si hay más de una recepción contra la misma OC, tomamos la
        // más reciente — caso típico variante B donde una OC puede
        // recibirse en parcialidades.
        var recepcion = await _db.RecepcionesOcLocal
            .AsNoTracking()
            .Where(r => r.OrdenCompraId == ordenCompraId)
            .OrderByDescending(r => r.OcurridoEn)
            .FirstOrDefaultAsync(cancellationToken);

        if (recepcion is null) return null;

        var lineas = JsonSerializer.Deserialize<List<LineaRecepcionLocalPayload>>(
            recepcion.LineasJson, JsonOptions) ?? [];

        return new RecepcionOcDto(
            RecepcionId: recepcion.RecepcionId,
            OrdenCompraId: recepcion.OrdenCompraId,
            FechaRecepcion: recepcion.FechaMovimiento,
            FacturaPendiente: recepcion.FacturaPendiente,
            Lineas: lineas
                .Where(l => l.LineaOcId is not null)
                .Select(l => new LineaRecepcionDto(l.LineaOcId!.Value, l.Cantidad))
                .ToList());
    }

    /// <summary>Forma en la que las líneas se persisten dentro de <c>LineasJson</c>.</summary>
    internal sealed record LineaRecepcionLocalPayload(
        Guid LineaRecepcionId,
        Guid? LineaOcId,
        Guid ArticuloId,
        string UnidadMedida,
        decimal Cantidad,
        decimal CostoUnitarioMxn,
        decimal MontoTotalMxn);
}
