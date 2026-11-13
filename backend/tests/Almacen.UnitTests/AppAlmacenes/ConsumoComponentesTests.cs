using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Consumo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.AppAlmacenes;

/// <summary>A4.8: cálculo por norma (D3/D4) y descuento idempotente por (etiqueta, operación, componente) (D2), InMemory.</summary>
public class ConsumoComponentesTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly Guid Separador = Guid.NewGuid();
    private static readonly Guid Silicon = Guid.NewGuid();

    private static NormaConsumoComponente Norma(decimal consumo = 1m, decimal merma = 0m, decimal? factor = null) =>
        new(Guid.NewGuid(), consumo, merma, factor);

    [Fact]
    public void Sin_norma_no_calcula_y_explica_el_motivo()
    {
        var (cantidad, error) = ConsumoComponentes.Calcular("M2", 600, 900, null, "419010");
        cantidad.Should().BeNull();
        error.Should().Contain("Sin norma").And.Contain("419010");
    }

    [Theory]
    [InlineData("M2", 600, 900, 1, 0, 0.54)]          // área
    [InlineData("M2", 600, 900, 1, 10, 0.594)]        // área + 10 % de merma
    [InlineData("M LIN.", 600, 900, 1, 0, 3.0)]       // perímetro 2 × (0.6 + 0.9)
    [InlineData("PZA", 600, 900, 2, 0, 2)]            // por pieza
    public void Calcula_por_la_unidad_del_articulo(string unidad, double ancho, double alto, double consumo, double merma, double esperado)
    {
        var (cantidad, error) = ConsumoComponentes.Calcular(unidad, (decimal)ancho, (decimal)alto, Norma((decimal)consumo, (decimal)merma), "X");
        error.Should().BeNull();
        cantidad.Should().Be((decimal)esperado);
    }

    [Fact]
    public void Argon_en_M3_usa_el_factor_y_sin_factor_va_a_revision()
    {
        ConsumoComponentes.Calcular("M³", 1000, 1000, Norma(factor: 0.0127m), "530000").Cantidad.Should().Be(0.0127m);
        var (cantidad, error) = ConsumoComponentes.Calcular("M³", 1000, 1000, Norma(), "530000");
        cantidad.Should().BeNull();
        error.Should().Contain("factor");
    }

    [Fact]
    public void Sin_medidas_no_calcula_por_area()
    {
        ConsumoComponentes.Calcular("M2", null, 900, Norma(), "X").Error.Should().Contain("medidas");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 100)]
    public void Norma_invalida_se_rechaza(double consumo, double merma)
    {
        var act = () => Norma((decimal)consumo, (decimal)merma);
        act.Should().Throw<BusinessRuleException>();
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }

    private sealed class Resolver : IAppSalidaResolverPort
    {
        public Task<ResolverSalidaResultado> ResolverAsync(string sucursalCodigo, IReadOnlyList<GrupoSalidaApp> grupos, CancellationToken ct) =>
            Task.FromResult(sucursalCodigo == "MID"
                ? new ResolverSalidaResultado(null, Sucursal, [new GrupoSalidaResuelto(grupos[0].ArticuloAw == "SIL" ? Silicon : Separador, "M LIN.", null, null, null)])
                : new ResolverSalidaResultado($"No existe la sucursal '{sucursalCodigo}'.", Guid.Empty, []));
    }

    /// <summary>M-172: solo el silicón está marcado «Se consume por tambor a línea».</summary>
    private sealed class Articulos : IArticuloReadPort
    {
        private static ArticuloLectura L(Guid id) => new(id, "X", "X", "M LIN.", null, true, ConsumoPorTamborALinea: id == Silicon);
        public Task<ArticuloLectura?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ArticuloLectura?>(L(id));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(ids.ToDictionary(i => i, L));
    }

    private sealed class Usuario : IUsuarioServicioReadPort
    {
        public Task<UsuarioServicioLectura?> ObtenerReordenAsync(CancellationToken ct) =>
            Task.FromResult<UsuarioServicioLectura?>(new(Guid.NewGuid(), EmpresaId, true));
    }

    private static (AlmacenDbContext Db, ConsumoComponentesGuardado G) Guardado(bool conNorma, Eventos? eventos = null)
    {
        var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"consumo-{Guid.NewGuid():N}").Options, new FakeEmpresa());
        var alm = Guid.NewGuid(); var sub = Guid.NewGuid(); var ubi = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(alm, "MP", "Materia prima", Sucursal));
        db.SubAlmacenes.Add(new SubAlmacen(sub, alm, "INS", "Insumos", TipoSubAlmacen.Insumos));
        db.Ubicaciones.Add(new Ubicacion(ubi, sub, "ÚNICA", "Única", esDefault: true));
        db.SaldosInventario.Add(new SaldoInventario(ubi, sub, Separador, 100, 10m, null));
        if (conNorma) db.NormasConsumoComponente.Add(new NormaConsumoComponente(Separador, 1m, 0m, null) { EmpresaId = EmpresaId });
        db.SaveChanges();
        return (db, new ConsumoComponentesGuardado(db, new FakeEmpresa(), new Resolver(), new Usuario(), eventos ?? new Eventos(), new Articulos()));
    }

    private static PiezaComponenteAw Pieza(string sucursal = "MID") =>
        new("031781876", ConsumoComponentes.OperacionEnsamblado, "520042", 1, "17000030", 1, 600, 900, sucursal, new DateTime(2026, 11, 4, 9, 2, 18));

    [Fact]
    public async Task Por_omision_el_consumo_es_solo_estadistico_y_no_mueve_inventario()
    {
        var (db, g) = Guardado(conNorma: true);
        (await g.RegistrarAsync(EmpresaId, "Real", Pieza(), default)).Should().BeTrue();

        var c = await db.ConsumosComponenteAw.SingleAsync();
        c.Estado.Should().Be(ConsumoComponenteAw.Estadistico);
        c.Cantidad.Should().Be(3m);
        c.Folio.Should().BeNull();
        (await db.Movimientos.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Descuenta_una_sola_vez_por_etiqueta_operacion_y_componente()
    {
        var (db, g) = Guardado(conNorma: true);
        (await g.RegistrarAsync(EmpresaId, "Real", Pieza(), default, true)).Should().BeTrue();
        (await g.RegistrarAsync(EmpresaId, "Real", Pieza(), default, true)).Should().BeFalse();

        var c = await db.ConsumosComponenteAw.SingleAsync();
        c.Estado.Should().Be(ConsumoComponenteAw.Recibido);
        c.Cantidad.Should().Be(3m);
        c.Folio.Should().StartWith("M-SAL");
        (await db.Movimientos.Include(m => m.Lineas).SingleAsync()).Lineas.Single().Cantidad.Should().Be(3m);
        (await g.MarcaAguaAsync(EmpresaId, "Real", default)).Should().Be(new DateTime(2026, 11, 4, 9, 2, 18));
    }

    [Fact]
    public async Task Descontar_un_componente_publica_un_evento_de_salida_con_motivo_de_composicion()
    {
        var eventos = new Eventos();
        var (_, g) = Guardado(conNorma: true, eventos);
        await g.RegistrarAsync(EmpresaId, "Real", Pieza(), default, true);
        await g.RegistrarAsync(EmpresaId, "Real", Pieza(), default, true); // mismo escaneo: no duplica el evento

        var ev = eventos.Publicados.OfType<Millet.Almacen.Application.Integration.SalidaInventarioRegistradaIntegrationEvent>().Should().ContainSingle().Subject;
        ev.Causa.Should().Be("consumo_composicion");
        ev.MontoTotalMxn.Should().Be(30m);
    }

    [Fact]
    public async Task Sin_norma_queda_en_revision_y_el_reintento_descuenta_cuando_ya_existe()
    {
        var (db, g) = Guardado(conNorma: false);
        await g.RegistrarAsync(EmpresaId, "Real", Pieza(), default, true);
        var c = await db.ConsumosComponenteAw.AsNoTracking().SingleAsync();
        c.Estado.Should().Be(ConsumoComponenteAw.EnRevision);
        (await db.Movimientos.CountAsync()).Should().Be(0);

        db.NormasConsumoComponente.Add(new NormaConsumoComponente(Separador, 1m, 0m, null) { EmpresaId = EmpresaId });
        await db.SaveChangesAsync();
        (await g.EnRevisionAsync(EmpresaId, 10, default)).Should().ContainSingle();
        await g.ReintentarAsync(c.Id, default, true);

        (await db.ConsumosComponenteAw.AsNoTracking().SingleAsync()).Estado.Should().Be(ConsumoComponenteAw.Recibido);
        (await db.Movimientos.CountAsync()).Should().Be(1);
        (await g.EnRevisionAsync(EmpresaId, 10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Sucursal_sin_mapeo_queda_en_revision()
    {
        var (db, g) = Guardado(conNorma: true);
        await g.RegistrarAsync(EmpresaId, "Real", Pieza("PERIFERICO"), default, true);
        var c = await db.ConsumosComponenteAw.SingleAsync();
        c.Estado.Should().Be(ConsumoComponenteAw.EnRevision);
        c.Error.Should().Contain("PERIFERICO");
    }

    [Fact]
    public async Task Pieza_sin_componentes_queda_como_excepcion_sin_movimiento()
    {
        var (db, g) = Guardado(conNorma: true);
        await g.RegistrarAsync(EmpresaId, "Real", Pieza() with { ComponenteRef = "" }, default, true);
        (await db.ConsumosComponenteAw.SingleAsync()).Estado.Should().Be(ConsumoComponenteAw.Excepcion);
        (await db.Movimientos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Criterio4_el_silicon_por_tambor_no_se_descuenta_por_pieza_y_el_separador_si()
    {
        var (db, g) = Guardado(conNorma: true);
        for (var i = 1; i <= 5; i++)
        {
            var etiqueta = $"03178187{i}";
            await g.RegistrarAsync(EmpresaId, "Real", Pieza() with { Etiqueta = etiqueta }, default, true);
            await g.RegistrarAsync(EmpresaId, "Real", Pieza() with { Etiqueta = etiqueta, ComponenteRef = "SIL" }, default, true);
        }

        var consumos = await db.ConsumosComponenteAw.AsNoTracking().ToListAsync();
        consumos.Where(c => c.ComponenteRef == "SIL").Should().HaveCount(5).And.OnlyContain(c => c.Estado == ConsumoComponenteAw.PorTambor && c.Folio == null);
        consumos.Where(c => c.ComponenteRef == "520042").Should().HaveCount(5).And.OnlyContain(c => c.Estado == ConsumoComponenteAw.Recibido);
        var lineas = await db.Movimientos.Include(m => m.Lineas).SelectMany(m => m.Lineas).ToListAsync();
        lineas.Should().HaveCount(5).And.OnlyContain(l => l.ArticuloId == Separador);
    }
}
