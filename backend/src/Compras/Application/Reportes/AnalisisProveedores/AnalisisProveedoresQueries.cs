using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Administracion;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Domain.Ports.CuentasPorPagar;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Fila = System.Collections.Generic.IReadOnlyDictionary<string, object?>;

namespace Millet.Compras.Application.Reportes.AnalisisProveedores;

public enum AgruparComprasPor { Proveedor, Articulo, Departamento, Mes }

public enum IndicadorDetalle { Compras, Saldos, ATiempo, Completo, Incidencias, DiferenciasPrecio }

// ---------- Compras por periodo ----------

public sealed record ConsultarComprasPeriodoQuery(FiltrosAnalisis Filtros, AgruparComprasPor AgruparPor = AgruparComprasPor.Proveedor)
    : AnalisisProveedoresQueryBase(Filtros), IRequest<AnalisisProveedoresResponse>;

public sealed class ConsultarComprasPeriodoValidator : AbstractValidator<ConsultarComprasPeriodoQuery>
{
    public ConsultarComprasPeriodoValidator() => RuleFor(q => q.Filtros).SetValidator(new FiltrosAnalisisValidator());
}

public sealed class ConsultarComprasPeriodoHandler(ComprasDbContext db, NombresAnalisis nombres, IClock clock)
    : IRequestHandler<ConsultarComprasPeriodoQuery, AnalisisProveedoresResponse>
{
    public async Task<AnalisisProveedoresResponse> Handle(ConsultarComprasPeriodoQuery q, CancellationToken cancellationToken)
    {
        var f = q.Filtros;
        var ocs = await OcAnalisisCargador.CargarAsync(OcAnalisisCargador.Base(db, q).DelPeriodo(f), cancellationToken);
        var lineas = ocs.SelectMany(oc => OcAnalisisCargador.LineasFiltradas(oc, f).Select(l => (oc, l))).ToList();
        await nombres.CargarAsync(ocs.Select(o => o.ProveedorId), lineas.Select(x => x.l.ArticuloId),
            lineas.Select(x => x.l.DepartamentoSolicitanteId), cancellationToken);

        (Guid? Id, string Nombre) Grupo(OrdenCompra oc, LineaOrdenCompra l) => q.AgruparPor switch
        {
            AgruparComprasPor.Articulo => (l.ArticuloId, nombres.Articulo(l.ArticuloId)),
            AgruparComprasPor.Departamento => (l.DepartamentoSolicitanteId, nombres.Departamento(l.DepartamentoSolicitanteId)),
            AgruparComprasPor.Mes => (null, $"{oc.FechaDocumento:yyyy-MM}"),
            _ => (oc.ProveedorId, nombres.Proveedor(oc.ProveedorId)),
        };

        var filas = lineas
            .GroupBy(x => (Grupo: Grupo(x.oc, x.l), x.oc.Moneda))
            .OrderBy(g => g.Key.Grupo.Nombre).ThenBy(g => g.Key.Moneda)
            .Select(g =>
            {
                var ordenado = g.Sum(x => ReglasAnalisisProveedores.Ordenado(x.l));
                var recibido = g.Sum(x => ReglasAnalisisProveedores.RecibidoValorizado(x.l));
                return (Fila)new Dictionary<string, object?>
                {
                    ["grupoId"] = g.Key.Grupo.Id,
                    ["grupo"] = g.Key.Grupo.Nombre,
                    ["moneda"] = g.Key.Moneda,
                    ["ordenes"] = g.Select(x => x.oc.Id).Distinct().Count(),
                    ["ordenado"] = ordenado,
                    ["recibido"] = recibido,
                    ["pendienteFisico"] = ordenado - recibido,
                };
            }).ToList();

        var etiquetaGrupo = q.AgruparPor switch
        {
            AgruparComprasPor.Articulo => "Artículo",
            AgruparComprasPor.Departamento => "Departamento",
            AgruparComprasPor.Mes => "Mes",
            _ => "Proveedor",
        };
        var reporte = new ReporteJsonResponse("Compras por periodo", clock.UtcNow,
            ReporteBuilder.Filtros(f, NombreProveedorFiltro(f, nombres)),
            [ReporteBuilder.Texto("grupo", etiquetaGrupo), ReporteBuilder.Texto("moneda", "Moneda"),
             ReporteBuilder.Entero("ordenes", "Órdenes"), ReporteBuilder.Importe("ordenado", "Ordenado"),
             ReporteBuilder.Importe("recibido", "Recibido"),
             ReporteBuilder.Importe("pendienteFisico", "Pendiente físico valorizado (no es deuda)")],
            filas, null);
        // Una OC con varios artículos o departamentos sale en varias filas: el
        // total de órdenes se cuenta distinto por moneda, no se suma.
        var ordenesPorMoneda = lineas.GroupBy(x => x.oc.Moneda).ToDictionary(g => g.Key, g => g.Select(x => x.oc.Id).Distinct().Count());
        var totales = ReporteBuilder.TotalesPorMoneda(filas, "ordenado", "recibido", "pendienteFisico")
            .Select(t => (Fila)new Dictionary<string, object?>(t) { ["ordenes"] = ordenesPorMoneda[(string)t["moneda"]!] })
            .ToList();
        return new(reporte, totales);
    }

    internal static string? NombreProveedorFiltro(FiltrosAnalisis f, NombresAnalisis nombres) =>
        f.ProveedorId is Guid p ? nombres.Proveedor(p) : null;
}

