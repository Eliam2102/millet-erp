namespace Millet.Compras.Application.Oc.Analisis;

public sealed record FiltrosAnalisisCompra(DateOnly Desde, DateOnly Hasta, Guid? ProveedorId = null,
    Guid? ArticuloId = null, Guid? DepartamentoId = null);
public sealed record CompromisoAnalisis(DateOnly Registrado, DateOnly Fecha);
public sealed record MovimientoAnalisis(Guid DocumentoId, string Folio, DateOnly Fecha, decimal Cantidad, bool EsDevolucion);
public sealed record PartidaCompraAnalisis(Guid OrdenCompraId, string Folio, Guid LineaId, Guid ProveedorId,
    string Proveedor, Guid ArticuloId, string Articulo, Guid DepartamentoId, string Departamento, string Moneda,
    DateOnly FechaDocumento, decimal Cantidad, string Unidad, decimal ImporteCompra, decimal PrecioNeto,
    bool EsServicio, DateOnly? FechaCompromiso, IReadOnlyList<CompromisoAnalisis> Compromisos,
    IReadOnlyList<DateOnly> Incidencias)
{
    public IReadOnlyList<MovimientoAnalisis> Movimientos { get; init; } = [];
    public decimal Facturada { get; init; }
    public decimal PorPagar { get; init; }
    public decimal DiferenciaPrecio { get; init; }
}
public sealed record PartidaAnalisisResultado(Guid OrdenCompraId, string Folio, Guid LineaId, Guid ProveedorId,
    string Proveedor, Guid ArticuloId, string Articulo, Guid DepartamentoId, string Departamento, string Moneda,
    decimal Cantidad, string Unidad, decimal ImporteCompra, decimal RecibidoNeto, decimal PorRecibir,
    decimal PorFacturar, decimal PorPagar, decimal DiferenciaPrecio, int Incidencias, DateOnly? Compromiso,
    int DiasAtraso, bool? ATiempo, bool? Completa, IReadOnlyList<MovimientoAnalisis> Movimientos);
public sealed record ProveedorAnalisisResultado(Guid ProveedorId, string Proveedor, string Moneda, int Partidas,
    int PartidasEvaluadas, decimal? PorcentajeATiempo, decimal? PorcentajeCompleto, int Incidencias,
    decimal ImporteCompra, decimal PorPagar, decimal DiferenciaPrecio, IReadOnlyList<Guid> LineasIds);
public sealed record AnalisisComprasResultado(IReadOnlyList<PartidaAnalisisResultado> Partidas,
    IReadOnlyList<ProveedorAnalisisResultado> Proveedores, DateTimeOffset GeneradoEn = default);

public static class CalculoAnalisisCompras
{
    public static AnalisisComprasResultado Calcular(IEnumerable<PartidaCompraAnalisis> compras, FiltrosAnalisisCompra filtros)
    {
        var filas = compras.Where(p => p.FechaDocumento >= filtros.Desde && p.FechaDocumento <= filtros.Hasta
            && (filtros.ProveedorId == null || p.ProveedorId == filtros.ProveedorId)
            && (filtros.ArticuloId == null || p.ArticuloId == filtros.ArticuloId)
            && (filtros.DepartamentoId == null || p.DepartamentoId == filtros.DepartamentoId))
            .Select(p => CalcularPartida(p, filtros.Hasta)).ToArray();
        var proveedores = filas.GroupBy(p => new { p.ProveedorId, p.Moneda }).Select(g =>
            new ProveedorAnalisisResultado(g.Key.ProveedorId, g.First().Proveedor, g.Key.Moneda, g.Count(),
                g.Count(p => p.ATiempo.HasValue), Porcentaje(g.Select(p => p.ATiempo)), Porcentaje(g.Select(p => p.Completa)),
                g.Sum(p => p.Incidencias), g.Sum(p => p.ImporteCompra), g.Sum(p => p.PorPagar),
                g.Sum(p => p.DiferenciaPrecio), g.Select(p => p.LineaId).ToArray())).ToArray();
        return new(filas, proveedores);
    }

    private static PartidaAnalisisResultado CalcularPartida(PartidaCompraAnalisis p, DateOnly corte)
    {
        var movimientos = p.Movimientos.Where(m => m.Fecha <= corte).OrderBy(m => m.Fecha).ThenBy(m => m.DocumentoId).ToArray();
        var neto = movimientos.Sum(m => m.EsDevolucion ? -m.Cantidad : m.Cantidad);
        var compromiso = p.Compromisos.Where(c => c.Registrado <= corte).OrderBy(c => c.Registrado).LastOrDefault()?.Fecha ?? p.FechaCompromiso;
        var completa = neto >= p.Cantidad;
        // Una devolución reabre el cumplimiento; se mide la última vez que se completó el neto vigente.
        decimal acumulado = 0;
        DateOnly? completadaEn = null;
        foreach (var m in movimientos)
        {
            acumulado += m.EsDevolucion ? -m.Cantidad : m.Cantidad;
            if (acumulado < p.Cantidad) completadaEn = null;
            else completadaEn ??= m.Fecha;
        }
        bool? aTiempo = p.EsServicio || compromiso is null || (!completa && compromiso > corte)
            ? null : completa && completadaEn <= compromiso;
        var atraso = p.EsServicio || compromiso is null ? 0
            : Math.Max(0, (completa ? completadaEn ?? corte : corte).DayNumber - compromiso.Value.DayNumber);
        return new(p.OrdenCompraId, p.Folio, p.LineaId, p.ProveedorId, p.Proveedor, p.ArticuloId, p.Articulo,
            p.DepartamentoId, p.Departamento, p.Moneda, p.Cantidad, p.Unidad, p.ImporteCompra, neto,
            p.EsServicio ? 0 : Math.Max(0, p.Cantidad - neto), Math.Max(0, p.Cantidad - p.Facturada),
            p.PorPagar, p.DiferenciaPrecio, p.Incidencias.Count(d => d <= corte), compromiso, atraso,
            aTiempo, p.EsServicio ? null : completa, movimientos);
    }
    private static decimal? Porcentaje(IEnumerable<bool?> valores)
    {
        var evaluados = valores.Where(x => x.HasValue).ToArray();
        return evaluados.Length == 0 ? null : Math.Round(100m * evaluados.Count(x => x == true) / evaluados.Length, 2);
    }
}
