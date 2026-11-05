using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Corridas;
using Millet.Tesoreria.Application.Pasivos;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Pasivos;

/// <summary>I13.3 (86e3ke4cx): CA a, b y c de la ficha y el ajuste de Millet (basta una firma de Dirección) contra handlers reales.</summary>
public sealed class AutorizacionPagoComercioExteriorTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid ComprasMp = Guid.NewGuid();
    private static readonly Guid DirectorComercializacion = Guid.NewGuid();
    private static readonly Guid DirectorFinanzas = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static TesoreriaDbContext Db() => new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());

    private static async Task<(CuentaBancaria Cuenta, PasivoPendientePago Agente, PasivoPendientePago Comun)> Escenario(TesoreriaDbContext db)
    {
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        var agente = Pasivo(); var comun = Pasivo();
        db.CuentasBancarias.Add(cuenta); db.PasivosPendientesPago.AddRange(agente, comun);
        // El pasivo del agente aduanal está marcado como de importación; el otro no.
        db.AutorizacionesPagoComercioExterior.Add(new AutorizacionPagoComercioExterior(Empresa, agente.FacturaProveedorId, TipoOperacionComercioExterior.Importacion));
        await db.SaveChangesAsync();
        return (cuenta, agente, comun);
    }

    private static PasivoPendientePago Pasivo() =>
        new(Empresa, Guid.NewGuid(), Guid.NewGuid(), null, 5_000m, 5_000m, "MXN", null, new DateOnly(2026, 11, 20), null, "F-1", Ahora, "PUE");

    private static AutorizarPagoComercioExteriorHandler Firmar(TesoreriaDbContext db, Guid usuario, params string[] permisos) =>
        new(db, new Proveedores(), new Permisos(permisos), new User(usuario), new Clock());

    private static readonly string[] Nivel1Importacion = [PermisosCanonicos.TesoreriaPagosCeAutorizarImportacion];
    private static readonly string[] Direccion = [PermisosCanonicos.TesoreriaPagosCeAutorizarDireccion];

    private static Task<PagedResponse<PasivoParaCorridaResponse>> Candidatos(TesoreriaDbContext db) =>
        new PasivosParaCorridaHandler(db, new Proveedores()).Handle(new PasivosParaCorridaQuery("MXN"), default);

    [Fact]
    public void P19_reversa_del_anticipo_no_se_trata_como_factura()
    {
        var pasivo = Pasivo(); pasivo.ExigirReversaSoportada();
        pasivo.MarcarAnticipoAgente(Guid.NewGuid());
        var act = () => pasivo.ExigirReversaSoportada();
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CE_REVERSA_NO_DEFINIDA");
    }
    [Fact]
    public async Task P19_solicitud_de_50000_proyecta_el_anticipo_y_exige_las_dos_firmas()
    {
        await using var db = Db(); var id = Guid.NewGuid();
        var p = new Millet.Tesoreria.Application.EventListeners.PasivoAutorizadoParaPagoPayload(Empresa, Ahora, id,
            Guid.NewGuid(), null, 50000, 50000, "MXN", 1, new(2026, 11, 5), null, "CE-P19",
            OrigenTipo: "AnticipoAgente", OrigenId: id) { SucursalId = Guid.NewGuid(), TipoOperacionCe = 1 };
        await new Millet.Tesoreria.Application.EventListeners.ProyectarPasivoAutorizadoHandler(db, new Clock())
            .Handle(new(Guid.NewGuid(), p), default);
        var a = await db.PasivosPendientesPago.SingleAsync();
        a.OrigenTipo.Should().Be("AnticipoAgente"); a.SucursalAnticipoCeId.Should().Be(p.SucursalId);
        (await Candidatos(db)).Items.Should().BeEmpty();
        await Firmar(db, ComprasMp, Nivel1Importacion).Handle(new(id, 1), default);
        (await Candidatos(db)).Items.Should().BeEmpty();
        await Firmar(db, DirectorFinanzas, Direccion).Handle(new(id, 2), default);
        (await Candidatos(db)).Items.Should().ContainSingle(x => x.FacturaProveedorId == id);
    }
    [Fact]
    public async Task CA_a_sin_la_firma_de_Direccion_el_pasivo_no_es_seleccionable_ni_se_arma()
    {
        await using var db = Db();
        var (cuenta, agente, comun) = await Escenario(db);
        await Firmar(db, ComprasMp, Nivel1Importacion).Handle(new(agente.FacturaProveedorId, 1), default);

        var candidatos = await Candidatos(db);
        candidatos.Items.Select(x => x.FacturaProveedorId).Should().BeEquivalentTo([comun.FacturaProveedorId]);

        var crear = new CrearCorridaHandler(db, new EmpresaContext(), new User(Guid.NewGuid()), new Proveedores(), new Clock());
        var act = () => crear.Handle(new(cuenta.Id, [agente.FacturaProveedorId]), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_PASIVO_NO_PAGABLE");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CA_b_con_nivel_1_y_una_firma_de_cualquiera_de_los_dos_directores_aparece_y_se_arma(bool comercializacion)
    {
        await using var db = Db();
        var (cuenta, agente, _) = await Escenario(db);
        await Firmar(db, ComprasMp, Nivel1Importacion).Handle(new(agente.FacturaProveedorId, 1), default);
        var r = await Firmar(db, comercializacion ? DirectorComercializacion : DirectorFinanzas, Direccion).Handle(new(agente.FacturaProveedorId, 2), default);

        r.Estado.Should().Be("Autorizado");
        (await Candidatos(db)).Items.Select(x => x.FacturaProveedorId).Should().Contain(agente.FacturaProveedorId);
        var crear = new CrearCorridaHandler(db, new EmpresaContext(), new User(Guid.NewGuid()), new Proveedores(), new Clock());
        (await crear.Handle(new(cuenta.Id, [agente.FacturaProveedorId]), default)).Lineas.Should().ContainSingle();
    }

    [Fact]
    public async Task CA_c_la_misma_persona_no_firma_los_dos_niveles()
    {
        await using var db = Db();
        var (_, agente, _) = await Escenario(db);
        var ambos = new[] { PermisosCanonicos.TesoreriaPagosCeAutorizarImportacion, PermisosCanonicos.TesoreriaPagosCeAutorizarDireccion };
        await Firmar(db, DirectorComercializacion, ambos).Handle(new(agente.FacturaProveedorId, 1), default);

        var act = () => Firmar(db, DirectorComercializacion, ambos).Handle(new(agente.FacturaProveedorId, 2), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CE_PAGO_MISMA_PERSONA");
        (await Candidatos(db)).Items.Select(x => x.FacturaProveedorId).Should().NotContain(agente.FacturaProveedorId);
    }

    [Fact]
    public async Task Nivel_2_sin_nivel_1_y_firmas_sin_la_facultad_del_tipo_se_rechazan()
    {
        await using var db = Db();
        var (_, agente, _) = await Escenario(db);
        var sinNivel1 = () => Firmar(db, DirectorFinanzas, Direccion).Handle(new(agente.FacturaProveedorId, 2), default);
        await sinNivel1.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CE_PAGO_FALTA_NIVEL1");

        // Documentación de Exportación no firma el nivel 1 de una importación.
        var otroTipo = () => Firmar(db, ComprasMp, PermisosCanonicos.TesoreriaPagosCeAutorizarExportacion).Handle(new(agente.FacturaProveedorId, 1), default);
        await otroTipo.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == "CE_PAGO_SIN_FACULTAD");
        var direccionNivel1 = () => Firmar(db, ComprasMp, Direccion).Handle(new(agente.FacturaProveedorId, 1), default);
        await direccionNivel1.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Rechazo_saca_el_pasivo_y_el_nivel_1_puede_reabrirlo()
    {
        await using var db = Db();
        var (_, agente, _) = await Escenario(db);
        await Firmar(db, ComprasMp, Nivel1Importacion).Handle(new(agente.FacturaProveedorId, 1), default);
        var rechazar = new RechazarPagoComercioExteriorHandler(db, new Proveedores(), new Permisos(Direccion), new User(DirectorFinanzas), new Clock());

        var r = await rechazar.Handle(new(agente.FacturaProveedorId, "Falta el pedimento"), default);
        r.Estado.Should().Be("Rechazado");
        (await Candidatos(db)).Items.Should().NotContain(x => x.FacturaProveedorId == agente.FacturaProveedorId);
        var firmaTrasRechazo = () => Firmar(db, DirectorFinanzas, Direccion).Handle(new(agente.FacturaProveedorId, 2), default);
        await firmaTrasRechazo.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CE_PAGO_RECHAZADO");

        (await Firmar(db, ComprasMp, Nivel1Importacion).Handle(new(agente.FacturaProveedorId, 1), default)).Estado.Should().Be("PendienteNivel2");
    }

    [Fact]
    public async Task Bandeja_por_rol_ofrece_a_cada_quien_solo_lo_de_su_nivel()
    {
        await using var db = Db();
        var (_, agente, _) = await Escenario(db);
        Task<PagedResponse<AutorizacionPagoComercioExteriorResponse>> Lista(Guid yo, params string[] p) =>
            new ListarAutorizacionesPagoComercioExteriorHandler(db, new Proveedores(), new Permisos(p), new User(yo))
                .Handle(new(SoloMiNivel: true), default);

        (await Lista(ComprasMp, Nivel1Importacion)).Items.Should().ContainSingle().Which.PuedoFirmar.Should().BeTrue();
        (await Lista(DirectorFinanzas, Direccion)).Items.Should().BeEmpty("el nivel 2 espera al nivel 1");
        await Firmar(db, ComprasMp, Nivel1Importacion).Handle(new(agente.FacturaProveedorId, 1), default);
        (await Lista(DirectorFinanzas, Direccion)).Items.Should().ContainSingle();
        (await Lista(ComprasMp, Nivel1Importacion)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Pago_directo_de_un_pasivo_de_comercio_exterior_sin_las_dos_firmas_se_rechaza()
    {
        await using var db = Db();
        var (_, agente, comun) = await Escenario(db);
        var act = () => PagosComercioExterior.ExigirAutorizadosAsync(db, [agente.FacturaProveedorId], "PAGO_PASIVO_CE_SIN_AUTORIZAR", default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAGO_PASIVO_CE_SIN_AUTORIZAR");
        await PagosComercioExterior.ExigirAutorizadosAsync(db, [comun.FacturaProveedorId], "X", default);
    }

    [Fact]
    public async Task El_listener_marca_el_pasivo_de_comercio_exterior_al_proyectarlo()
    {
        await using var db = Db();
        var (_, agente, _) = await Escenario(db);
        var nuevo = Guid.NewGuid();
        var oc = Guid.NewGuid();
        var marcado = await PagosComercioExterior.MarcarAsync(db, new Marca(oc), [(Empresa, nuevo, oc), (Empresa, agente.FacturaProveedorId, null)], default);
        marcado.Should().BeTrue();
        await db.SaveChangesAsync();
        db.AutorizacionesPagoComercioExterior.Count(a => a.FacturaProveedorId == nuevo).Should().Be(1);
        db.AutorizacionesPagoComercioExterior.Count(a => a.FacturaProveedorId == agente.FacturaProveedorId).Should().Be(1, "no se duplica");
    }

    private sealed class Marca(Guid ocImportacion) : IPasivoComercioExteriorReadPort
    {
        public Task<TipoOperacionComercioExterior?> TipoAsync(Guid factura, Guid? oc, CancellationToken ct) =>
            Task.FromResult<TipoOperacionComercioExterior?>(oc == ocImportacion ? TipoOperacionComercioExterior.Importacion : null);
    }

    private sealed class EmpresaContext : ICurrentEmpresaContext
    { public Guid? Current => Empresa; public bool IsBypassed => false; public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } } }
    private sealed class User(Guid id) : ICurrentUserContext { public Guid? UserId => id; public string? UserName => "Prueba"; }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Ahora; }
    private sealed class Permisos(params string[] otorgados) : ICurrentUserPermissions
    { public ValueTask<bool> TieneAsync(string permiso, CancellationToken ct = default) => ValueTask.FromResult(otorgados.Contains(permiso)); }
    private sealed class Proveedores : IProveedorBancoReadPort
    {
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ProveedorBancoDto?>(Dto(id));
        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(ids.ToDictionary(i => i, Dto));
        private static ProveedorBancoDto Dto(Guid id) => new(id, "P", "Agente aduanal Ejemplo SA",
            [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", "012180001234567897", true, null, "MXN", "Titular", true, "Activa")]);
    }
}