// ---------- Saldos pendientes por OC ----------

public sealed record ConsultarSaldosOcQuery(FiltrosAnalisis Filtros)
    : AnalisisProveedoresQueryBase(Filtros), IRequest<AnalisisProveedoresResponse>;

public sealed class ConsultarSaldosOcValidator : AbstractValidator<ConsultarSaldosOcQuery>
{
    public ConsultarSaldosOcValidator() => RuleFor(q => q.Filtros).SetValidator(new FiltrosAnalisisValidator());
}

public sealed class ConsultarSaldosOcHandler(ComprasDbContext db, NombresAnalisis nombres, IClock clock)
    : IRequestHandler<ConsultarSaldosOcQuery, AnalisisProveedoresResponse>
{
    public async Task<AnalisisProveedoresResponse> Handle(ConsultarSaldosOcQuery q, CancellationToken cancellationToken)
    {
        var f = q.Filtros;
        var abiertas = (await OcAnalisisCargador.CargarAsync(OcAnalisisCargador.Base(db, q).DelPeriodo(f), cancellationToken))
            .Where(SaldosOc.Abierta).OrderBy(o => o.FechaDocumento).ThenBy(OcAnalisisCargador.Folio).ToList();

        // Saldo inicial: OC firmes anteriores al periodo que seguían abiertas al
        // inicio (owner, 22-oct-2026). ponytail: usa los acumulados actuales,
        // no reconstruye lo recibido al día de corte; una OC cerrada después
        // del inicio queda con pendiente 0, y las canceladas no cuentan.
        // El margen de un día cubre el desfase UTC; el corte fino es en hora local (ADR-0040).
        var margen = new DateTimeOffset(f.Desde.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var anteriores = (await OcAnalisisCargador.CargarAsync(OcAnalisisCargador.Base(db, q)
            .Where(o => o.FechaDocumento < f.Desde)
            .Where(o => o.Estado != EstadoOrdenCompra.Cerrada || o.FechaCierre == null || o.FechaCierre >= margen), cancellationToken))
            .Where(o => o.Estado != EstadoOrdenCompra.Cerrada || o.FechaCierre is not { } cierre
                || FechaContable.HoyLocal(cierre) >= f.Desde);
        var saldoInicial = anteriores
            .Select(o => (o.Moneda, Pendiente: SaldosOc.PorRecibir(o, OcAnalisisCargador.LineasFiltradas(o, f))))
            .Where(x => x.Pendiente > 0m)
            .GroupBy(x => x.Moneda).OrderBy(g => g.Key)
            .Select(g => new SaldoInicialMoneda(g.Key, g.Count(), g.Sum(x => x.Pendiente)))
            .ToList();

        await nombres.CargarAsync(abiertas.Select(o => o.ProveedorId), [], [], cancellationToken);
        var filas = abiertas.Select(o => (Fila)new Dictionary<string, object?>
        {
            ["ocId"] = o.Id,
            ["folio"] = OcAnalisisCargador.Folio(o),
            ["proveedorId"] = o.ProveedorId,
            ["proveedor"] = nombres.Proveedor(o.ProveedorId),
            ["moneda"] = o.Moneda,
            ["fechaDocumento"] = o.FechaDocumento,
            ["estado"] = o.Estado.ToString(),
            ["ordenado"] = o.Lineas.Sum(ReglasAnalisisProveedores.Ordenado),
            ["porRecibir"] = SaldosOc.PorRecibir(o),
            ["porFacturar"] = o.Lineas.Sum(ReglasAnalisisProveedores.PorFacturar),
            ["porPagar"] = ReglasAnalisisProveedores.PorPagar(o),
            ["subEstadoRecepcion"] = o.SubEstadoRecepcion.ToString(),
            ["subEstadoFacturacion"] = o.SubEstadoFacturacion.ToString(),
            ["subEstadoPago"] = o.SubEstadoPago.ToString(),
        }).ToList();

        var reporte = new ReporteJsonResponse("Saldos pendientes por orden de compra", clock.UtcNow,
            ReporteBuilder.Filtros(f, ConsultarComprasPeriodoHandler.NombreProveedorFiltro(f, nombres)),
            [ReporteBuilder.Texto("folio", "Orden"), ReporteBuilder.Texto("proveedor", "Proveedor"),
             ReporteBuilder.Fecha("fechaDocumento", "Fecha"), ReporteBuilder.Texto("moneda", "Moneda"),
             ReporteBuilder.Importe("ordenado", "Ordenado"), ReporteBuilder.Importe("porRecibir", "Por recibir"),
             ReporteBuilder.Importe("porFacturar", "Por facturar"), ReporteBuilder.Importe("porPagar", "Por pagar")],
            filas, null);
        return new(reporte, ReporteBuilder.TotalesPorMoneda(filas, "ordenado", "porRecibir", "porFacturar", "porPagar"), saldoInicial);
    }
}

internal static class SaldosOc
{
    /// <summary>
    /// OC firme no cerrada con alguna dimensión abierta: mismo criterio que el
    /// reporte de partidas abiertas (<c>ListarPartidasAbiertasHandler</c>), para
    /// que ambos conjuntos coincidan con los mismos filtros.
    /// </summary>
    internal static bool Abierta(OrdenCompra o) =>
        o.Estado != EstadoOrdenCompra.Cerrada
        && (o.SubEstadoRecepcion != SubEstadoRecepcion.Completa
            || o.SubEstadoFacturacion != SubEstadoFacturacion.Completa
            || o.SubEstadoPago != SubEstadoPago.Pagada);

    /// <summary>Pendiente físico valorizado; los servicios no se reciben en almacén.</summary>
    internal static decimal PorRecibir(OrdenCompra o) => PorRecibir(o, o.Lineas);

    internal static decimal PorRecibir(OrdenCompra o, IEnumerable<LineaOrdenCompra> lineas) =>
        lineas.Where(l => !l.EsServicio)
            .Sum(l => Math.Round(Math.Max(0m, l.Cantidad - l.CantidadRecibida) * ReglasAnalisisProveedores.PrecioNeto(l), 2));
}

// ---------- Cumplimiento de proveedores ----------

public sealed record ConsultarCumplimientoProveedoresQuery(FiltrosAnalisis Filtros)
    : AnalisisProveedoresQueryBase(Filtros), IRequest<AnalisisProveedoresResponse>;

public sealed class ConsultarCumplimientoProveedoresValidator : AbstractValidator<ConsultarCumplimientoProveedoresQuery>
{
    public ConsultarCumplimientoProveedoresValidator() => RuleFor(q => q.Filtros).SetValidator(new FiltrosAnalisisValidator());
}

public sealed class ConsultarCumplimientoProveedoresHandler(
    ComprasDbContext db, NombresAnalisis nombres, IRecepcionesOcReadPort recepciones, IFacturasOcReadPort facturas, IClock clock)
    : IRequestHandler<ConsultarCumplimientoProveedoresQuery, AnalisisProveedoresResponse>
{
    public async Task<AnalisisProveedoresResponse> Handle(ConsultarCumplimientoProveedoresQuery q, CancellationToken cancellationToken)
    {
        var f = q.Filtros;
        var ocs = await OcAnalisisCargador.CargarAsync(OcAnalisisCargador.Base(db, q).DelPeriodo(f), cancellationToken);
        var datos = await DatosEvaluacion.CargarAsync(ocs, recepciones, facturas, cancellationToken);
        var hoy = clock.HoyLocal();
        await nombres.CargarAsync(ocs.Select(o => o.ProveedorId), [], [], cancellationToken);

        // COM-13-b: sólo aparecen proveedores con OC firmes en el periodo.
        var filas = ocs.GroupBy(o => o.ProveedorId)
            .Select(g =>
            {
                var evaluaciones = g.SelectMany(oc => OcAnalisisCargador.LineasFiltradas(oc, f)
                    .Select(l => ReglasAnalisisProveedores.Evaluar(oc, l, datos.Movimientos(l.Id), hoy))).ToList();
                var lineasIds = g.SelectMany(oc => OcAnalisisCargador.LineasFiltradas(oc, f)).Select(l => l.Id).ToHashSet();
                var incidencias = g.SelectMany(oc => oc.Incidencias).Where(i => lineasIds.Contains(i.LineaOrdenCompraId)).ToList();
                var conDiferencia = g.SelectMany(oc => datos.FacturasConDiferencia(oc.Id)).ToList();
                var evaluables = evaluaciones.Count(e => e.Evaluable);
                var aTiempo = evaluaciones.Count(e => e.ATiempo == true);
                var completables = evaluaciones.Count(e => e.Completable);
                var completas = evaluaciones.Count(e => e.Completa == true);
                return (Fila)new Dictionary<string, object?>
                {
                    ["proveedorId"] = g.Key,
                    ["proveedor"] = nombres.Proveedor(g.Key),
                    ["ordenes"] = g.Count(),
                    ["lineasEvaluables"] = evaluables,
                    ["lineasATiempo"] = aTiempo,
                    ["porcentajeATiempo"] = ReglasAnalisisProveedores.Porcentaje(aTiempo, evaluables),
                    ["lineasCompletables"] = completables,
                    ["lineasCompletas"] = completas,
                    ["porcentajeCompleto"] = ReglasAnalisisProveedores.Porcentaje(completas, completables),
                    ["incidencias"] = incidencias.Count,
                    ["incidenciasAbiertas"] = incidencias.Count(i => i.Abierta),
                    ["incidenciasVencidas"] = incidencias.Count(i => i.Abierta && i.FechaCompromisoVigente < hoy),
                    ["facturasConDiferencia"] = conDiferencia.Count,
                    ["diferenciaPrecio"] = conDiferencia.Count == 0 ? "—" : string.Join(" · ",
                        conDiferencia.GroupBy(x => x.Moneda).OrderBy(x => x.Key)
                            .Select(x => $"{x.Key} {x.Sum(y => y.DiferenciaContraOc):N2}")),
                };
            })
            .OrderBy(x => (string)x["proveedor"]!).ToList();

        var reporte = new ReporteJsonResponse("Cumplimiento de proveedores", clock.UtcNow,
            ReporteBuilder.Filtros(f, ConsultarComprasPeriodoHandler.NombreProveedorFiltro(f, nombres)),
            [ReporteBuilder.Texto("proveedor", "Proveedor"), ReporteBuilder.Entero("ordenes", "Órdenes"),
             ReporteBuilder.Entero("lineasEvaluables", "Líneas evaluables"), ReporteBuilder.Entero("lineasATiempo", "A tiempo"),
             ReporteBuilder.Numero("porcentajeATiempo", "% a tiempo"),
             ReporteBuilder.Entero("lineasCompletables", "Líneas evaluables (completo)"), ReporteBuilder.Entero("lineasCompletas", "Completas"),
             ReporteBuilder.Numero("porcentajeCompleto", "% completo"),
             ReporteBuilder.Entero("incidencias", "Incidencias"), ReporteBuilder.Entero("incidenciasAbiertas", "Abiertas"),
             ReporteBuilder.Entero("incidenciasVencidas", "Vencidas"),
             ReporteBuilder.Entero("facturasConDiferencia", "Facturas con diferencia de precio"),
             ReporteBuilder.Texto("diferenciaPrecio", "Diferencia de precio")],
            filas, null);
        return new(reporte, [], Formulas: ReglasAnalisisProveedores.Formulas);
    }
}

/// <summary>Recepciones (Almacén) y facturas (CxP) de las OC, leídas en lote por los puertos de la tríada.</summary>
internal sealed class DatosEvaluacion
{
    private readonly ILookup<Guid, RecepcionOcLinea> _porLinea;
    private readonly ILookup<Guid, FacturaOcLectura> _facturasPorOc;

    private DatosEvaluacion(IReadOnlyList<RecepcionOcLinea> movimientos, IReadOnlyList<FacturaOcLectura> facturas)
    {
        _porLinea = movimientos.ToLookup(m => m.LineaOcId);
        _facturasPorOc = facturas.ToLookup(x => x.OcId);
    }

    internal static async Task<DatosEvaluacion> CargarAsync(IReadOnlyCollection<OrdenCompra> ocs,
        IRecepcionesOcReadPort recepciones, IFacturasOcReadPort facturas, CancellationToken cancellationToken)
    {
        var ids = ocs.Select(o => o.Id).ToArray();
        return new(await recepciones.ListarPorOcAsync(ids, cancellationToken), await facturas.ListarPorOcAsync(ids, cancellationToken));
    }

    internal IEnumerable<(DateOnly Fecha, decimal Cantidad)> Movimientos(Guid lineaId) =>
        _porLinea[lineaId].Select(m => (m.Fecha, m.Cantidad));

    internal IEnumerable<RecepcionOcLinea> Recepciones(Guid lineaId) => _porLinea[lineaId];

    internal IEnumerable<FacturaOcLectura> Facturas(Guid ocId) => _facturasPorOc[ocId];

    internal IEnumerable<FacturaOcLectura> FacturasConDiferencia(Guid ocId) =>
        _facturasPorOc[ocId].Where(x => x.Estado != "Cancelada" && x.DiferenciaContraOc != 0m);
}

// ---------- Detalle (drill-down) ----------

public sealed record ConsultarOrdenesAnalisisQuery(FiltrosAnalisis Filtros, IndicadorDetalle Indicador = IndicadorDetalle.Compras)
    : AnalisisProveedoresQueryBase(Filtros), IRequest<OrdenesAnalisisResponse>;

public sealed class ConsultarOrdenesAnalisisValidator : AbstractValidator<ConsultarOrdenesAnalisisQuery>
{
    public ConsultarOrdenesAnalisisValidator() => RuleFor(q => q.Filtros).SetValidator(new FiltrosAnalisisValidator());
}

public sealed record OrdenesAnalisisResponse(IReadOnlyList<OrdenAnalisisItem> Items);

public sealed record OrdenAnalisisItem(
    Guid OcId, string Folio, Guid ProveedorId, string Proveedor, string Moneda, DateOnly FechaDocumento, string Estado,
    decimal Ordenado, decimal PorRecibir, decimal PorFacturar, decimal PorPagar,
    PendientesOc Pendientes, int Incidencias,
    IReadOnlyList<LineaAnalisisItem> Lineas, IReadOnlyList<FacturaAnalisisItem> Facturas);

public sealed record PendientesOc(bool Recepcion, bool Facturacion, bool Pago);

public sealed record LineaAnalisisItem(
    Guid LineaId, int Numero, string Articulo, decimal Cantidad, decimal RecibidoNeto,
    DateOnly? Compromiso, string? OrigenCompromiso, DateOnly? FechaCompletada,
    bool Evaluable, bool? ATiempo, bool? Completa);

public sealed record FacturaAnalisisItem(
    Guid FacturaId, string? Serie, string? Folio, DateOnly Fecha, string Moneda, decimal Total, decimal DiferenciaContraOc, string Estado);

public sealed class ConsultarOrdenesAnalisisHandler(
    ComprasDbContext db, NombresAnalisis nombres, IRecepcionesOcReadPort recepciones, IFacturasOcReadPort facturas, IClock clock)
    : IRequestHandler<ConsultarOrdenesAnalisisQuery, OrdenesAnalisisResponse>
{
    public async Task<OrdenesAnalisisResponse> Handle(ConsultarOrdenesAnalisisQuery q, CancellationToken cancellationToken)
    {
        var f = q.Filtros;
        var ocs = await OcAnalisisCargador.CargarAsync(OcAnalisisCargador.Base(db, q).DelPeriodo(f), cancellationToken);
        var datos = await DatosEvaluacion.CargarAsync(ocs, recepciones, facturas, cancellationToken);
        var hoy = clock.HoyLocal();
        await nombres.CargarAsync(ocs.Select(o => o.ProveedorId), ocs.SelectMany(o => o.Lineas).Select(l => l.ArticuloId), [], cancellationToken);

        var items = new List<OrdenAnalisisItem>();
        foreach (var oc in ocs.OrderBy(o => o.FechaDocumento).ThenBy(OcAnalisisCargador.Folio))
        {
            var lineas = OcAnalisisCargador.LineasFiltradas(oc, f).OrderBy(l => l.Posicion)
                .Select(l => (l, e: ReglasAnalisisProveedores.Evaluar(oc, l, datos.Movimientos(l.Id), hoy))).ToList();
            var lineasIds = lineas.Select(x => x.l.Id).ToHashSet();
            var incidencias = oc.Incidencias.Count(i => lineasIds.Contains(i.LineaOrdenCompraId));
            var incluir = q.Indicador switch
            {
                IndicadorDetalle.Saldos => SaldosOc.Abierta(oc),
                IndicadorDetalle.ATiempo => lineas.Any(x => x.e.Evaluable),
                IndicadorDetalle.Completo => lineas.Any(x => x.e.Completable),
                IndicadorDetalle.Incidencias => incidencias > 0,
                IndicadorDetalle.DiferenciasPrecio => datos.FacturasConDiferencia(oc.Id).Any(),
                _ => lineas.Count > 0,
            };
            if (!incluir) continue;

            var porRecibir = SaldosOc.PorRecibir(oc);
            items.Add(new OrdenAnalisisItem(
                oc.Id, OcAnalisisCargador.Folio(oc), oc.ProveedorId, nombres.Proveedor(oc.ProveedorId), oc.Moneda,
                oc.FechaDocumento, oc.Estado.ToString(),
                oc.Lineas.Sum(ReglasAnalisisProveedores.Ordenado), porRecibir,
                oc.Lineas.Sum(ReglasAnalisisProveedores.PorFacturar), ReglasAnalisisProveedores.PorPagar(oc),
                new PendientesOc(porRecibir > 0m, oc.SubEstadoFacturacion != SubEstadoFacturacion.Completa,
                    oc.SubEstadoPago != SubEstadoPago.Pagada),
                incidencias,
                lineas.Select(x => new LineaAnalisisItem(x.l.Id, x.l.Posicion, nombres.Articulo(x.l.ArticuloId), x.l.Cantidad,
                    x.l.CantidadRecibida, x.e.Compromiso, x.e.OrigenCompromiso, x.e.FechaCompletada, x.e.Evaluable,
                    x.e.ATiempo, x.e.Completa)).ToList(),
                datos.Facturas(oc.Id).Select(x => new FacturaAnalisisItem(x.FacturaId, x.Serie, x.Folio, x.Fecha, x.Moneda,
                    x.Total, x.DiferenciaContraOc, x.Estado)).ToList()));
        }
        return new(items);
    }
}

/// <summary>Nombres de proveedor, artículo y departamento, leídos en lote por los puertos existentes.</summary>
public sealed class NombresAnalisis(IProveedorReadPort proveedores, IArticuloReadPort articulos, IDepartamentoReadPort departamentos)
{
    private IReadOnlyDictionary<Guid, ProveedorLectura> _proveedores = new Dictionary<Guid, ProveedorLectura>();
    private IReadOnlyDictionary<Guid, ArticuloLectura> _articulos = new Dictionary<Guid, ArticuloLectura>();
    private IReadOnlyDictionary<Guid, DepartamentoNombreLectura> _departamentos = new Dictionary<Guid, DepartamentoNombreLectura>();

    public async Task CargarAsync(IEnumerable<Guid> proveedorIds, IEnumerable<Guid> articuloIds, IEnumerable<Guid> departamentoIds, CancellationToken cancellationToken)
    {
        var p = proveedorIds.Distinct().ToArray();
        var a = articuloIds.Distinct().ToArray();
        var d = departamentoIds.Distinct().ToArray();
        if (p.Length > 0) _proveedores = await proveedores.ObtenerPorIdsAsync(p, cancellationToken);
        if (a.Length > 0) _articulos = await articulos.ObtenerPorIdsAsync(a, cancellationToken);
        if (d.Length > 0) _departamentos = await departamentos.ObtenerAsync(d, cancellationToken);
    }

    public string Proveedor(Guid id) => _proveedores.TryGetValue(id, out var x) ? x.RazonSocial : id.ToString();
    public string Articulo(Guid id) => _articulos.TryGetValue(id, out var x) ? $"{x.Clave} · {x.Nombre}" : id.ToString();
    public string Departamento(Guid id) => _departamentos.TryGetValue(id, out var x) ? x.Nombre : id.ToString();
}
