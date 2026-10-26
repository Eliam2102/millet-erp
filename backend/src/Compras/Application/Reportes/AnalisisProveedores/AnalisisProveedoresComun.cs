using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.Compras.Application.Reportes.AnalisisProveedores;

/// <summary>
/// Filtros comunes del análisis de compras y proveedores (F1-COM-13).
/// Plano 1.7.1 «Consultar trazabilidad y reportes de compras», paso 1:
/// sucursal autorizada, proveedor, periodo, moneda y estado; la ficha
/// agrega artículo y departamento.
/// </summary>
public sealed record FiltrosAnalisis(
    DateOnly Desde,
    DateOnly Hasta,
    Guid? SucursalId = null,
    Guid? ProveedorId = null,
    Guid? ArticuloId = null,
    Guid? DepartamentoId = null,
    string? Moneda = null,
    EstadoOrdenCompra? Estado = null);

/// <summary>
/// Base de las consultas del análisis: alcance por sucursal (ADR-0051) con
/// el bypass de OC existente; <see cref="SucursalId"/> se valida contra las
/// sucursales del usuario en <c>SucursalScopeQueryBehavior</c>.
/// </summary>
public abstract record AnalisisProveedoresQueryBase(FiltrosAnalisis Filtros) : ISucursalSeleccionadaQuery
{
    public string PermisoTodasSucursales => "compras.ordenes.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public Guid? SucursalId => Filtros.SucursalId;
}

/// <summary>Respuesta común de las tres pestañas (contrato `.local-context/contrato-86e3jvu98.md`).</summary>
public sealed record AnalisisProveedoresResponse(
    ReporteJsonResponse Reporte,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> TotalesPorMoneda,
    IReadOnlyList<SaldoInicialMoneda>? SaldoInicial = null,
    IReadOnlyList<string>? Formulas = null);

public sealed record SaldoInicialMoneda(string Moneda, int Ordenes, decimal PendienteFisico);

public sealed class FiltrosAnalisisValidator : AbstractValidator<FiltrosAnalisis>
{
    public const int MaxDiasPeriodo = 366;

    public FiltrosAnalisisValidator()
    {
        RuleFor(f => f.Desde).NotEmpty().WithMessage("Indica la fecha inicial del periodo.");
        RuleFor(f => f.Hasta).NotEmpty().WithMessage("Indica la fecha final del periodo.")
            .GreaterThanOrEqualTo(f => f.Desde).WithMessage("La fecha final no puede ser anterior a la inicial.");
        RuleFor(f => f).Must(f => f.Hasta.DayNumber - f.Desde.DayNumber < MaxDiasPeriodo)
            .WithName("Hasta").WithMessage($"El periodo no puede pasar de {MaxDiasPeriodo} días.");
        RuleFor(f => f.Moneda).Length(3).When(f => !string.IsNullOrEmpty(f.Moneda))
            .WithMessage("La moneda debe ser un código ISO de 3 letras.");
        RuleFor(f => f.SucursalId).Must(id => id != Guid.Empty).When(f => f.SucursalId is not null).WithMessage("Sucursal inválida.");
        RuleFor(f => f.ProveedorId).Must(id => id != Guid.Empty).When(f => f.ProveedorId is not null).WithMessage("Proveedor inválido.");
        RuleFor(f => f.ArticuloId).Must(id => id != Guid.Empty).When(f => f.ArticuloId is not null).WithMessage("Artículo inválido.");
        RuleFor(f => f.DepartamentoId).Must(id => id != Guid.Empty).When(f => f.DepartamentoId is not null).WithMessage("Departamento inválido.");
    }
}

/// <summary>Carga las OC firmes del alcance con todo lo que necesitan las reglas.</summary>
internal static class OcAnalisisCargador
{
    /// <summary>OC firme = ya comprometida con el proveedor.</summary>
    internal static readonly EstadoOrdenCompra[] EstadosFirmes =
        [EstadoOrdenCompra.Autorizada, EstadoOrdenCompra.Cerrada, EstadoOrdenCompra.CancelacionSolicitada];

    internal static IQueryable<OrdenCompra> Base(ComprasDbContext db, AnalisisProveedoresQueryBase q)
    {
        var f = q.Filtros;
        var permitidas = q.SucursalesPermitidas;
        var query = db.OrdenesCompra.AsNoTracking()
            .Where(o => permitidas == null || permitidas.Contains(o.SucursalDestinoId))
            .Where(o => EstadosFirmes.Contains(o.Estado));
        if (f.ProveedorId is Guid p) query = query.Where(o => o.ProveedorId == p);
        if (!string.IsNullOrWhiteSpace(f.Moneda))
        {
            var moneda = f.Moneda.Trim().ToUpperInvariant();
            query = query.Where(o => o.Moneda == moneda);
        }
        if (f.Estado is { } estado) query = query.Where(o => o.Estado == estado);
        if (f.ArticuloId is Guid a) query = query.Where(o => o.Lineas.Any(l => l.ArticuloId == a));
        if (f.DepartamentoId is Guid d) query = query.Where(o => o.Lineas.Any(l => l.DepartamentoSolicitanteId == d));
        return query;
    }

