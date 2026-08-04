using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.Reportes;

/// <summary>
/// Reporte <b>ALFAK-HISTORIAL-ALMACEN</b> (F8-PR1). Agrupa movimientos
/// por (sub_almacen, articulo) y calcula entradas/salidas/ajustes/saldo
/// final del periodo. Sustituye el reporte SAP histórico.
///
/// <para>
/// Para el saldo inicial se calcula sumando todos los movimientos
/// previos al inicio del periodo (entrada - salida + ajustes); para el
/// saldo final, todos los movimientos hasta el final. Tradeoff aceptado
/// por el volumen del módulo (~5K movimientos/mes; queries con índices
/// sobre fecha_movimiento + sub_almacen son rápidos).
/// </para>
/// </summary>
public sealed record AlfakHistorialAlmacenQuery(
    DateOnly Desde,
    DateOnly Hasta,
    Guid? SubAlmacenId,
    Guid? ArticuloId) : IRequest<ReporteResponse<AlfakHistorialFila>>;

public sealed record AlfakHistorialFila(
    Guid SubAlmacenId,
    Guid ArticuloId,
    decimal SaldoInicial,
    decimal Entradas,
    decimal Salidas,
    decimal AjustesPositivos,
    decimal AjustesNegativos,
    decimal SaldoFinal,
    decimal CostoPromedioFinal,
    decimal ValorInventarioFinal,
    // Resueltos en backend (ADR-0042). Null = el read-port no resolvió → el
    // frontend cae al id crudo. SubAlmacenNombre viene por JOIN; Articulo* por batch.
    string? SubAlmacenNombre,
    string? ArticuloClave,
    string? ArticuloDescripcion);

