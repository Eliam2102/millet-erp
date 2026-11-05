using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Taller;
using Millet.Almacen.UnitTests.AppAlmacenes;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Domain.Taller;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Taller;

/// <summary>PT5.10: descarga idempotente del taller por pedido Serie 9000 (por medida) y fórmula del factor de optimización, InMemory.</summary>
public class TallerSerie9000Tests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Circuito = Guid.NewGuid();
    private static readonly Guid FloatClaro6 = Guid.NewGuid();
    private static readonly Guid Medida2440x3660 = Guid.NewGuid();
    private static readonly Guid Medida2140x3300 = Guid.NewGuid();

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }

    private sealed class Resolver : IAppSalidaResolverPort
    {
        public Task<ResolverSalidaResultado> ResolverAsync(string sucursalCodigo, IReadOnlyList<GrupoSalidaApp> grupos, CancellationToken ct)
        {
            if (sucursalCodigo != "CIRCUITO") return Task.FromResult(new ResolverSalidaResultado($"No existe la sucursal '{sucursalCodigo}'.", Guid.Empty, []));
            var resueltos = grupos.Select(g => g.AnchoMm == 2440
                ? new GrupoSalidaResuelto(FloatClaro6, "HOJA", Medida2440x3660, 2440, 3660, 8.9304m)
                : new GrupoSalidaResuelto(FloatClaro6, "HOJA", Medida2140x3300, 2140, 3300, 7.062m)).ToList();
            return Task.FromResult(new ResolverSalidaResultado(null, Circuito, resueltos));
        }
    }

    private sealed class Usuario : IUsuarioServicioReadPort
    {
        public Task<UsuarioServicioLectura?> ObtenerReordenAsync(CancellationToken ct) =>
            Task.FromResult<UsuarioServicioLectura?>(new(Guid.NewGuid(), EmpresaId, true));
    }

    private static (AlmacenDbContext Db, DescargaTallerGuardado G) Guardado(decimal hojas2440 = 10)
    {
        var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"taller-{Guid.NewGuid():N}").Options, new FakeEmpresa());
        var alm = Guid.NewGuid(); var sub = Guid.NewGuid(); var ubi = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(alm, "TAL", "Taller Circuito", Circuito));
        db.SubAlmacenes.Add(new SubAlmacen(sub, alm, "TAL", "Taller", TipoSubAlmacen.MaterialesDirectos));
        db.Ubicaciones.Add(new Ubicacion(ubi, sub, "ÚNICA", "Única", esDefault: true));
        db.SaldosInventario.Add(new SaldoInventario(ubi, sub, FloatClaro6, hojas2440 * 8.9304m, 95m, Medida2440x3660));
        db.SaldosInventario.Add(new SaldoInventario(ubi, sub, FloatClaro6, 5 * 7.062m, 80m, Medida2140x3300));
        db.SaveChanges();
        return (db, new DescargaTallerGuardado(db, new FakeEmpresa(), new Resolver(), new Usuario(), new Eventos()));
    }

    private static PedidoInternoLineaAw Pedido(decimal hojas = 3, decimal ancho = 2440, decimal alto = 3660, string sucursal = "CIRCUITO") =>
        new("90990010", 1, "100245", hojas, ancho, alto, sucursal, new DateOnly(2026, 11, 4));

    [Fact]
    public async Task Pedido_9000_baja_las_hojas_de_esa_medida_y_sus_m2()
    {
        var (db, g) = Guardado();
        (await g.RegistrarAsync(EmpresaId, "Demo", Pedido(), default)).Should().BeTrue();

        var d = await db.DescargasTallerAw.SingleAsync();
        d.Estado.Should().Be(DescargaTallerAw.Recibido);
        d.SucursalId.Should().Be(Circuito);
        d.M2.Should().Be(26.7912m); // 3 × 8.9304
        d.Folio.Should().StartWith("M-SAL");
        // El saldo lo baja el disparador de PostgreSQL (cubierto en integración); aquí se prueba que la línea sea de esa medida.
        var linea = (await db.Movimientos.Include(m => m.Lineas).SingleAsync()).Lineas.Single();
        linea.Cantidad.Should().Be(26.7912m); // 3 hojas × 8.9304 m²
        linea.ProductoAwVarianteId.Should().Be(Medida2440x3660); // nunca suma medidas
        (await g.MarcaAguaAsync(EmpresaId, "Demo", default)).Should().Be(new DateOnly(2026, 11, 4));
    }

    [Fact]
    public async Task Si_el_pedido_llega_dos_veces_baja_una_sola_vez()
    {
        var (db, g) = Guardado();
        await g.RegistrarAsync(EmpresaId, "Demo", Pedido(), default);
        (await g.RegistrarAsync(EmpresaId, "Demo", Pedido(), default)).Should().BeFalse();

        (await db.DescargasTallerAw.CountAsync()).Should().Be(1);
        (await db.Movimientos.Include(m => m.Lineas).SingleAsync()).Lineas.Single().Cantidad.Should().Be(26.7912m);
    }

    [Fact]
    public async Task Sin_existencia_suficiente_queda_en_revision_sin_mover_saldo_y_el_reintento_descarga()
    {
        var (db, g) = Guardado(hojas2440: 2);
        await g.RegistrarAsync(EmpresaId, "Demo", Pedido(hojas: 3), default);
        var d = await db.DescargasTallerAw.AsNoTracking().SingleAsync();
        d.Estado.Should().Be(DescargaTallerAw.EnRevision);
        d.SucursalId.Should().Be(Circuito);
        (await db.Movimientos.CountAsync()).Should().Be(0);

        db.SaldosInventario.Add(new SaldoInventario(db.Ubicaciones.First().Id, db.SubAlmacenes.First().Id, FloatClaro6, 10 * 8.9304m, 95m, Medida2440x3660));
        await db.SaveChangesAsync();
        await g.ReintentarAsync(d.Id, default);
        (await db.DescargasTallerAw.AsNoTracking().SingleAsync()).Estado.Should().Be(DescargaTallerAw.Recibido);
    }

    [Fact]
    public async Task Sin_medida_o_sin_sucursal_conocida_queda_en_revision()
    {
        var (db, g) = Guardado();
        await g.RegistrarAsync(EmpresaId, "Demo", new("90990011", 1, "100245", 1, null, null, "CIRCUITO", new DateOnly(2026, 11, 4)), default);
        await g.RegistrarAsync(EmpresaId, "Demo", Pedido(sucursal: "PERIFERICO") with { NumeroPedido = "90990012" }, default);
        var filas = await db.DescargasTallerAw.OrderBy(x => x.NumeroPedido).ToListAsync();
        filas.Should().OnlyContain(x => x.Estado == DescargaTallerAw.EnRevision);
        filas[0].Error.Should().Contain("medida");
        filas[1].Error.Should().Contain("PERIFERICO");
        (await db.Movimientos.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(26.7912, 30.0, 0.8930)]
    [InlineData(40, 40, 1)]
    public void Factor_es_m2_descargados_entre_m2_cortados(double descargados, double cortados, double esperado) =>
        FactorOptimizacionTaller.Calcular((decimal)descargados, (decimal)cortados).Should().Be((decimal)esperado);

    [Fact]
    public void Sin_m2_cortados_no_hay_factor()
    {
        FactorOptimizacionTaller.Calcular(26.79m, 0m).Should().BeNull();
    }

    [Fact]
    public void Cierre_guarda_el_factor_con_su_formula_y_rechaza_negativos()
    {
        var c = new CierreTaller(Circuito, new DateOnly(2026, 11, 4), 6, 2, 53.5824m, 60m, 4, 2, 2, "  Corte normal  ");
        c.FactorOptimizacion.Should().Be(0.8930m);
        c.Notas.Should().Be("Corte normal");
        FactorOptimizacionTaller.Formula.Should().Contain("m² descargados").And.Contain("m² cortados");

        c.Capturar(6, 2, 53.5824m, 50m, 4, 2, 2, null);
        c.FactorOptimizacion.Should().Be(1.0716m);

        var act = () => c.Capturar(-1, 0, 0, 0, 0, 0, 0, null);
        act.Should().Throw<BusinessRuleException>();
    }
}
