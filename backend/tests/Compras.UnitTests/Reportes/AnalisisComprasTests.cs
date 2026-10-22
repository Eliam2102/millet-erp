using Millet.Compras.Application.Oc.Analisis;

namespace Millet.Compras.UnitTests.Reportes;

// Todos los datos de esta suite son DEMO ficticios, no corresponden a Millet.
public sealed class AnalisisComprasTests
{
    private static readonly Guid ProveedorA = Guid.NewGuid(), ProveedorB = Guid.NewGuid();
    private static readonly DateOnly Desde = new(2026, 1, 1), Corte = new(2026, 3, 31);
    private static PartidaCompraAnalisis Partida(Guid proveedor, string folio, DateOnly? fecha = null) =>
        new(Guid.NewGuid(), folio, Guid.NewGuid(), proveedor, "DEMO proveedor", Guid.NewGuid(), "DEMO artículo",
            Guid.NewGuid(), "DEMO departamento", "MXN", fecha ?? Desde, 10m, "PZA", 100m, 10m, false,
            new DateOnly(2026, 1, 10), [], []);

    private static IReadOnlyList<PartidaCompraAnalisis> Demo()
    {
        var a = Partida(ProveedorA, "OC-DEMO-1");
        a = a with { Compromisos = [new(new(2026, 1, 5), new(2026, 1, 20)), new(new(2026, 4, 1), new(2026, 5, 1))],
            Incidencias = [new(2026, 1, 5)], Movimientos = [new(Guid.NewGuid(), "REC-DEMO-1", new(2026, 1, 15), 10m, false),
                new(Guid.NewGuid(), "DEV-DEMO-1", new(2026, 2, 1), 2m, true)] };
        var b = Partida(ProveedorA, "OC-DEMO-2") with { Movimientos = [new(Guid.NewGuid(), "REC-DEMO-2", new(2026, 1, 9), 10m, false)] };
        var c = Partida(ProveedorB, "OC-DEMO-3") with { Movimientos = [new(Guid.NewGuid(), "REC-DEMO-3", new(2026, 2, 1), 10m, false)] };
        return [a, b, c, Partida(Guid.NewGuid(), "OC-DEMO-FUERA", new(2025, 12, 1))];
    }

