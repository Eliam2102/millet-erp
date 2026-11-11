using System.Text.RegularExpressions;
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
/// F1-MP-09: trazabilidad de un artículo (y, opcional, un lote del proveedor): compras (OC y recepción), consumos (salidas,
/// incluido el consumo de componentes de A+W), merma/rotura y el saldo corrido con su valor. Solo consulta: el lote es detalle
/// del movimiento, no se lleva existencia por lote. Reusa el kardex del ALFAK (<see cref="AlfakHistorialAlmacenHandler.LineasKardex"/>).
/// </summary>
public sealed record TrazabilidadMpQuery(
    Guid ArticuloId,
    DateOnly Desde,
    DateOnly Hasta,
    string? Lote = null,
    Guid? SubAlmacenId = null) : IRequest<ReporteResponse<TrazabilidadMpFila>>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "almacen.reportes.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class TrazabilidadMpQueryValidator : AbstractValidator<TrazabilidadMpQuery>
{
    public TrazabilidadMpQueryValidator()
    {
        RuleFor(x => x.ArticuloId).NotEmpty().WithMessage("El artículo es obligatorio.");
        RuleFor(x => x).Must(x => x.Desde <= x.Hasta)
            .WithMessage("La fecha inicial no puede ser posterior a la final.")
            .WithName("Desde");
    }
}

/// <summary>Una línea de movimiento del kardex del artículo. <c>Saldo</c> y <c>ValorSaldo</c> son corridos (incluyen el saldo inicial).</summary>
public sealed record TrazabilidadMpFila(
    DateOnly Fecha,
    string? Folio,
    string Concepto,
    string? Referencia,
    string? Lote,
    string? SubAlmacenNombre,
    decimal Entrada,
    decimal Salida,
    decimal Saldo,
    decimal CostoUnitarioMxn,
    decimal ValorSaldo,
    // Hojas (unidad física) del vidrio por medida, como la columna Hojas del ALFAK; null = sin medida. Las cantidades siguen en m².
    decimal? HojasEntrada = null,
    decimal? HojasSalida = null,
    decimal? HojasSaldo = null);

