using Millet.Integraciones.Aw.Infrastructure.CostoPromedio;
using Millet.Integraciones.Aw.UnitTests.Application;

namespace Millet.Integraciones.Aw.UnitTests.CostoPromedio;

/// <summary>M-447: una fila por cambio del promedio, versión + 1, nada si no cambia, PESOSMX y columnas de vidrio vs cantidad.</summary>
public sealed class CostoPromedioPublicadorTests
{
    private static CostoPromedioFila Vidrio(decimal costoM2) =>
        new(1001, "CONKAL", "EXPORTACION", "HEGLA 1", 2600, 1800, costoM2, null);
    private static CostoPromedioFila Cantidad(decimal costoPieza) =>
        new(2002, "CIRCUITO", null, null, null, null, null, costoPieza);

    private static async Task<(CostoPromedioPublicador Pub, SimuladoCostoPromedioTransporte Tr)> CrearAsync()
    {
        var tr = new SimuladoCostoPromedioTransporte();
        return (new CostoPromedioPublicador(await InMemoryDb.CreateAsync(), tr, TimeProvider.System), tr);
    }

    [Fact]
    public async Task PrimeraVez_EscribeUnaFilaConVersionUno()
    {
        var (pub, tr) = await CrearAsync();
        (await pub.PublicarAsync([Vidrio(100m), Cantidad(5m)], default)).Should().Be(2);
        tr.Filas.Select(f => f.Version).Should().AllBeEquivalentTo(1);
        tr.Filas.Should().HaveCount(2);
    }

    [Fact]
    public async Task SinCambio_NoEscribe()
    {
        var (pub, tr) = await CrearAsync();
        await pub.PublicarAsync([Vidrio(100m)], default);
        (await pub.PublicarAsync([Vidrio(100m)], default)).Should().Be(0);
        // Diferencia por debajo de la precisión de la columna destino (6 decimales) tampoco cuenta.
        (await pub.PublicarAsync([Vidrio(100.0000004m)], default)).Should().Be(0);
        tr.Filas.Should().ContainSingle();
    }

    [Fact]
    public async Task CadaCambio_EscribeFilaNuevaYSubeLaVersion()
    {
        var (pub, tr) = await CrearAsync();
        await pub.PublicarAsync([Vidrio(100m)], default);
        await pub.PublicarAsync([Vidrio(110.5m)], default);
        await pub.PublicarAsync([Vidrio(95m)], default);
        tr.Filas.Select(f => (f.Version, f.Fila.CostoM2)).Should().Equal((1, 100m), (2, 110.5m), (3, 95m));
    }

    [Fact]
    public async Task LlavesDistintas_LlevanVersionPropia()
    {
        var (pub, tr) = await CrearAsync();
        await pub.PublicarAsync([Vidrio(100m), Vidrio(100m) with { Linea = "HEGLA 2" }], default);
        await pub.PublicarAsync([Vidrio(120m), Vidrio(100m) with { Linea = "HEGLA 2" }], default);
        tr.Filas.Count(f => f.Fila.Linea == "HEGLA 1" && f.Version == 2).Should().Be(1);
        tr.Filas.Count(f => f.Fila.Linea == "HEGLA 2").Should().Be(1);
    }

    [Fact]
    public async Task Moneda_SiempreEsPesosMx()
    {
        var (pub, tr) = await CrearAsync();
        await pub.PublicarAsync([Vidrio(100m), Cantidad(5m)], default);
        tr.Filas.Should().OnlyContain(f => f.Moneda == "PESOSMX");
    }

    [Fact]
    public async Task Vidrio_UsaCostoM2YMedida_Cantidad_UsaCostoPiezaSinMedida()
    {
        var (pub, tr) = await CrearAsync();
        await pub.PublicarAsync([Vidrio(100m), Cantidad(5m)], default);
        var v = tr.Filas.Single(f => f.Fila.ArticuloAw == 1001).Fila;
        (v.CostoM2, v.CostoPieza, v.AnchoMm, v.AltoMm).Should().Be((100m, (decimal?)null, 2600, 1800));
        var c = tr.Filas.Single(f => f.Fila.ArticuloAw == 2002).Fila;
        (c.CostoM2, c.CostoPieza, c.AnchoMm, c.AltoMm, c.AlmacenTipo, c.Linea)
            .Should().Be(((decimal?)null, 5m, (int?)null, (int?)null, (string?)null, (string?)null));
    }

    [Fact]
    public async Task FilaDeSucursalYFilaDeAlmacen_LlevanVersionPropia_YLaDeSucursalSigueNullAlVersionar()
    {
        var (pub, tr) = await CrearAsync();
        var sucursal = Vidrio(100m) with { AlmacenTipo = null, Linea = null };
        await pub.PublicarAsync([Vidrio(100m), sucursal], default);
        await pub.PublicarAsync([Vidrio(100m), sucursal with { CostoM2 = 130m }], default);
        tr.Filas.Where(f => f.Fila.AlmacenTipo is null).Select(f => (f.Version, f.Fila.Linea, f.Fila.CostoM2))
            .Should().Equal((1, (string?)null, (decimal?)100m), (2, null, 130m));
        tr.Filas.Should().ContainSingle(f => f.Fila.AlmacenTipo == "EXPORTACION");
    }

    [Fact]
    public async Task ReintentoTrasFallaDelTransporte_NoPierdeElCambio()
    {
        var db = await InMemoryDb.CreateAsync();
        var falla = new FallaUnaVez();
        var pub = new CostoPromedioPublicador(db, falla, TimeProvider.System);
        await FluentActions.Awaiting(() => pub.PublicarAsync([Vidrio(100m)], default)).Should().ThrowAsync<InvalidOperationException>();
        (await pub.PublicarAsync([Vidrio(100m)], default)).Should().Be(1);
        falla.Escritas.Should().ContainSingle().Which.Version.Should().Be(1);
    }

    private sealed class FallaUnaVez : ICostoPromedioTransporte
    {
        private bool _fallo;
        public List<CostoPromedioEscrito> Escritas { get; } = [];
        public Task EscribirAsync(CostoPromedioEscrito e, CancellationToken ct)
        {
            if (!_fallo) { _fallo = true; throw new InvalidOperationException("sin conexión"); }
            Escritas.Add(e);
            return Task.CompletedTask;
        }
    }
}
