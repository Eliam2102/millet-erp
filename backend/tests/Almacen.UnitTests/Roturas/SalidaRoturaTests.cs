using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Roturas;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Roturas;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.AppAlmacenes;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.Roturas;

/// <summary>ALM-03 (InMemory): captura sin efecto, autorización a costo promedio con evento único y reglas comunes de salida.</summary>
public class SalidaRoturaTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Vidrio = Guid.NewGuid();
    private static readonly Guid Almacenista = Guid.NewGuid();
    private static readonly Guid Jefe = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 11, 5);
    private Guid _sub, _ubi;

    private AlmacenDbContext Db(decimal existencia = 20m, decimal costo = 900m, TipoSubAlmacen tipo = TipoSubAlmacen.MaterialesDirectos)
    {
        var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"rotura-{Guid.NewGuid():N}").Options, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        var alm = Guid.NewGuid(); _sub = Guid.NewGuid(); _ubi = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(alm, "ALM-CNK", "Conkal", Guid.NewGuid()));
        db.SubAlmacenes.Add(new SubAlmacen(_sub, alm, "MP", "Materia prima", tipo));
        db.Ubicaciones.Add(new Ubicacion(_ubi, _sub, "R-01", "Rack 01"));
        db.SaldosInventario.Add(new SaldoInventario(_ubi, _sub, Vidrio, existencia, costo, null));
        db.SaveChanges();
        return db;
    }

    private CapturarRoturaApp Captura(decimal cantidad = 2m, DateOnly? fecha = null, string motivo = "rotura_rack", string? comentario = null) =>
        new(_ubi, Vidrio, cantidad, fecha ?? Hoy, motivo, comentario);

    private static Task<SalidaRotura> Capturar(AlmacenDbContext db, CapturarRoturaApp c, bool periodoAbierto = true) =>
        SalidaRoturaApp.CapturarAsync(db, new PeriodoContableStub(periodoAbierto), new Decimales(), new Articulos(), EmpresaId, Almacenista, c, default);

    [Fact]
    public async Task Capturar_deja_la_rotura_pendiente_y_no_crea_movimiento_ni_evento()
    {
        var db = Db();
        var r = await Capturar(db, Captura());
        await db.SaveChangesAsync();

        r.Estado.Should().Be(SalidaRotura.Pendiente);
        (await db.Movimientos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Autorizar_crea_el_movimiento_de_rotura_a_costo_promedio_y_publica_el_evento_una_vez()
    {
        var db = Db();
        var r = await Capturar(db, Captura(2m));
        await db.SaveChangesAsync();
        var eventos = new Eventos();

        await SalidaRoturaApp.AutorizarAsync(db, eventos, new PeriodoContableStub(true), r, Jefe, default);

        r.Estado.Should().Be(SalidaRotura.Autorizada);
        r.ValorMxn.Should().Be(1800m);
        var mov = await db.Movimientos.Include(m => m.Lineas).SingleAsync();
        mov.Tipo.Should().Be(TipoMovimiento.SalidaPorRotura);
        mov.Folio.Should().StartWith("M-ROT2026-");
        mov.Lineas.Single().Cantidad.Should().Be(2m);
        mov.Lineas.Single().CostoUnitarioMxn.Should().Be(900m);
        var evento = eventos.Publicados.OfType<SalidaRoturaRegistradaIntegrationEvent>().Should().ContainSingle().Subject;
        evento.EventType.Should().Be("almacen.salida_rotura.registrada.v1");
        (evento.Cantidad, evento.CostoUnitarioMxn, evento.ValorMxn, evento.MotivoCodigo).Should().Be((2m, 900m, 1800m, "rotura_rack"));
        eventos.Publicados.OfType<SalidaInventarioRegistradaIntegrationEvent>().Should().BeEmpty();
    }

    // 86e3zf04y: el área o el equipo capturados viajan a la póliza de merma (evento) y quedan en la línea del movimiento.
    [Fact]
    public async Task El_area_o_equipo_capturados_viajan_en_el_evento_y_en_la_linea_del_movimiento()
    {
        var db = Db();
        var r = await Capturar(db, Captura(2m));
        var equipo = Guid.NewGuid();
        r.AsignarAreaEquipo(" 500-01 ", "M-07", equipo);
        await db.SaveChangesAsync();
        var eventos = new Eventos();

        await SalidaRoturaApp.AutorizarAsync(db, eventos, new PeriodoContableStub(true), r, Jefe, default);

        (r.AreaClave, r.EquipoClave, r.CentroCostoId).Should().Be(("500-01", "M-07", equipo));
        eventos.Publicados.OfType<SalidaRoturaRegistradaIntegrationEvent>().Single().CentroCostoId.Should().Be(equipo);
        (await db.Movimientos.Include(m => m.Lineas).SingleAsync()).Lineas.Single().CentroCostoId.Should().Be(equipo);
        var otraVez = () => r.AsignarAreaEquipo("500-02", null, Guid.NewGuid());
        otraVez.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ROTURA_NO_PENDIENTE");
    }

    [Fact]
    public async Task Sin_area_ni_equipo_el_evento_va_sin_centro()
    {
        var db = Db();
        var r = await Capturar(db, Captura(1m));
        await db.SaveChangesAsync();
        var eventos = new Eventos();

        await SalidaRoturaApp.AutorizarAsync(db, eventos, new PeriodoContableStub(true), r, Jefe, default);

        eventos.Publicados.OfType<SalidaRoturaRegistradaIntegrationEvent>().Single().CentroCostoId.Should().BeNull();
    }

    [Fact]
    public async Task El_mismo_usuario_no_puede_autorizar_su_captura()
    {
        var db = Db();
        var r = await Capturar(db, Captura());
        await db.SaveChangesAsync();

        var act = () => SalidaRoturaApp.AutorizarAsync(db, new Eventos(), new PeriodoContableStub(true), r, Almacenista, default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ROTURA_MISMO_USUARIO");
        r.Estado.Should().Be(SalidaRotura.Pendiente);
        (await db.Movimientos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Autorizar_una_rotura_de_25_con_saldo_18_se_rechaza_por_saldo_insuficiente()
    {
        var db = Db(existencia: 18m);
        var r = await Capturar(db, Captura(25m));
        await db.SaveChangesAsync();
        var eventos = new Eventos();

        var act = () => SalidaRoturaApp.AutorizarAsync(db, eventos, new PeriodoContableStub(true), r, Jefe, default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ROTURA_SALDO_INSUFICIENTE");
        r.Estado.Should().Be(SalidaRotura.Pendiente);
        eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Con_inventario_anual_en_curso_no_se_captura_ni_se_autoriza()
    {
        var db = Db();
        var r = await Capturar(db, Captura());
        await db.SaveChangesAsync();
        db.Add(new BloqueoInventario(Guid.NewGuid(), Guid.NewGuid(), _sub));
        await db.SaveChangesAsync();

        var capturar = () => Capturar(db, Captura());
        var autorizar = () => SalidaRoturaApp.AutorizarAsync(db, new Eventos(), new PeriodoContableStub(true), r, Jefe, default);

        (await capturar.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("SALIDA_BLOQUEADA_POR_INVENTARIO_ANUAL");
        (await autorizar.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("SALIDA_BLOQUEADA_POR_INVENTARIO_ANUAL");
    }

    [Fact]
    public async Task Con_el_periodo_cerrado_no_se_captura()
    {
        var db = Db();
        db.Add(new PeriodoCerrado(Guid.NewGuid(), EmpresaId, 2026, 9, Jefe));
        await db.SaveChangesAsync();

        var act = () => Capturar(db, Captura(fecha: new DateOnly(2026, 9, 15)));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PERIODO_CERRADO");
    }

    [Fact]
    public async Task Con_el_periodo_contable_cerrado_no_se_captura()
    {
        var db = Db();

        var act = () => Capturar(db, Captura(), periodoAbierto: false);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PERIODO_CONTABLE_NO_ADMITE");
    }

    [Fact]
    public async Task El_material_en_revision_no_se_da_de_baja_por_rotura()
    {
        var db = Db(tipo: TipoSubAlmacen.MaterialEnRevision);

        var act = () => Capturar(db, Captura());

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ROTURA_DESDE_REVISION");
    }

    [Theory]
    [InlineData("no_existe", null, "ROTURA_MOTIVO_INVALIDO")]
    [InlineData("otro", null, "ROTURA_COMENTARIO_REQUERIDO")]
    public async Task El_motivo_debe_ser_del_catalogo_y_otro_pide_comentario(string motivo, string? comentario, string codigo)
    {
        var db = Db();

        var act = () => Capturar(db, Captura(motivo: motivo, comentario: comentario));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(codigo);
    }

    [Fact]
    public async Task Rechazar_pide_motivo_y_una_decidida_no_se_vuelve_a_decidir()
    {
        var db = Db();
        var r = await Capturar(db, Captura());

        var sinMotivo = () => r.Rechazar(Jefe, " ");
        sinMotivo.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ROTURA_RECHAZO_SIN_MOTIVO");
        r.Rechazar(Jefe, "Falta la foto de la hoja rota");
        r.Estado.Should().Be(SalidaRotura.Rechazada);
        var otraVez = () => r.VerificarAutorizacion(Jefe);
        otraVez.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ROTURA_NO_PENDIENTE");
    }

    private sealed class Decimales : IDecimalesUnidadGuard
    {
        public Task ValidarAsync(IEnumerable<CantidadAValidar> cantidades, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Articulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(new ArticuloLectura(articuloId, "VCL06", "Vidrio claro 6 mm", "PZA", null, true));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(new Dictionary<Guid, ArticuloLectura>());
    }

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
