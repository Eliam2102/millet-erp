using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

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
    Guid? ArticuloId) : IRequest<ReporteResponse<AlfakHistorialFila>>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "almacen.reportes.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class AlfakHistorialAlmacenQueryValidator : AbstractValidator<AlfakHistorialAlmacenQuery>
{
    public AlfakHistorialAlmacenQueryValidator()
    {
        RuleFor(x => x).Must(x => x.Desde <= x.Hasta)
            .WithMessage("La fecha inicial no puede ser posterior a la final.")
            .WithName("Desde");
    }
}

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
    string? ArticuloDescripcion,
    // F1-MP-01: una fila por medida de hoja (cantidades en m²); Medida = «2440 × 3660 mm · 20 hojas» al saldo final.
    Guid? ProductoAwVarianteId = null,
    string? Medida = null,
    // S4b: una fila por lote del proveedor (null = sin lote / apertura).
    string? Lote = null,
    // F1-ALM-13: hojas (unidad física) al saldo final del vidrio por medida; la existencia sigue en m². Null = sin medida.
    decimal? HojasFinal = null);

internal sealed record LineaKardex(Guid SubAlmacenId, string SubAlmacenNombre, Guid ArticuloId, Guid? ProductoAwVarianteId, string? Lote,
    TipoMovimiento Tipo, DateOnly FechaMovimiento, decimal Cantidad, decimal CostoUnitarioMxn,
    Guid? OcId = null, string? Folio = null, string? Motivo = null, Guid? UbicacionId = null, string? Ubicacion = null);

