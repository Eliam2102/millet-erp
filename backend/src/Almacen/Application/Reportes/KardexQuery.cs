using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Application.Reportes;

/// <summary>
/// F1-ALM-15 (86e3tac22): kardex de un artículo por almacén, ubicación y periodo. Saldo inicial del periodo + entradas − salidas =
/// saldo final, con el saldo, el costo unitario, el costo promedio y el valor después de cada movimiento. Valoriza con el costo de
/// cada línea, igual que el cuadre del cierre (ALM-13-a); si <c>Hasta</c> es el último día de un mes cerrado y no se filtra por
/// ubicación, compara el saldo final con la foto del cierre. Reusa el kardex del ALFAK (<see cref="AlfakHistorialAlmacenHandler.LineasKardex"/>).
/// </summary>
public sealed record KardexQuery(
    Guid ArticuloId,
    DateOnly Desde,
    DateOnly Hasta,
    Guid? SubAlmacenId = null,
    Guid? UbicacionId = null) : IRequest<ReporteResponse<KardexFila>>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "almacen.reportes.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class KardexQueryValidator : AbstractValidator<KardexQuery>
{
    public KardexQueryValidator()
    {
        RuleFor(x => x.ArticuloId).NotEmpty().WithMessage("El artículo es obligatorio.");
        RuleFor(x => x).Must(x => x.Desde <= x.Hasta)
            .WithMessage("La fecha inicial no puede ser posterior a la final.")
            .WithName("Desde");
    }
}

/// <summary>Un movimiento del kardex (o la fila «Saldo inicial»). <c>Saldo</c>, <c>CostoPromedioMxn</c> y <c>ValorSaldo</c> son los de después del movimiento.</summary>
public sealed record KardexFila(
    DateOnly Fecha,
    string? Folio,
    string Tipo,
    string? Documento,
    string? Ubicacion,
    decimal Entrada,
    decimal Salida,
    decimal Saldo,
    decimal CostoUnitarioMxn,
    decimal CostoPromedioMxn,
    decimal ValorSaldo,
    // Hojas (unidad física del vidrio por medida); null si el artículo no es por medida. La existencia sigue en m².
    decimal? EntradaHojas = null,
    decimal? SalidaHojas = null,
    decimal? SaldoHojas = null);

