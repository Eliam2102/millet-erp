using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Mermas;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Mermas;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.AppAlmacenes;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Mermas;

/// <summary>A4.4 (InMemory): desperdicio de mesa (a), merma por etapa con autorización (b) y sin autorización (c).</summary>
public class MermaProcesoTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly Guid Vidrio = Guid.NewGuid();
    private static readonly Guid Autorizador = Guid.NewGuid();

    private static AlmacenDbContext Db(decimal existencia = 40, decimal costo = 180m)
    {
        var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"merma-{Guid.NewGuid():N}").Options, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        var alm = Guid.NewGuid(); var sub = Guid.NewGuid(); var ubi = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(alm, "ALM-CNK", "Conkal", Sucursal));
        db.SubAlmacenes.Add(new SubAlmacen(sub, alm, "MP", "Materia prima", TipoSubAlmacen.MaterialesDirectos));
        db.Ubicaciones.Add(new Ubicacion(ubi, sub, "ÚNICA", "Única", esDefault: true));
        db.SaldosInventario.Add(new SaldoInventario(ubi, sub, Vidrio, existencia, costo, null));
        db.SaveChanges();
        return db;
    }

    private static MermaApp Desperdicio(params (string Mesa, decimal M2)[] mesas) =>
        new(MermaProceso.DesperdicioMesa, Sucursal, new DateOnly(2026, 11, 4), null, null,
            mesas.Select(m => new MermaLineaApp(m.Mesa, Vidrio, m.M2, null)).ToList());

    private static MermaApp Merma(string etapa = "templado") =>
        new(MermaProceso.Merma, Sucursal, new DateOnly(2026, 11, 4), etapa, "Rotura en horno",
            [new MermaLineaApp(null, Vidrio, 0.8m, 12m)]);

    [Fact]
    public async Task A44a_el_desperdicio_del_dia_queda_por_mesa_aplicado_y_avisa_a_contabilidad()
    {
        var db = Db();
        var eventos = new Eventos();
        var (estado, error) = await MermaProcesoApp.RegistrarAsync(db, eventos, EmpresaId,
            Desperdicio(("HEGLA1", 3.2m), ("hegla2", 2.5m), ("Bystronic", 1.1m)), "m-1", default);
        await db.SaveChangesAsync();

        estado.Should().Be("RECIBIDO"); error.Should().BeNull();
        var filas = await db.MermasProceso.ToListAsync();
        filas.Select(f => f.Mesa).Should().BeEquivalentTo("HEGLA1", "HEGLA2", "BYSTRONIC");
        filas.Should().OnlyContain(f => f.Estado == MermaProceso.Aplicada && f.Etapa == "corte" && f.Tipo == MermaProceso.DesperdicioMesa);
        filas.Single(f => f.Mesa == "HEGLA1").ValorMxn.Should().Be(576m); // 3.2 m² × 180
        eventos.Publicados.OfType<MermaProcesoAplicadaIntegrationEvent>().Should().HaveCount(3);
    }

    [Fact]
    public async Task El_desperdicio_de_la_misma_mesa_el_mismo_dia_no_se_registra_dos_veces()
    {
        var db = Db();
        await MermaProcesoApp.RegistrarAsync(db, new Eventos(), EmpresaId, Desperdicio(("HEGLA1", 3m)), "m-1", default);
        await db.SaveChangesAsync();

        var (estado, error) = await MermaProcesoApp.RegistrarAsync(db, new Eventos(), EmpresaId, Desperdicio(("HEGLA1", 1m)), "m-2", default);

        estado.Should().Be("ERROR");
        error.Should().Contain("HEGLA1");
        (await db.MermasProceso.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A44b_merma_de_12_kg_en_templado_queda_con_etapa_motivo_valor_y_autorizacion()
    {
        var db = Db();
        var eventos = new Eventos();
        await MermaProcesoApp.RegistrarAsync(db, eventos, EmpresaId, Merma(), "m-3", default);
        await db.SaveChangesAsync();
        var m = await db.MermasProceso.SingleAsync();

        await MermaProcesoApp.AutorizarAsync(eventos, m, Autorizador, default);
        await db.SaveChangesAsync();

        m = await db.MermasProceso.SingleAsync();
        m.Etapa.Should().Be("templado"); m.Motivo.Should().Be("Rotura en horno");
        m.Kg.Should().Be(12m); m.M2.Should().Be(0.8m); m.ValorMxn.Should().Be(144m); // 0.8 m² × 180
        m.Estado.Should().Be(MermaProceso.Aplicada); m.AutorizadoPor.Should().Be(Autorizador); m.AutorizadoAt.Should().NotBeNull();
        var evento = eventos.Publicados.OfType<MermaProcesoAplicadaIntegrationEvent>().Single();
        evento.ValorMxn.Should().Be(144m); evento.Etapa.Should().Be("templado"); evento.AutorizadoPor.Should().Be(Autorizador);
    }

    [Fact]
    public async Task A44c_merma_sin_autorizacion_queda_pendiente_y_no_avisa()
    {
        var db = Db();
        var eventos = new Eventos();
        await MermaProcesoApp.RegistrarAsync(db, eventos, EmpresaId, Merma(), "m-4", default);
        await db.SaveChangesAsync();

        var m = await db.MermasProceso.SingleAsync();
        m.Estado.Should().Be(MermaProceso.Pendiente);
        m.AutorizadoPor.Should().BeNull();
        eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Una_merma_ya_autorizada_o_rechazada_no_vuelve_a_decidirse()
    {
        var db = Db();
        var eventos = new Eventos();
        await MermaProcesoApp.RegistrarAsync(db, eventos, EmpresaId, Merma(), "m-5", default);
        var m = db.MermasProceso.Local.Single();
        await MermaProcesoApp.AutorizarAsync(eventos, m, Autorizador, default);

        var otra = async () => await MermaProcesoApp.AutorizarAsync(eventos, m, Autorizador, default);
        await otra.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "MERMA_NO_PENDIENTE");
        eventos.Publicados.Should().HaveCount(1);
    }

    [Fact]
    public void Rechazar_exige_motivo_y_la_etapa_debe_ser_de_la_lista()
    {
        var m = MermaProceso.PorEtapa(EmpresaId, Sucursal, new DateOnly(2026, 11, 4), "laminado", "Burbuja", Vidrio, 5m, 0.4m, 100m, null);
        var sinMotivo = () => m.Rechazar(Autorizador, " ");
        sinMotivo.Should().Throw<BusinessRuleException>().Where(e => e.Code == "MERMA_RECHAZO_SIN_MOTIVO");
        m.Rechazar(Autorizador, "No corresponde al lote");
        m.Estado.Should().Be(MermaProceso.Rechazada);

        var etapaMala = () => MermaProceso.PorEtapa(EmpresaId, Sucursal, new DateOnly(2026, 11, 4), "pulido", "x", Vidrio, 5m, 0.4m, 100m, null);
        etapaMala.Should().Throw<BusinessRuleException>().Where(e => e.Code == "MERMA_ETAPA_INVALIDA");
    }

    [Fact]
    public async Task El_costo_es_ponderado_y_sin_existencia_usa_el_ultimo_saldo()
    {
        var sinExistencia = Db(existencia: 0, costo: 95m);
        (await MermaProcesoApp.CostoPromedioM2Async(sinExistencia, Sucursal, Vidrio, default)).Should().Be(95m);
        (await MermaProcesoApp.CostoPromedioM2Async(sinExistencia, Sucursal, Guid.NewGuid(), default)).Should().BeNull();

        var (estado, error) = await MermaProcesoApp.RegistrarAsync(sinExistencia, new Eventos(), EmpresaId,
            Merma() with { Lineas = [new MermaLineaApp(null, Guid.NewGuid(), 1m, 5m)] }, "m-6", default);
        estado.Should().Be("ERROR"); error.Should().Contain("costo promedio");
    }

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