public sealed class AlfakHistorialAlmacenHandler
    : IRequestHandler<AlfakHistorialAlmacenQuery, ReporteResponse<AlfakHistorialFila>>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;
    private readonly IVarianteVidrioReadPort? _variantes; // opcional solo para pruebas unitarias sin medidas

    public AlfakHistorialAlmacenHandler(AlmacenDbContext db, IArticuloReadPort articulos, IVarianteVidrioReadPort? variantes = null)
    {
        _db = db;
        _articulos = articulos;
        _variantes = variantes;
    }

    public async Task<ReporteResponse<AlfakHistorialFila>> Handle(
        AlfakHistorialAlmacenQuery request, CancellationToken cancellationToken)
    {
        await AlcanceSucursalAlmacen.VerificarSubAlmacenAsync(_db, request.SubAlmacenId,
            request.SucursalesPermitidas, cancellationToken);
        var filas = await CalcularAsync(_db, request.Desde, request.Hasta, request.SubAlmacenId, request.ArticuloId,
            request.SucursalesPermitidas, cancellationToken);
        filas = await AplicarFotoCierreAsync(_db, request.Hasta, filas, cancellationToken);
        var medidas = _variantes is null ? new Dictionary<Guid, VarianteVidrioLectura>()
            : await _variantes.ObtenerPorIdsAsync(
                filas.Where(f => f.ProductoAwVarianteId != null).Select(f => f.ProductoAwVarianteId!.Value).Distinct().ToArray(), cancellationToken);
        filas = filas.Select(f => f with
        {
            Medida = MedidaReporte.Texto(f.ProductoAwVarianteId, f.SaldoFinal, medidas),
            HojasFinal = MedidaReporte.Hojas(f.ProductoAwVarianteId, f.SaldoFinal, medidas),
        }).ToList();

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

    /// <summary>
    /// Líneas que cuentan para el kardex y el ALFAK (registradas, y canceladas con folio para netear entrada y CEN), con su
    /// sub-almacén y su costo congelado. La comparten el ALFAK y el cuadre del cierre (ALM-13-a).
    /// </summary>
    internal static IQueryable<LineaKardex> LineasKardex(AlmacenDbContext db, Guid? subAlmacenId, Guid? articuloId, IReadOnlyList<Guid>? sucursales,
        DateOnly? desde = null, DateOnly? hasta = null) =>
        from l in db.LineasMovimiento.AsNoTracking()
            join m in db.Movimientos.AsNoTracking() on l.MovimientoId equals m.Id
            join v in db.Ubicaciones.AsNoTracking() on l.UbicacionId equals (Guid?)v.Id
            join sa in db.SubAlmacenes.AsNoTracking() on v.SubAlmacenId equals sa.Id
            // Una recepción cancelada (con folio) sigue en el kardex: su entrada y su CEN se netean.
            where (m.Estado == EstadoMovimiento.Registrado || (m.Estado == EstadoMovimiento.Cancelado && m.Folio != null))
               && (subAlmacenId == null || v.SubAlmacenId == subAlmacenId)
               && (articuloId == null || l.ArticuloId == articuloId)
               && (desde == null || m.FechaMovimiento >= desde) && (hasta == null || m.FechaMovimiento <= hasta)
               && (sucursales == null
                   || AlcanceSucursalAlmacen.SubAlmacenesPermitidos(db, sucursales!).Contains(v.SubAlmacenId))
            select new LineaKardex(v.SubAlmacenId, sa.Nombre, l.ArticuloId, l.ProductoAwVarianteId, l.Lote,
                m.Tipo, m.FechaMovimiento, l.Cantidad, l.CostoUnitarioMxn, m.OcId, m.Folio, m.Motivo, v.Id, v.Clave);

    /// <summary>
    /// Filas del ALFAK sin enriquecer (sin medida ni artículo). Lo usan el reporte y el cierre mensual para la foto de la valuación.
    /// <c>sucursales = null</c> = alcance corporativo.
    /// </summary>
    internal static async Task<List<AlfakHistorialFila>> CalcularAsync(AlmacenDbContext db, DateOnly desde, DateOnly hasta,
        Guid? subAlmacenId, Guid? articuloId, IReadOnlyList<Guid>? sucursales, CancellationToken cancellationToken)
    {
        // Líneas de movimiento Registrados, con el nombre del sub-almacén por
        // JOIN (traducible; proyección sobre columnas del entity). El cálculo de
        // saldo inicial/final por periodo se hace en memoria sobre estas filas.
        // PR6a: el sub-almacén salió de la cabecera; se deriva de la ubicación de
        // la línea (la misma derivación de la vista v_movimiento_sub_almacen: todas
        // las líneas de un movimiento comparten sub-almacén). Sin la vista, la
        // consulta también corre en EF InMemory (pruebas unitarias del cierre).
        var rows = await LineasKardex(db, subAlmacenId, articuloId, sucursales).ToListAsync(cancellationToken);

        var grupos = rows.GroupBy(r => new { r.SubAlmacenId, r.ArticuloId, r.ProductoAwVarianteId, r.Lote });
        var filas = new List<AlfakHistorialFila>();

        foreach (var g in grupos)
        {
            var subAlmacenNombre = g.First().SubAlmacenNombre;
            decimal inicial = 0m, entradas = 0m, salidas = 0m, ajPos = 0m, ajNeg = 0m;
            foreach (var mov in g)
            {
                var signo = TipoMovimientoExtensions.EsEntrada(mov.Tipo) ? 1m : -1m;
                if (mov.FechaMovimiento < desde)
                {
                    inicial += signo * mov.Cantidad;
                }
                else if (mov.FechaMovimiento >= desde && mov.FechaMovimiento <= hasta)
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
                        || mov.Tipo == TipoMovimiento.BajaPorDano
                        || mov.Tipo == TipoMovimiento.SalidaPorLiberacionRevision
                        || mov.Tipo == TipoMovimiento.CancelacionEntradaCompra
                        || mov.Tipo == TipoMovimiento.SalidaNoPlanificada)
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
            var posteriores = g.Where(m => m.FechaMovimiento > hasta)
                .Select(m => (TipoMovimientoExtensions.EsEntrada(m.Tipo), m.Cantidad, m.CostoUnitarioMxn)).ToList();
            // C7.2a: costo representativo del sub-almacén = promedio ponderado
            // entre sus bins (una fila arbitraria sería el costo de un bin al
            // azar con N bins). Sin cambio con la ÚNICA única.
            var saldosActuales = await db.SaldosInventario.AsNoTracking()
                .Where(s => s.SubAlmacenId == g.Key.SubAlmacenId
                    && s.ArticuloId == g.Key.ArticuloId
                    && s.ProductoAwVarianteId == g.Key.ProductoAwVarianteId
                    && s.Lote == g.Key.Lote)
                .Select(s => new { s.Cantidad, s.CostoPromedioMxn })
                .ToListAsync(cancellationToken);
            var cantidadActual = saldosActuales.Sum(s => s.Cantidad);
            var costoProm = cantidadActual != 0
                ? Math.Round(
                    saldosActuales.Sum(s => s.Cantidad * s.CostoPromedioMxn) / cantidadActual, 4)
                : saldosActuales.Count > 0 ? saldosActuales[0].CostoPromedioMxn : 0m;
            var (valorFinal, costoFinal) = ValorizarAlCorte(
                saldoFinal, saldosActuales.Sum(s => s.Cantidad * s.CostoPromedioMxn), costoProm, posteriores);
            filas.Add(new AlfakHistorialFila(
                SubAlmacenId: g.Key.SubAlmacenId,
                ArticuloId: g.Key.ArticuloId,
                SaldoInicial: inicial,
                Entradas: entradas,
                Salidas: salidas,
                AjustesPositivos: ajPos,
                AjustesNegativos: ajNeg,
                SaldoFinal: saldoFinal,
                CostoPromedioFinal: costoFinal,
                ValorInventarioFinal: valorFinal,
                // Sub-almacén por JOIN; Articulo* nulos aquí, se enriquecen en batch.
                SubAlmacenNombre: subAlmacenNombre,
                ArticuloClave: null,
                ArticuloDescripcion: null,
                ProductoAwVarianteId: g.Key.ProductoAwVarianteId,
                Lote: g.Key.Lote));
        }
        return filas;
    }

    /// <summary>
    /// F1-ALM-13: si <c>Hasta</c> es el último día de un mes cerrado, el valor y el costo salen de la foto tomada al cierre
    /// (<see cref="ValuacionCierre"/>), no del cálculo de hoy. Los grupos sin foto (nacieron después del cierre) se quedan como están.
    /// </summary>
    internal static async Task<List<AlfakHistorialFila>> AplicarFotoCierreAsync(AlmacenDbContext db, DateOnly hasta,
        List<AlfakHistorialFila> filas, CancellationToken ct)
    {
        if (hasta.Day != DateTime.DaysInMonth(hasta.Year, hasta.Month) || filas.Count == 0) return filas;
        var subs = filas.Select(f => f.SubAlmacenId).Distinct().ToList();
        var foto = await db.ValuacionesCierre.AsNoTracking()
            .Where(v => v.Anio == hasta.Year && v.Mes == hasta.Month && subs.Contains(v.SubAlmacenId)).ToListAsync(ct);
        if (foto.Count == 0) return filas;
        var porGrupo = foto.ToDictionary(v => (v.SubAlmacenId, v.ArticuloId, v.ProductoAwVarianteId, v.Lote));
        return filas.Select(f => porGrupo.TryGetValue((f.SubAlmacenId, f.ArticuloId, f.ProductoAwVarianteId, f.Lote), out var v)
            ? f with { ValorInventarioFinal = v.ValorMxn, CostoPromedioFinal = v.Cantidad != 0 ? Math.Round(v.ValorMxn / v.Cantidad, 4) : f.CostoPromedioFinal }
            : f).ToList();
    }

    /// <summary>
    /// Valor y costo promedio al corte (<c>Hasta</c>): parte del valor actual del saldo y revierte las
    /// líneas posteriores al corte con su costo congelado (entrada resta, salida suma). Sin líneas
    /// posteriores equivale al cálculo previo (saldoFinal × costo actual).
    /// </summary>
    // ponytail: los ajustes de valor sin línea de movimiento (SaldoInventario.AjustarValor) posteriores a Hasta no se
    // revierten; en un mes cerrado manda la foto del cierre (AplicarFotoCierreAsync), así que solo afecta cortes de meses abiertos
    // y ajustes hechos entre el fin de mes y la ejecución del cierre.
    public static (decimal Valor, decimal Costo) ValorizarAlCorte(
        decimal saldoFinal, decimal valorActual, decimal costoActual,
        IEnumerable<(bool EsEntrada, decimal Cantidad, decimal CostoUnitario)> posterioresAHasta)
    {
        var lineas = posterioresAHasta.ToList();
        if (lineas.Count == 0)
            return (Math.Round(saldoFinal * costoActual, 2), costoActual);
        var valor = valorActual - lineas.Sum(l => (l.EsEntrada ? 1m : -1m) * l.Cantidad * l.CostoUnitario);
        var costo = saldoFinal != 0 ? Math.Round(valor / saldoFinal, 4) : costoActual;
        return (Math.Round(valor, 2), costo);
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
        new("medida", "Medida", "texto"),
        new("lote", "Lote", "texto"),
        new("saldoInicial", "Saldo inicial", "numero", "right"),
        new("entradas", "Entradas", "numero", "right"),
        new("salidas", "Salidas", "numero", "right"),
        new("ajustesPositivos", "Ajuste +", "numero", "right"),
        new("ajustesNegativos", "Ajuste -", "numero", "right"),
        new("saldoFinal", "Saldo final", "numero", "right"),
        new("hojasFinal", "Hojas", "numero", "right"),
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