    [Fact] public void COM13a_Trimestre_indicadores_suman_con_partidas()
    {
        var r = CalculoAnalisisCompras.Calcular(Demo(), new(Desde, Corte));
        var a = Assert.Single(r.Proveedores, p => p.ProveedorId == ProveedorA);
        Assert.Equal(50m, a.PorcentajeATiempo); Assert.Equal(50m, a.PorcentajeCompleto);
        Assert.Equal(1, a.Incidencias); Assert.Equal(2, a.Partidas); Assert.Equal(200m, a.ImporteCompra);
        Assert.Equal(r.Partidas.Sum(p => p.ImporteCompra), r.Proveedores.Sum(p => p.ImporteCompra));
    }
    [Fact] public void COM13b_Proveedor_sin_compras_del_periodo_no_aparece()
    {
        var r = CalculoAnalisisCompras.Calcular(Demo(), new(Desde, Corte));
        Assert.Equal(2, r.Proveedores.Count); Assert.Equal(3, r.Partidas.Count);
        Assert.Empty(CalculoAnalisisCompras.Calcular(Demo(), new(new(2027, 1, 1), new(2027, 3, 31))).Proveedores);
    }
    [Fact] public void COM13c_Neto_devolucion_corte_compromiso_y_saldos()
    {
        var p = Demo()[0] with { Facturada = 6m, PorPagar = 42m, DiferenciaPrecio = 2m };
        p = p with { Movimientos = [..p.Movimientos, new(Guid.NewGuid(), "REC-FUTURA", new(2026, 4, 1), 2m, false)] };
        var fila = Assert.Single(CalculoAnalisisCompras.Calcular([p], new(Desde, Corte)).Partidas);
        Assert.Equal(8m, fila.RecibidoNeto); Assert.Equal(2m, fila.PorRecibir);
        Assert.Equal(4m, fila.PorFacturar); Assert.Equal(42m, fila.PorPagar);
        Assert.Equal(2m, fila.DiferenciaPrecio); Assert.Equal(new DateOnly(2026, 1, 20), fila.Compromiso);
        Assert.Equal(70, fila.DiasAtraso); Assert.False(fila.ATiempo);
    }
    [Fact] public void COM13d_Filtros_proveedor_periodo_articulo_departamento()
    {
        var demo = Demo();
        Assert.Single(CalculoAnalisisCompras.Calcular(demo, new(Desde, Corte, ProveedorB)).Partidas);
        Assert.Single(CalculoAnalisisCompras.Calcular(demo, new(Desde, Corte, ArticuloId: demo[0].ArticuloId)).Partidas);
        Assert.Single(CalculoAnalisisCompras.Calcular(demo, new(Desde, Corte, DepartamentoId: demo[0].DepartamentoId)).Partidas);
        Assert.Empty(CalculoAnalisisCompras.Calcular(demo, new(new(2026, 2, 1), Corte)).Partidas);
    }
    [Fact] public void COM13e_Total_conserva_OC_recepciones_y_devoluciones_sustento()
    {
        var r = CalculoAnalisisCompras.Calcular(Demo(), new(Desde, Corte, ProveedorA));
        var ids = Assert.Single(r.Proveedores).LineasIds;
        Assert.Equal(r.Partidas.Select(p => p.LineaId), ids);
        Assert.Contains(r.Partidas, p => p.Folio == "OC-DEMO-1" && p.Movimientos.Any(m => m.Folio == "REC-DEMO-1")
            && p.Movimientos.Any(m => m.Folio == "DEV-DEMO-1"));
    }
    [Fact] public void Monedas_no_se_suman_servicios_y_sin_compromiso_no_falsean_puntualidad()
    {
        var p = Partida(ProveedorA, "DEMO") with { FechaCompromiso = null };
        var r = CalculoAnalisisCompras.Calcular([p, p with { LineaId = Guid.NewGuid(), Moneda = "USD", EsServicio = true }], new(Desde, Corte));
        Assert.Equal(2, r.Proveedores.Count); Assert.All(r.Proveedores, x => Assert.Null(x.PorcentajeATiempo));
        Assert.Equal(0m, r.Partidas.Single(x => x.Moneda == "USD").PorRecibir);
    }
    [Theory]
    [InlineData(100, 1, 1, 1)]
    [InlineData(0.01, 1, 1, 1)]
    [InlineData(101.11, 0, 0, 0)]
    public void Prorrateo_conserva_centavos_y_total(decimal total, decimal a, decimal b, decimal c)
    {
        var r = ProrrateoAnalisis.Distribuir(total, [a, b, c]);
        Assert.Equal(total, r.Sum());
    }
    [Fact] public void Entrega_tardia_sin_pendiente_conserva_atraso_y_futura_no_penaliza()
    {
        var p = Partida(ProveedorA, "DEMO") with { Movimientos = [new(Guid.NewGuid(), "DEMO-REC", new(2026, 1, 15), 10, false)] };
        var fila = Assert.Single(CalculoAnalisisCompras.Calcular([p], new(Desde, Corte)).Partidas);
        Assert.Equal(5, fila.DiasAtraso); Assert.True(fila.Completa); Assert.False(fila.ATiempo);
        p = p with { Movimientos = [], FechaCompromiso = new(2026, 4, 1) };
        Assert.Null(Assert.Single(CalculoAnalisisCompras.Calcular([p], new(Desde, Corte)).Proveedores).PorcentajeATiempo);
    }
    [Fact] public void Valida_periodo_y_identificadores()
    {
        var v = new ObtenerAnalisisComprasValidator();
        Assert.True(v.Validate(new ObtenerAnalisisComprasQuery(Desde, Corte)).IsValid);
        Assert.False(v.Validate(new ObtenerAnalisisComprasQuery(Corte, Desde)).IsValid);
        Assert.False(v.Validate(new ObtenerAnalisisComprasQuery(Desde, DateOnly.MaxValue)).IsValid);
        Assert.False(v.Validate(new ObtenerAnalisisComprasQuery(Desde, Corte, Guid.Empty)).IsValid);
    }
}