    // ponytail: carga agregados completos en memoria; el periodo está topado
    // a 366 días. Si el volumen lo pide, proyectar sólo columnas por línea.
    internal static Task<List<OrdenCompra>> CargarAsync(IQueryable<OrdenCompra> query, CancellationToken ct) =>
        query
            .Include(o => o.Lineas)
            .Include(o => o.Incidencias).ThenInclude(i => i.Compromisos)
            .Include(o => o.RespuestasProveedor).ThenInclude(r => r.Compromisos)
            .AsSplitQuery()
            .ToListAsync(ct);

    internal static IQueryable<OrdenCompra> DelPeriodo(this IQueryable<OrdenCompra> query, FiltrosAnalisis f) =>
        query.Where(o => o.FechaDocumento >= f.Desde && o.FechaDocumento <= f.Hasta);

    /// <summary>Líneas que cuentan con los filtros de artículo y departamento.</summary>
    internal static IEnumerable<LineaOrdenCompra> LineasFiltradas(OrdenCompra oc, FiltrosAnalisis f) =>
        oc.Lineas.Where(l => (f.ArticuloId is not Guid a || l.ArticuloId == a)
            && (f.DepartamentoId is not Guid d || l.DepartamentoSolicitanteId == d));

    internal static string Folio(OrdenCompra oc) => oc.Folio.Valor;
}

/// <summary>
/// Reglas del análisis de proveedores. «A tiempo» es la fórmula del plano 1.7.1
/// (Propuesta VILO: entregas puntuales sobre compromisos vencidos evaluables);
/// «completa» la confirmó el owner el 22-oct-2026. Si Millet las cambia, sólo
/// cambia este archivo.
/// </summary>
public static class ReglasAnalisisProveedores
{
    public const string FormulaATiempo =
        "% a tiempo = líneas recibidas completas en o antes de su compromiso vigente ÷ líneas evaluables " +
        "(compromiso vencido o línea ya completa). Compromiso vigente: la última reprogramación de una incidencia; " +
        "si no hay, la fecha prometida por el proveedor; si no, la fecha de entrega de la línea; si no, la de la orden. " +
        "Fórmula propuesta por VILO (plano 1.7.1), sujeta a revisión.";

    public const string FormulaCompleto =
        "% completo = líneas con recibido neto (recepciones menos devoluciones al proveedor) igual o mayor a lo pedido ÷ " +
        "líneas de órdenes cerradas o con compromiso vencido.";

    public const string NotaServicios =
        "Las líneas de servicio no se reciben en almacén y no cuentan en estos porcentajes.";

    public const string NotaDiferencias =
        "Diferencias de precio: facturas no canceladas con diferencia contra la orden registrada en Cuentas por Pagar. " +
        "Las facturas rechazadas por tolerancia no se registran y no cuentan.";

    public static readonly IReadOnlyList<string> Formulas = [FormulaATiempo, FormulaCompleto, NotaServicios, NotaDiferencias];

    public static decimal PrecioNeto(LineaOrdenCompra l) => l.Cantidad == 0m ? 0m : l.SubtotalLinea / l.Cantidad;

    public static decimal Ordenado(LineaOrdenCompra l) => Math.Round(l.SubtotalLinea, 2);

    public static decimal RecibidoValorizado(LineaOrdenCompra l) =>
        Math.Round(Math.Min(l.CantidadRecibida, l.Cantidad) * PrecioNeto(l), 2);

    public static decimal PorFacturar(LineaOrdenCompra l) =>
        Math.Round(Math.Max(0m, l.Cantidad - l.CantidadFacturada) * PrecioNeto(l), 2);

    public static decimal PorPagar(OrdenCompra oc) =>
        Math.Max(0m, Math.Round(oc.CalcularTotales().TotalAPagar - oc.MontoPagado, 2));

    /// <summary>Compromiso vigente de una línea y de dónde salió (null si no tiene).</summary>
    public static (DateOnly? Fecha, string? Origen) CompromisoVigente(OrdenCompra oc, LineaOrdenCompra l)
    {
        var incidencia = oc.Incidencias.Where(i => i.LineaOrdenCompraId == l.Id && i.Compromisos.Count > 0)
            .MaxBy(i => i.Numero);
        if (incidencia is not null) return (incidencia.FechaCompromisoVigente, "incidencia");

        var promesa = oc.RespuestasProveedor.OrderByDescending(r => r.Numero)
            .Select(r => r.Compromisos.Where(c => c.LineaOrdenCompraId == l.Id).Select(c => (DateOnly?)c.FechaPrometida).Max())
            .FirstOrDefault(fecha => fecha is not null);
        if (promesa is not null) return (promesa, "promesa-proveedor");

        if (l.FechaEntregaLinea is { } linea) return (FechaContable.HoyLocal(linea), "linea");
        if (oc.FechaCompromisoProveedor is { } ocProveedor) return (ocProveedor, "oc-proveedor");
        if (oc.FechaEntregaEsperada is { } esperada) return (esperada, "oc-esperada");
        return (null, null);
    }