public sealed class AlfakHistorialAlmacenHandler
    : IRequestHandler<AlfakHistorialAlmacenQuery, ReporteResponse<AlfakHistorialFila>>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;

    public AlfakHistorialAlmacenHandler(AlmacenDbContext db, IArticuloReadPort articulos)
    {
        _db = db;
        _articulos = articulos;
    }

    public async Task<ReporteResponse<AlfakHistorialFila>> Handle(
        AlfakHistorialAlmacenQuery request, CancellationToken cancellationToken)
    {
        // Líneas de movimiento Registrados, con el nombre del sub-almacén por
        // JOIN (traducible; proyección sobre columnas del entity). El cálculo de
        // saldo inicial/final por periodo se hace en memoria sobre estas filas.
        // PR6a: el sub-almacén salió de la cabecera; se deriva vía la vista
        // v_movimiento_sub_almacen (una fila por movimiento).
        var query = from l in _db.LineasMovimiento.AsNoTracking()
                    join m in _db.Movimientos.AsNoTracking() on l.MovimientoId equals m.Id
                    join v in _db.MovimientosSubAlmacen.AsNoTracking() on m.Id equals v.MovimientoId
                    join sa in _db.SubAlmacenes.AsNoTracking() on v.SubAlmacenId equals sa.Id
                    where m.Estado == EstadoMovimiento.Registrado
                       && (request.SubAlmacenId == null || v.SubAlmacenId == request.SubAlmacenId)
                       && (request.ArticuloId == null || l.ArticuloId == request.ArticuloId)
                    select new
                    {
                        v.SubAlmacenId,
                        SubAlmacenNombre = sa.Nombre,
                        l.ArticuloId,
                        m.Tipo,
                        m.FechaMovimiento,
                        l.Cantidad,
                    };

        var rows = await query.ToListAsync(cancellationToken);

        var grupos = rows.GroupBy(r => new { r.SubAlmacenId, r.ArticuloId });
        var filas = new List<AlfakHistorialFila>();

        foreach (var g in grupos)
        {
            var subAlmacenNombre = g.First().SubAlmacenNombre;
            decimal inicial = 0m, entradas = 0m, salidas = 0m, ajPos = 0m, ajNeg = 0m;
            foreach (var mov in g)
            {
                var signo = TipoMovimientoExtensions.EsEntrada(mov.Tipo) ? 1m : -1m;
                if (mov.FechaMovimiento < request.Desde)
                {
                    inicial += signo * mov.Cantidad;
                }
                else if (mov.FechaMovimiento >= request.Desde && mov.FechaMovimiento <= request.Hasta)
                {
                    if (mov.Tipo == TipoMovimiento.EntradaCompra
                        || mov.Tipo == TipoMovimiento.DevolucionSalida
                        || mov.Tipo == TipoMovimiento.ReincorporacionTrasRevision)
                    {
                        entradas += mov.Cantidad;
                    }
                    else if (mov.Tipo == TipoMovimiento.SalidaConsumo
                        || mov.Tipo == TipoMovimiento.SalidaPorVale
                        || mov.Tipo == TipoMovimiento.SalidaPorDevolucionAProveedor
                        || mov.Tipo == TipoMovimiento.BajaPorDano)
                    {
                        salidas += mov.Cantidad;
                    }
                    else if (mov.Tipo == TipoMovimiento.AjustePositivo)
                    {
                        ajPos += mov.Cantidad;
                    }
                    else if (mov.Tipo == TipoMovimiento.AjusteNegativo)
                    {
                        ajNeg += mov.Cantidad;
                    }
                }
            }
            var saldoFinal = inicial + entradas - salidas + ajPos - ajNeg;
            // C7.2a: costo representativo del sub-almacén = promedio ponderado
            // entre sus bins (una fila arbitraria sería el costo de un bin al
            // azar con N bins). Sin cambio con la ÚNICA única.
            var saldosActuales = await _db.SaldosInventario.AsNoTracking()
                .Where(s => s.SubAlmacenId == g.Key.SubAlmacenId
                    && s.ArticuloId == g.Key.ArticuloId)
                .Select(s => new { s.Cantidad, s.CostoPromedioMxn })
                .ToListAsync(cancellationToken);
            var cantidadActual = saldosActuales.Sum(s => s.Cantidad);
            var costoProm = cantidadActual != 0
                ? Math.Round(
                    saldosActuales.Sum(s => s.Cantidad * s.CostoPromedioMxn) / cantidadActual, 4)
                : saldosActuales.Count > 0 ? saldosActuales[0].CostoPromedioMxn : 0m;
            filas.Add(new AlfakHistorialFila(
                SubAlmacenId: g.Key.SubAlmacenId,
                ArticuloId: g.Key.ArticuloId,
                SaldoInicial: inicial,
                Entradas: entradas,
                Salidas: salidas,
                AjustesPositivos: ajPos,
                AjustesNegativos: ajNeg,
                SaldoFinal: saldoFinal,
                CostoPromedioFinal: costoProm,
                ValorInventarioFinal: Math.Round(saldoFinal * costoProm, 2),
                // Sub-almacén por JOIN; Articulo* nulos aquí, se enriquecen en batch.
                SubAlmacenNombre: subAlmacenNombre,
                ArticuloClave: null,
                ArticuloDescripcion: null));
        }

        // Enriquecimiento de artículo (clave/descripción) en batch sobre los
        // ArticuloId distintos (ADR-0042, anti-N+1); fallback a null cuando el
        // read-port no resuelve.
        var ids = ExtraerArticuloIdsDistintos(filas);
        var articulos = ids.Count > 0
            ? await _articulos.ObtenerPorIdsAsync(ids, cancellationToken)
            : new Dictionary<Guid, ArticuloLectura>();
        filas = AplicarArticulos(filas, articulos);

        return new ReporteResponse<AlfakHistorialFila>(
            Titulo: "ALFAK-HISTORIAL-ALMACEN",
            GeneradoEn: DateTimeOffset.UtcNow,
            FiltrosAplicados: new Dictionary<string, string?>
            {
                ["desde"] = request.Desde.ToString("yyyy-MM-dd"),
                ["hasta"] = request.Hasta.ToString("yyyy-MM-dd"),
                ["sub_almacen_id"] = request.SubAlmacenId?.ToString(),
                ["articulo_id"] = request.ArticuloId?.ToString(),
            },
            Columnas: Columnas,
            Filas: filas,
            Totales: CalcularTotales(filas));
    }

    /// <summary>ArticuloId distintos para el batch de enriquecimiento (ADR-0042).</summary>
    public static IReadOnlyCollection<Guid> ExtraerArticuloIdsDistintos(
        IReadOnlyList<AlfakHistorialFila> filas)
        => filas.Select(f => f.ArticuloId).Distinct().ToArray();

    /// <summary>Aplica clave/descripción del artículo resuelto; fallback a null
    /// (el FE cae al id) cuando el read-port no lo trae.</summary>
    public static List<AlfakHistorialFila> AplicarArticulos(
        IReadOnlyList<AlfakHistorialFila> filas,
        IReadOnlyDictionary<Guid, ArticuloLectura> articulos)
        => filas.Select(f => articulos.TryGetValue(f.ArticuloId, out var art)
            ? f with { ArticuloClave = art.Clave, ArticuloDescripcion = art.Descripcion }
            : f).ToList();

    /// <summary>
    /// Columnas del reporte. La <c>Clave</c> coincide (camelCase) con la key
    /// serializada de <see cref="AlfakHistorialFila"/> — el front
    /// (<c>ReporteShell</c>) resuelve <c>fila[clave]</c> sensible a mayúsculas,
    /// así que un desalineo deja la celda en blanco. Se muestran nombre/clave/
    /// descripción (no los GUID crudos de sub-almacén/artículo).
    /// </summary>
    public static readonly IReadOnlyList<ColumnaDescriptor> Columnas =
    [
        new("subAlmacenNombre", "Sub-almacén", "texto"),
        new("articuloClave", "Artículo", "texto"),
        new("articuloDescripcion", "Descripción", "texto"),
        new("saldoInicial", "Saldo inicial", "numero", "right"),
        new("entradas", "Entradas", "numero", "right"),
        new("salidas", "Salidas", "numero", "right"),
        new("ajustesPositivos", "Ajuste +", "numero", "right"),
        new("ajustesNegativos", "Ajuste -", "numero", "right"),
        new("saldoFinal", "Saldo final", "numero", "right"),
        new("costoPromedioFinal", "Costo prom.", "moneda", "right"),
        new("valorInventarioFinal", "Valor", "moneda", "right"),
    ];

    /// <summary>Totales del pie. Las keys deben ser claves de columna.</summary>
    public static Dictionary<string, decimal> CalcularTotales(
        IReadOnlyList<AlfakHistorialFila> filas) => new()
    {
        ["entradas"] = filas.Sum(f => f.Entradas),
        ["salidas"] = filas.Sum(f => f.Salidas),
        ["ajustesPositivos"] = filas.Sum(f => f.AjustesPositivos),
        ["ajustesNegativos"] = filas.Sum(f => f.AjustesNegativos),
        ["valorInventarioFinal"] = filas.Sum(f => f.ValorInventarioFinal),
    };
}