public sealed partial class TrazabilidadMpHandler(AlmacenDbContext db, IArticuloReadPort articulos, IComprasOcReadPort? ocs = null,
    IComprasRequisicionReadPort? requisiciones = null, IVarianteVidrioReadPort? variantes = null)
    : IRequestHandler<TrazabilidadMpQuery, ReporteResponse<TrazabilidadMpFila>>
{
    public const string ConceptoSaldoInicial = "Saldo inicial";
    public const string ConceptoCompra = "Compra";
    public const string ConceptoConsumo = "Consumo";
    public const string ConceptoMerma = "Merma / rotura";
    public const string ConceptoAjuste = "Ajuste / devolución";
    public const string ConceptoCancelacion = "Cancelación de recepción";

    public async Task<ReporteResponse<TrazabilidadMpFila>> Handle(TrazabilidadMpQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        await AlcanceSucursalAlmacen.VerificarSubAlmacenAsync(db, request.SubAlmacenId, request.SucursalesPermitidas, ct);
        var lote = LineaMovimiento.NormalizarLote(request.Lote);

        // Todo el histórico del artículo: lo anterior al periodo da el saldo inicial. El lote se filtra en memoria (volumen por artículo).
        var lineas = (await AlfakHistorialAlmacenHandler
                .LineasKardex(db, request.SubAlmacenId, request.ArticuloId, request.SucursalesPermitidas).ToListAsync(ct))
            .Where(l => l.Tipo != TipoMovimiento.AjustePrecioFactura && l.FechaMovimiento <= request.Hasta && (lote == null || l.Lote == lote))
            .OrderBy(l => l.FechaMovimiento).ThenByDescending(l => l.Tipo.EsEntrada()).ThenBy(l => l.Folio, StringComparer.Ordinal)
            .ToList();

        var folios = lineas.Where(l => l.Folio != null && l.Tipo == TipoMovimiento.SalidaConsumo).Select(l => l.Folio!).Distinct().ToList();
        var ordenes = folios.Count == 0 ? new Dictionary<string, string>()
            : (await db.ConsumosComponenteAw.AsNoTracking().Where(c => c.Folio != null && folios.Contains(c.Folio)).Select(c => new { Folio = c.Folio!, c.Auftnr }).ToListAsync(ct))
                .GroupBy(c => c.Folio).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(c => c.Auftnr).Distinct()));
        var ocIds = lineas.Where(l => l.OcId != null).Select(l => l.OcId!.Value).Distinct().ToList();
        var foliosOc = ocs is null || ocIds.Count == 0 ? new Dictionary<Guid, string>()
            : (await ocs.ObtenerFoliosAsync(ocIds, ct)).ToDictionary(x => x.Key, x => x.Value);
        var rqIds = lineas.Where(l => l.RqId != null).Select(l => l.RqId!.Value).Distinct().ToList();
        var foliosRq = requisiciones is null || rqIds.Count == 0 ? new Dictionary<Guid, string>()
            : (await requisiciones.ObtenerFoliosAsync(rqIds, ct)).ToDictionary(x => x.Key, x => x.Value);
        var medidas = variantes is null ? new Dictionary<Guid, VarianteVidrioLectura>()
            : (await variantes.ObtenerPorIdsAsync(lineas.Where(l => l.ProductoAwVarianteId != null).Select(l => l.ProductoAwVarianteId!.Value).Distinct().ToArray(), ct))
                .ToDictionary(x => x.Key, x => x.Value);
        decimal? Hojas(LineaKardex l) => MedidaReporte.Hojas(l.ProductoAwVarianteId, l.Cantidad, medidas);

        var filas = new List<TrazabilidadMpFila>();
        decimal saldo = 0m, valor = 0m, entradas = 0m, salidas = 0m;
        decimal? hojasSaldo = null;
        foreach (var l in lineas)
        {
            var signo = l.Tipo.EsEntrada() ? 1m : -1m;
            saldo += signo * l.Cantidad;
            valor += signo * Math.Round(l.Cantidad * l.CostoUnitarioMxn, 2);
            var hojas = Hojas(l);
            if (hojas != null) hojasSaldo = (hojasSaldo ?? 0m) + signo * hojas;
            if (l.FechaMovimiento < request.Desde) continue;
            if (signo > 0) entradas += l.Cantidad; else salidas += l.Cantidad;
            filas.Add(new TrazabilidadMpFila(l.FechaMovimiento, l.Folio, Concepto(l.Tipo), Referencia(l, ordenes, foliosOc, foliosRq), l.Lote,
                l.SubAlmacenNombre, signo > 0 ? l.Cantidad : 0m, signo < 0 ? l.Cantidad : 0m, saldo, l.CostoUnitarioMxn, valor,
                signo > 0 ? hojas : null, signo < 0 ? hojas : null, hojasSaldo));
        }
        // Saldo inicial = el saldo corrido justo antes de la primera fila del periodo.
        var previas = lineas.Where(l => l.FechaMovimiento < request.Desde).ToList();
        var inicial = previas.Sum(l => (l.Tipo.EsEntrada() ? 1m : -1m) * l.Cantidad);
        var valorInicial = previas.Sum(l => (l.Tipo.EsEntrada() ? 1m : -1m) * Math.Round(l.Cantidad * l.CostoUnitarioMxn, 2));
        if (previas.Count > 0)
            filas.Insert(0, new TrazabilidadMpFila(request.Desde, null, ConceptoSaldoInicial, null, lote, null, 0m, 0m, inicial, 0m, valorInicial,
                HojasSaldo: previas.Select(l => Hojas(l) is { } h ? (l.Tipo.EsEntrada() ? h : -h) : (decimal?)null).Where(h => h != null).Sum()));

        var art = (await articulos.ObtenerPorIdsAsync([request.ArticuloId], ct)).GetValueOrDefault(request.ArticuloId);
        return new ReporteResponse<TrazabilidadMpFila>(
            Titulo: "TRAZABILIDAD-MP",
            GeneradoEn: DateTimeOffset.UtcNow,
            FiltrosAplicados: new Dictionary<string, string?>
            {
                ["articulo"] = art is null ? request.ArticuloId.ToString() : $"{art.Clave} · {art.Descripcion}",
                ["lote"] = lote,
                ["desde"] = request.Desde.ToString("yyyy-MM-dd"),
                ["hasta"] = request.Hasta.ToString("yyyy-MM-dd"),
                ["sub_almacen_id"] = request.SubAlmacenId?.ToString(),
            },
            Columnas: Columnas,
            Filas: filas,
            Totales: new Dictionary<string, decimal> { ["entrada"] = entradas, ["salida"] = salidas, ["saldo"] = saldo, ["valorSaldo"] = valor });
    }

    internal static string Concepto(TipoMovimiento tipo) => tipo switch
    {
        TipoMovimiento.EntradaCompra => ConceptoCompra,
        TipoMovimiento.SalidaConsumo or TipoMovimiento.SalidaPorVale => ConceptoConsumo,
        TipoMovimiento.BajaPorDano => ConceptoMerma,
        TipoMovimiento.CancelacionEntradaCompra => ConceptoCancelacion,
        _ => ConceptoAjuste,
    };

    private static string? Referencia(LineaKardex l, Dictionary<string, string> ordenes, Dictionary<Guid, string> foliosOc, Dictionary<Guid, string> foliosRq)
    {
        if (l.OcId is { } oc) return foliosOc.TryGetValue(oc, out var f) ? $"OC {f}" : "OC";
        if (l.RqId is { } rq) return foliosRq.TryGetValue(rq, out var r) ? $"RQ {r}" : "RQ";
        if (l.Folio != null && ordenes.TryGetValue(l.Folio, out var o)) return $"Orden A+W {o}";
        // Salida de la app de Almacenes: el pedido A+W del mensaje viaja en el motivo («… pedido 123»).
        if (l.Motivo != null && PedidoAw().Match(l.Motivo) is { Success: true } p) return $"Orden A+W {p.Groups[1].Value}";
        return l.Motivo;
    }

    [GeneratedRegex(@"\bpedido (\S+)$")]
    private static partial Regex PedidoAw();

    public static readonly IReadOnlyList<ColumnaDescriptor> Columnas =
    [
        new("fecha", "Fecha", "fecha"),
        new("folio", "Folio", "texto"),
        new("concepto", "Concepto", "texto"),
        new("referencia", "OC / orden / motivo", "texto"),
        new("lote", "Lote", "texto"),
        new("subAlmacenNombre", "Sub-almacén", "texto"),
        new("entrada", "Entrada", "numero", "right"),
        new("salida", "Salida", "numero", "right"),
        new("saldo", "Saldo", "numero", "right"),
        new("costoUnitarioMxn", "Costo unit.", "moneda", "right"),
        new("valorSaldo", "Valor saldo", "moneda", "right"),
        new("hojasEntrada", "Hojas entrada", "numero", "right"),
        new("hojasSalida", "Hojas salida", "numero", "right"),
        new("hojasSaldo", "Hojas saldo", "numero", "right"),
    ];
}