public sealed class KardexHandler(AlmacenDbContext db, IArticuloReadPort articulos, IComprasOcReadPort? ocs = null,
    IComprasRequisicionReadPort? requisiciones = null, IVarianteVidrioReadPort? variantes = null)
    : IRequestHandler<KardexQuery, ReporteResponse<KardexFila>>
{
    public const string TipoSaldoInicial = "Saldo inicial";

    /// <summary>
    /// M-331: la devolución que corrige una salida y la salida corregida se ven ligadas en el kardex por su folio («Corrige …» /
    /// «Corregida por …»), aunque la salida sea de un mes cerrado y la corrección del mes corriente.
    /// </summary>
    internal static async Task<(Dictionary<string, string> Corrige, Dictionary<string, string> CorregidaPor)> CorreccionesAsync(
        AlmacenDbContext db, IReadOnlyList<LineaKardex> lineas, CancellationToken ct)
    {
        var folios = lineas.Where(l => l.Folio != null && l.Tipo is TipoMovimiento.DevolucionSalida or TipoMovimiento.SalidaConsumo or TipoMovimiento.SalidaPorVale)
            .Select(l => l.Folio!).Distinct().ToList();
        if (folios.Count == 0) return ([], []);
        var pares = await (from d in db.Movimientos.AsNoTracking()
                           join o in db.Movimientos.AsNoTracking() on d.SalidaOrigenId equals (Guid?)o.Id
                           where d.Tipo == TipoMovimiento.DevolucionSalida && d.Estado == EstadoMovimiento.Registrado
                                 && d.Folio != null && o.Folio != null && (folios.Contains(d.Folio) || folios.Contains(o.Folio))
                           select new { Devolucion = d.Folio!, Salida = o.Folio! }).ToListAsync(ct);
        return (pares.GroupBy(p => p.Devolucion).ToDictionary(g => g.Key, g => g.First().Salida),
            pares.GroupBy(p => p.Salida).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(p => p.Devolucion).Distinct())));
    }

    internal static string? ConCorreccion(string? referencia, string? folio, Dictionary<string, string> corrige, Dictionary<string, string> corregidaPor)
    {
        if (folio is null) return referencia;
        var liga = corrige.TryGetValue(folio, out var s) ? $"Corrige {s}" : corregidaPor.TryGetValue(folio, out var d) ? $"Corregida por {d}" : null;
        return liga is null ? referencia : referencia is null ? liga : $"{liga} · {referencia}";
    }

    public async Task<ReporteResponse<KardexFila>> Handle(KardexQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        await AlcanceSucursalAlmacen.VerificarSubAlmacenAsync(db, request.SubAlmacenId, request.SucursalesPermitidas, ct);
        if (request.UbicacionId is { } ub)
        {
            // Una ubicación de otra sucursal no se ve: mismo rechazo que un sub-almacén fuera del alcance.
            var sub = await db.Ubicaciones.AsNoTracking().Where(u => u.Id == ub).Select(u => (Guid?)u.SubAlmacenId).FirstOrDefaultAsync(ct);
            await AlcanceSucursalAlmacen.VerificarSubAlmacenAsync(db, sub, request.SucursalesPermitidas, ct);
        }

        // Todo el histórico del artículo: lo anterior al periodo da el saldo inicial.
        var lineas = (await AlfakHistorialAlmacenHandler
                .LineasKardex(db, request.SubAlmacenId, request.ArticuloId, request.SucursalesPermitidas).ToListAsync(ct))
            .Where(l => l.Tipo != TipoMovimiento.AjustePrecioFactura && l.FechaMovimiento <= request.Hasta
                && (request.UbicacionId == null || l.UbicacionId == request.UbicacionId))
            .OrderBy(l => l.FechaMovimiento).ThenByDescending(l => l.Tipo.EsEntrada()).ThenBy(l => l.Folio, StringComparer.Ordinal)
            .ToList();
        var (ordenes, foliosOc, foliosRq) = await TrazabilidadMpHandler.ResolverReferenciasAsync(db, ocs, requisiciones, lineas, ct);
        var (corrige, corregidaPor) = await CorreccionesAsync(db, lineas, ct);
        var medidas = await TrazabilidadMpHandler.MedidasAsync(variantes, lineas, ct);
        var conHojas = medidas.Count > 0 && lineas.Any(l => MedidaReporte.Hojas(l.ProductoAwVarianteId, l.Cantidad, medidas) != null);

        // La clave de la ubicación solo es única dentro de su sub-almacén: se antepone la clave del almacén.
        var subs = lineas.Select(l => l.SubAlmacenId).Distinct().ToList();
        var almacenes = await (from s in db.SubAlmacenes.AsNoTracking() join a in db.Almacenes.AsNoTracking() on s.AlmacenId equals a.Id
                               where subs.Contains(s.Id) select new { s.Id, a.Clave }).ToDictionaryAsync(x => x.Id, x => x.Clave, ct);

        var filas = new List<KardexFila>();
        decimal saldo = 0m, valor = 0m, entradas = 0m, salidas = 0m, saldoInicial = 0m, valorInicial = 0m, saldoHojas = 0m, hojasIniciales = 0m;
        foreach (var l in lineas)
        {
            var signo = l.Tipo.EsEntrada() ? 1m : -1m;
            saldo += signo * l.Cantidad;
            valor += signo * Math.Round(l.Cantidad * l.CostoUnitarioMxn, 2);
            var hojas = MedidaReporte.Hojas(l.ProductoAwVarianteId, l.Cantidad, medidas) ?? 0m;
            saldoHojas += signo * hojas;
            if (l.FechaMovimiento < request.Desde) { saldoInicial = saldo; valorInicial = valor; hojasIniciales = saldoHojas; continue; }
            if (signo > 0) entradas += l.Cantidad; else salidas += l.Cantidad;
            filas.Add(new KardexFila(l.FechaMovimiento, l.Folio, Etiqueta(l.Tipo), ConCorreccion(TrazabilidadMpHandler.Referencia(l, ordenes, foliosOc, foliosRq), l.Folio, corrige, corregidaPor), $"{almacenes.GetValueOrDefault(l.SubAlmacenId)} · {l.Ubicacion}",
                signo > 0 ? l.Cantidad : 0m, signo < 0 ? l.Cantidad : 0m, saldo, l.CostoUnitarioMxn, CostoPromedio(valor, saldo), valor,
                conHojas && signo > 0 ? hojas : conHojas ? 0m : null, conHojas && signo < 0 ? hojas : conHojas ? 0m : null, conHojas ? saldoHojas : null));
        }
        filas.Insert(0, new KardexFila(request.Desde, null, TipoSaldoInicial, null, null, 0m, 0m, saldoInicial, 0m, CostoPromedio(valorInicial, saldoInicial), valorInicial,
            conHojas ? 0m : null, conHojas ? 0m : null, conHojas ? hojasIniciales : null));

        var art = (await articulos.ObtenerPorIdsAsync([request.ArticuloId], ct)).GetValueOrDefault(request.ArticuloId);
        var filtros = new Dictionary<string, string?>
        {
            ["articulo"] = art is null ? request.ArticuloId.ToString() : $"{art.Clave} · {art.Descripcion}",
            ["desde"] = request.Desde.ToString("yyyy-MM-dd"),
            ["hasta"] = request.Hasta.ToString("yyyy-MM-dd"),
            ["sub_almacen"] = request.SubAlmacenId is { } sa ? await db.SubAlmacenes.AsNoTracking().Where(x => x.Id == sa).Select(x => x.Clave + " · " + x.Nombre).FirstOrDefaultAsync(ct) : null,
            ["ubicacion"] = request.UbicacionId is { } u ? await db.Ubicaciones.AsNoTracking().Where(x => x.Id == u).Select(x => x.Clave + " · " + x.Nombre).FirstOrDefaultAsync(ct) : null,
            ["saldo_inicial"] = Texto(saldoInicial),
            ["saldo_final"] = Texto(saldo),
        };
        var cierre = await SaldoCierreAsync(request, ct);
        if (cierre is { } c)
        {
            filtros["saldo_cierre"] = Texto(c);
            filtros["cuadra_con_cierre"] = c == saldo ? "sí" : "no";
        }
        return new ReporteResponse<KardexFila>(
            Titulo: "KARDEX-ALMACEN",
            GeneradoEn: DateTimeOffset.UtcNow,
            FiltrosAplicados: filtros,
            Columnas: Columnas,
            Filas: filas,
            Totales: new Dictionary<string, decimal> { ["entrada"] = entradas, ["salida"] = salidas, ["saldo"] = saldo, ["valorSaldo"] = valor });
    }

    /// <summary>Existencia del artículo en la foto del cierre del mes de <c>Hasta</c> (solo si es fin de mes, hay foto y no se filtra por ubicación: la foto es por sub-almacén).</summary>
    private async Task<decimal?> SaldoCierreAsync(KardexQuery request, CancellationToken ct)
    {
        var h = request.Hasta;
        if (request.UbicacionId != null || h.Day != DateTime.DaysInMonth(h.Year, h.Month)) return null;
        var foto = db.ValuacionesCierre.AsNoTracking().Where(v => v.Anio == h.Year && v.Mes == h.Month && v.ArticuloId == request.ArticuloId
            && (request.SubAlmacenId == null || v.SubAlmacenId == request.SubAlmacenId)
            && (request.SucursalesPermitidas == null || AlcanceSucursalAlmacen.SubAlmacenesPermitidos(db, request.SucursalesPermitidas).Contains(v.SubAlmacenId)));
        // Cierre ejecutado sin fotos del artículo (no tenía existencia) = cero, siempre que el mes tenga foto.
        return await foto.AnyAsync(ct) ? await foto.SumAsync(v => v.Cantidad, ct)
            : await db.ValuacionesCierre.AnyAsync(v => v.Anio == h.Year && v.Mes == h.Month, ct) ? 0m : null;
    }

    private static decimal CostoPromedio(decimal valor, decimal saldo) => saldo != 0 ? Math.Round(valor / saldo, 4) : 0m;

    private static string Texto(decimal d) => d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    internal static string Etiqueta(TipoMovimiento tipo) => tipo switch
    {
        TipoMovimiento.EntradaCompra => "Entrada por compra",
        TipoMovimiento.SalidaConsumo => "Salida por consumo",
        TipoMovimiento.SalidaPorVale => "Salida por vale",
        TipoMovimiento.DevolucionSalida => "Devolución interna",
        TipoMovimiento.SalidaPorDevolucionAProveedor => "Devolución a proveedor",
        TipoMovimiento.AjustePositivo => "Ajuste positivo",
        TipoMovimiento.AjusteNegativo => "Ajuste negativo",
        TipoMovimiento.BajaPorDano => "Baja por daño",
        TipoMovimiento.ReincorporacionTrasRevision => "Reincorporación tras revisión",
        TipoMovimiento.SalidaPorLiberacionRevision => "Salida por liberación de revisión",
        TipoMovimiento.CancelacionEntradaCompra => "Cancelación de entrada",
        TipoMovimiento.SalidaNoPlanificada => "Salida no planificada",
        TipoMovimiento.TrasladoInterno => "Traslado interno (salida)",
        TipoMovimiento.EntradaTrasladoInterno => "Traslado interno (entrada)",
        _ => tipo.ToString(),
    };

    public static readonly IReadOnlyList<ColumnaDescriptor> Columnas =
    [
        new("fecha", "Fecha", "fecha"),
        new("folio", "Folio", "texto"),
        new("tipo", "Tipo de movimiento", "texto"),
        new("documento", "Documento origen", "texto"),
        new("ubicacion", "Ubicación", "texto"),
        new("entrada", "Entrada", "numero", "right"),
        new("salida", "Salida", "numero", "right"),
        new("saldo", "Saldo", "numero", "right"),
        new("costoUnitarioMxn", "Costo unit.", "moneda", "right"),
        new("costoPromedioMxn", "Costo prom.", "moneda", "right"),
        new("valorSaldo", "Valor", "moneda", "right"),
        new("entradaHojas", "Entrada (hojas)", "numero", "right"),
        new("salidaHojas", "Salida (hojas)", "numero", "right"),
        new("saldoHojas", "Saldo (hojas)", "numero", "right"),
    ];
}