    /// <summary>
    /// Primer día en que el recibido neto acumulado (por fecha de movimiento)
    /// alcanza lo pedido. Null si nunca lo alcanzó o no hay movimientos.
    /// </summary>
    public static DateOnly? FechaCompletada(decimal cantidad, IEnumerable<(DateOnly Fecha, decimal Cantidad)> movimientos)
    {
        var acumulado = 0m;
        foreach (var dia in movimientos.GroupBy(m => m.Fecha).OrderBy(g => g.Key))
        {
            acumulado += dia.Sum(m => m.Cantidad);
            if (acumulado >= cantidad) return dia.Key;
        }
        return null;
    }

    public sealed record EvaluacionLinea(
        DateOnly? Compromiso, string? OrigenCompromiso, DateOnly? FechaCompletada,
        bool Evaluable, bool? ATiempo, bool Completable, bool? Completa);

    public static EvaluacionLinea Evaluar(OrdenCompra oc, LineaOrdenCompra l,
        IEnumerable<(DateOnly Fecha, decimal Cantidad)> movimientos, DateOnly hoy)
    {
        var (compromiso, origen) = CompromisoVigente(oc, l);
        if (l.EsServicio) return new(compromiso, origen, null, false, null, false, null);

        var completada = FechaCompletada(l.Cantidad, movimientos);
        var vencido = compromiso is { } c && c < hoy;
        // Completa sin fecha de recepción en Almacén (p. ej. entrega histórica
        // migrada): no se sabe si fue a tiempo, así que no se evalúa.
        var completaSinFecha = completada is null && l.CantidadRecibida >= l.Cantidad;
        var evaluable = compromiso is not null && !completaSinFecha && (completada is not null || vencido);
        bool? aTiempo = evaluable ? completada is { } fc && fc <= compromiso!.Value : null;

        var completable = oc.Estado == EstadoOrdenCompra.Cerrada || vencido;
        bool? completa = completable ? l.CantidadRecibida >= l.Cantidad : null;
        return new(compromiso, origen, completada, evaluable, aTiempo, completable, completa);
    }

    public static decimal? Porcentaje(int numerador, int denominador) =>
        denominador == 0 ? null : Math.Round(100m * numerador / denominador, 1);
}

internal static class ReporteBuilder
{
    internal static ColumnaReporte Texto(string key, string label) => new(key, label, TipoColumnaReporte.Texto, AlineacionColumna.Izquierda);
    internal static ColumnaReporte Entero(string key, string label) => new(key, label, TipoColumnaReporte.Entero, AlineacionColumna.Derecha);
    internal static ColumnaReporte Numero(string key, string label) => new(key, label, TipoColumnaReporte.Numerico, AlineacionColumna.Derecha);
    internal static ColumnaReporte Importe(string key, string label) => new(key, label, TipoColumnaReporte.Moneda, AlineacionColumna.Derecha);
    internal static ColumnaReporte Fecha(string key, string label) => new(key, label, TipoColumnaReporte.Fecha, AlineacionColumna.Izquierda);

    internal static List<FiltroAplicado> Filtros(FiltrosAnalisis f, string? proveedor = null)
    {
        var filtros = new List<FiltroAplicado> { new("Periodo", $"{f.Desde:yyyy-MM-dd} a {f.Hasta:yyyy-MM-dd}") };
        if (f.SucursalId is Guid s) filtros.Add(new("Sucursal", s.ToString()));
        if (f.ProveedorId is Guid p) filtros.Add(new("Proveedor", proveedor ?? p.ToString()));
        if (f.ArticuloId is Guid a) filtros.Add(new("Artículo", a.ToString()));
        if (f.DepartamentoId is Guid d) filtros.Add(new("Departamento", d.ToString()));
        if (!string.IsNullOrWhiteSpace(f.Moneda)) filtros.Add(new("Moneda", f.Moneda.ToUpperInvariant()));
        if (f.Estado is { } e) filtros.Add(new("Estado", e.ToString()));
        return filtros;
    }

    /// <summary>Totales por moneda de las columnas indicadas; nunca suma monedas distintas.</summary>
    internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> TotalesPorMoneda(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> filas, params string[] columnas) =>
        filas.GroupBy(f => (string)f["moneda"]!).OrderBy(g => g.Key)
            .Select(g =>
            {
                var total = new Dictionary<string, object?> { ["moneda"] = g.Key };
                foreach (var c in columnas)
                    total[c] = g.Sum(f => Convert.ToDecimal(f[c] ?? 0m));
                return (IReadOnlyDictionary<string, object?>)total;
            }).ToList();
}
