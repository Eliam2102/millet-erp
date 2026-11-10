using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Liberacion;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Liberacion;

/// <summary>C7.2 (86e3kdxy6): administración de las reglas de serie.</summary>
public sealed class ReglasLiberacionSerieAdminTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();

    [Fact]
    public void Series_de_Millet_son_las_del_cuestionario_04()
    {
        var siempre = ReglaLiberacionSerieConfiguration.SeriesMillet
            .Where(s => s.Comportamiento == ComportamientoSerie.SiempreLibera).Select(s => s.Prefijo);
        var nunca = ReglaLiberacionSerieConfiguration.SeriesMillet
            .Where(s => s.Comportamiento == ComportamientoSerie.NuncaLibera).Select(s => s.Prefijo);

        siempre.Should().BeEquivalentTo(["50", "55", "56", "70", "72", "75"]);
        nunca.Should().BeEquivalentTo(["58", "59", "78", "79", "90"]);
        ReglaLiberacionSerieConfiguration.SeriesMillet.Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Listar_admin_incluye_inactivas_ordenadas_por_serie()
    {
        await using var db = CrearDbContext();
        db.ReglasLiberacionSerie.Add(new ReglaLiberacionSerie(Guid.NewGuid(), "6100", ComportamientoSerie.EvaluaCredito, activo: false));
        await db.SaveChangesAsync();

        var reglas = await new ListarReglasLiberacionSerieAdminHandler(db)
            .Handle(new ListarReglasLiberacionSerieAdminQuery(), CancellationToken.None);

        reglas.Should().HaveCount(12);
        reglas.Select(r => r.Prefijo).Should().BeInAscendingOrder(StringComparer.Ordinal);
        reglas.Should().Contain(r => r.Prefijo == "6100" && !r.Activo);
    }

    [Fact]
    public async Task Crear_da_de_alta_una_serie_nueva_activa()
    {
        await using var db = CrearDbContext();

        var res = await new CrearReglaLiberacionSerieHandler(db).Handle(
            new CrearReglaLiberacionSerieCommand(" 6100 ", ComportamientoSerie.NuncaLibera), CancellationToken.None);

        res.Prefijo.Should().Be("6100");
        res.Activo.Should().BeTrue();
        (await db.ReglasLiberacionSerie.SingleAsync(r => r.Prefijo == "6100")).Comportamiento
            .Should().Be(ComportamientoSerie.NuncaLibera);
    }

    [Fact]
    public async Task Crear_una_serie_existente_es_conflicto()
    {
        await using var db = CrearDbContext();

        var act = () => new CrearReglaLiberacionSerieHandler(db).Handle(
            new CrearReglaLiberacionSerieCommand("78", ComportamientoSerie.SiempreLibera), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == "RLS_PREFIJO_DUPLICADO");
    }

    [Theory]
    [InlineData("")]
    [InlineData("78A0")]
    [InlineData("12345678901")]
    public void Validador_rechaza_series_que_no_son_digitos(string prefijo)
    {
        var resultado = new CrearReglaLiberacionSerieValidator().Validate(
            new CrearReglaLiberacionSerieCommand(prefijo, ComportamientoSerie.SiempreLibera));

        resultado.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Dominio_rechaza_comportamiento_fuera_del_catalogo()
    {
        var regla = new ReglaLiberacionSerie(Guid.NewGuid(), "6100", ComportamientoSerie.EvaluaCredito);

        var act = () => regla.CambiarComportamiento((ComportamientoSerie)9);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "RLS_COMPORTAMIENTO_INVALIDO");
    }

    [Fact]
    public async Task Cambiar_con_version_vieja_es_conflicto_de_concurrencia()
    {
        await using var db = CrearDbContext();
        var regla = await db.ReglasLiberacionSerie.SingleAsync(r => r.Prefijo == "55");

        var act = () => new CambiarComportamientoSerieHandler(db).Handle(
            new CambiarComportamientoSerieCommand(regla.Id, regla.Version + 1, ComportamientoSerie.NuncaLibera),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        (await db.ReglasLiberacionSerie.SingleAsync(r => r.Prefijo == "55")).Comportamiento
            .Should().Be(ComportamientoSerie.SiempreLibera);
    }

    [Fact]
    public async Task Cambiar_una_regla_inexistente_es_404()
    {
        await using var db = CrearDbContext();

        var act = () => new CambiarEstadoReglaSerieHandler(db).Handle(
            new CambiarEstadoReglaSerieCommand(Guid.NewGuid(), 1, Activo: false), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "RLS_NO_ENCONTRADA");
    }

    [Fact]
    public async Task Desactivar_y_reactivar_una_regla()
    {
        await using var db = CrearDbContext();
        var regla = await db.ReglasLiberacionSerie.SingleAsync(r => r.Prefijo == "90");
        var handler = new CambiarEstadoReglaSerieHandler(db);

        var inactiva = await handler.Handle(new CambiarEstadoReglaSerieCommand(regla.Id, regla.Version, false), CancellationToken.None);
        inactiva.Activo.Should().BeFalse();
        var activa = await handler.Handle(new CambiarEstadoReglaSerieCommand(regla.Id, inactiva.Version, true), CancellationToken.None);
        activa.Activo.Should().BeTrue();
    }

    [Fact]
    public void Historial_lee_alta_y_cambio_del_interceptor_de_auditoria()
    {
        var alta = CambiosRegla.Leer("""{"snapshot":{"Prefijo":"6100","Comportamiento":2,"Activo":true}}""");
        alta.ComportamientoDespues.Should().Be(ComportamientoSerie.NuncaLibera);
        alta.ActivoDespues.Should().BeTrue();
        alta.ComportamientoAntes.Should().BeNull();

        var cambio = CambiosRegla.Leer("""{"diff":{"Comportamiento":{"antes":1,"despues":3,"antesTexto":"SiempreLibera"}}}""");
        cambio.ComportamientoAntes.Should().Be(ComportamientoSerie.SiempreLibera);
        cambio.ComportamientoDespues.Should().Be(ComportamientoSerie.EvaluaCredito);
        cambio.ActivoAntes.Should().BeNull();

        var estado = CambiosRegla.Leer("""{"diff":{"Activo":{"antes":true,"despues":false}}}""");
        estado.ActivoAntes.Should().BeTrue();
        estado.ActivoDespues.Should().BeFalse();

        CambiosRegla.Leer("no es json").Should().Be(new CambiosRegla.Valores(null, null, null, null));
    }

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_reglas_serie_{Guid.NewGuid()}")
            .Options;
        var db = new CuentasPorCobrarDbContext(options, new FakeEmpresaContext(EmpresaId));
        db.ReglasLiberacionSerie.AddRange(
            ReglaLiberacionSerieConfiguration.SeriesMillet.Select(r =>
                new ReglaLiberacionSerie(r.Id, r.Prefijo, r.Comportamiento)));
        db.SaveChanges();
        return db;
    }

    private sealed class FakeEmpresaContext(Guid empresaId) : ICurrentEmpresaContext
    {
        public Guid? Current => empresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
