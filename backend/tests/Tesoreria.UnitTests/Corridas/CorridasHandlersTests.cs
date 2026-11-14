using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Corridas;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Application.PublicPorts;
using Millet.Tesoreria.Domain.Corridas;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Corridas;

/// <summary>T9.1-A (86e3ke5bt): criterios 1, 2, 4, 5 de la ficha y T9.1-a contra handlers reales con BD en memoria.</summary>
public sealed class CorridasHandlersTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Armador = Guid.NewGuid();
    private static readonly Guid JefeTesoreria = Guid.NewGuid();
    private static readonly Guid Gerente = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static TesoreriaDbContext Db() => new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());

    private static async Task<(CuentaBancaria, List<PasivoPendientePago>)> Escenario(TesoreriaDbContext db, params (Guid Proveedor, decimal Monto)[] pasivos)
    {
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        cuenta.RegistrarSaldoInicial(10_000_000m, new DateOnly(2000, 1, 1), "Saldo para la corrida (M-264: sin saldo se bloquea)");
        var lista = pasivos.Select(x => CorridaPagoDominioTests.Pasivo(x.Proveedor, x.Monto, x.Monto, 0m, 0m, empresa: Empresa)).ToList();
        db.CuentasBancarias.Add(cuenta); db.PasivosPendientesPago.AddRange(lista);
        await db.SaveChangesAsync();
        return (cuenta, lista);
    }

    private static CrearCorridaHandler Crear(TesoreriaDbContext db, Guid usuario, Proveedores? p = null) =>
        new(db, new EmpresaContext(), new User(usuario), p ?? new Proveedores(), new Clock());

    private static AutorizarCorridaHandler Autorizar(TesoreriaDbContext db, Guid usuario) =>
        new(db, new EmpresaContext(), new User(usuario), new Roles(), new Proveedores(), new Clock());

    private static async Task<CorridaResponse> Enviar(TesoreriaDbContext db, CorridaResponse c) =>
        await new EnviarCorridaAAutorizacionHandler(db, new Proveedores(), new Clock()).Handle(new(c.Id, c.Version), default);

    [Fact]
    public async Task CA1_cinco_pasivos_de_tres_proveedores_se_arman_y_el_jefe_autoriza_cuatro()
    {
        await using var db = Db();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var (cuenta, p) = await Escenario(db, (a, 1_000m), (a, 2_000m), (b, 3_000m), (c, 4_000m), (c, 5_000m));

        // Se arman 4 de los 5 (el quinto no se elige).
        var r = await Crear(db, Armador).Handle(new(cuenta.Id, p.Take(4).Select(x => x.FacturaProveedorId).ToList()), default);
        r.Lineas.Should().HaveCount(4);
        r.Proveedores.Should().HaveCount(3);
        r.Proveedores.Single(x => x.ProveedorId == a).Neto.Should().Be(3_000m);
        r.Proveedores.Single(x => x.ProveedorId == c).Facturas.Should().Be(1);
        r.Total.Should().Be(10_000m);
        r.Bruto.Should().Be(10_000m);
        r.Retenciones.Should().Be(0m);

        r = await Enviar(db, r);
        r.FirmasRequeridas.Should().Be(1);
        r = await Autorizar(db, JefeTesoreria).Handle(new(r.Id, r.Version), default);

        r.Estado.Should().Be("Autorizada");
        r.AutorizadaPor.Should().Be(JefeTesoreria);
        r.Lineas.Select(l => l.FacturaProveedorId).Should().NotContain(p[4].FacturaProveedorId);
    }

    [Fact]
    public async Task Fecha_programada_para_el_portal_es_la_de_la_corrida_autorizada_y_solo_de_esa()
    {
        await using var db = Db();
        var (cuenta, p) = await Escenario(db, (Guid.NewGuid(), 1_000m), (Guid.NewGuid(), 2_000m));
        var r = await Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default);
        var consulta = new ConsultarFechasProgramadasPagoHandler(db);
        var ids = p.Select(x => x.FacturaProveedorId).ToArray();

        (await consulta.Handle(new(ids), default)).Should().BeEmpty("una corrida en borrador no programa nada");
        r = await Enviar(db, r);
        r = await Autorizar(db, JefeTesoreria).Handle(new(r.Id, r.Version), default);

        var fechas = await consulta.Handle(new(ids), default);
        fechas.Should().ContainSingle().Which.Should().Be(new KeyValuePair<Guid, DateOnly>(p[0].FacturaProveedorId, DateOnly.FromDateTime(Ahora.UtcDateTime)));
    }

    [Fact]
    public async Task Bruto_retenciones_y_neto_por_proveedor_salen_del_CFDI()
    {
        await using var db = Db();
        var prov = Guid.NewGuid();
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        cuenta.RegistrarSaldoInicial(10_000_000m, new DateOnly(2000, 1, 1), "Saldo para la corrida (M-264: sin saldo se bloquea)");
        var p = CorridaPagoDominioTests.Pasivo(prov, 1_053.33m, 1_000m, 160m, 106.67m, empresa: Empresa);
        db.CuentasBancarias.Add(cuenta); db.PasivosPendientesPago.Add(p); await db.SaveChangesAsync();

        var r = await Crear(db, Armador).Handle(new(cuenta.Id, [p.FacturaProveedorId]), default);

        var x = r.Proveedores.Single();
        (x.Bruto, x.Retenciones, x.Neto).Should().Be((1_160m, 106.67m, 1_053.33m));
    }

    // F-TES-05: corrida en USD con una factura MXN responde moneda distinta, no que al proveedor le falta cuenta en USD.
    [Fact]
    public async Task Corrida_en_otra_moneda_responde_moneda_distinta()
    {
        await using var db = Db();
        var (_, p) = await Escenario(db, (Guid.NewGuid(), 100m));
        var usd = new CuentaBancaria(Empresa, "BBVA", "9876543210", null, "USD"); usd.RegistrarSaldoInicial(10_000_000m, new DateOnly(2000, 1, 1), "Saldo de prueba"); db.CuentasBancarias.Add(usd); await db.SaveChangesAsync();
        var act = () => Crear(db, Armador).Handle(new(usd.Id, [p[0].FacturaProveedorId]), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_MONEDA_DISTINTA");
        db.CorridasPago.Should().BeEmpty();
    }

    [Fact]
    public async Task CA2_pasivo_en_otra_corrida_viva_se_rechaza_con_el_nombre_de_la_corrida()
    {
        await using var db = Db();
        var (cuenta, p) = await Escenario(db, (Guid.NewGuid(), 1_000m), (Guid.NewGuid(), 2_000m));
        var primera = await Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId], "Corrida de la semana 44"), default);
        await Enviar(db, primera);

        var act = () => Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId, p[1].FacturaProveedorId]), default);

        (await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_PASIVO_EN_OTRA_CORRIDA"))
            .Which.Message.Should().Contain("Corrida de la semana 44");
    }

    [Fact]
    public async Task Pasivo_de_corrida_cancelada_vuelve_a_ser_seleccionable()
    {
        await using var db = Db();
        var (cuenta, p) = await Escenario(db, (Guid.NewGuid(), 1_000m));
        var primera = await Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default);
        await new CancelarCorridaHandler(db, new Proveedores()).Handle(new(primera.Id, "error", primera.Version), default);

        var segunda = await Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default);

        segunda.Lineas.Should().HaveCount(1);
    }

    [Fact]
    public async Task T91a_pasivo_retirado_por_CxP_no_es_seleccionable_ni_aparece_como_candidato()
    {
        await using var db = Db();
        var (cuenta, p) = await Escenario(db, (Guid.NewGuid(), 1_000m), (Guid.NewGuid(), 2_000m));
        var retirado = await db.PasivosPendientesPago.FirstAsync(x => x.Id == p[0].Id);
        retirado.RetirarDePago("en revisión documental", Ahora);
        await db.SaveChangesAsync();

        var candidatos = await new PasivosParaCorridaHandler(db, new Proveedores()).Handle(new("MXN"), default);
        candidatos.Items.Select(x => x.FacturaProveedorId).Should().BeEquivalentTo([p[1].FacturaProveedorId]);

        var act = () => Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_PASIVO_NO_PAGABLE");
    }

    [Fact]
    public async Task CA8_4_proveedor_sin_opinion_vigente_aparece_detenido_por_documentacion_y_no_entra_a_la_corrida()
    {
        await using var db = Db();
        Guid sinOpinion = Guid.NewGuid(), conOpinion = Guid.NewGuid();
        var (cuenta, p) = await Escenario(db, (sinOpinion, 1_000m), (conOpinion, 2_000m));
        var proveedores = new Proveedores { SinOpinion = [sinOpinion] };

        var candidatos = (await new PasivosParaCorridaHandler(db, proveedores).Handle(new("MXN"), default)).Items;
        candidatos.Single(x => x.ProveedorId == sinOpinion).Should().Match<PasivoParaCorridaResponse>(x => x.Detenida && x.MotivoDetencion == "documentación");
        candidatos.Single(x => x.ProveedorId == conOpinion).Detenida.Should().BeFalse();

        var act = () => Crear(db, Armador, proveedores).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_PASIVO_DETENIDO_DOCUMENTACION");

        // Con la opinión vigente en el expediente, la misma factura se arma sin cambios.
        var r = await Crear(db, Armador, new Proveedores()).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default);
        r.Lineas.Should().HaveCount(1);
    }

    [Fact]
    public async Task Candidatos_marcan_el_proveedor_bloqueado_con_su_motivo()
    {
        await using var db = Db();
        var bloqueado = Guid.NewGuid();
        await Escenario(db, (bloqueado, 1_000m), (Guid.NewGuid(), 2_000m));

        var items = (await new PasivosParaCorridaHandler(db, new Proveedores { Bloqueados = [bloqueado] }).Handle(new("MXN"), default)).Items;

        items.Single(i => i.ProveedorId == bloqueado).Should().Match<PasivoParaCorridaResponse>(i => i.ProveedorBloqueado && i.MotivoBloqueo == "Fraude");
        items.Single(i => i.ProveedorId != bloqueado).ProveedorBloqueado.Should().BeFalse();
    }

    [Fact]
    public async Task Detalle_de_corrida_resuelve_el_nombre_del_firmante()
    {
        await using var db = Db();
        var (cuenta, p) = await Escenario(db, (Guid.NewGuid(), 1_000m));
        var r = await Enviar(db, await Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default));
        await Autorizar(db, Gerente).Handle(new(r.Id, r.Version), default);

        var c = await new ObtenerCorridaHandler(db, new Proveedores(), new Nombres(Gerente, "Ana Gerente")).Handle(new(r.Id), default);

        c.Firmas.Should().ContainSingle().Which.UsuarioNombre.Should().Be("Ana Gerente");
    }

    [Fact]
    public async Task Proveedor_sin_cuenta_validada_no_entra_a_la_corrida()
    {
        await using var db = Db();
        var sinCuenta = Guid.NewGuid();
        var (cuenta, p) = await Escenario(db, (sinCuenta, 1_000m));

        var act = () => Crear(db, Armador, new Proveedores(sinCuenta)).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PROVEEDOR_SIN_CUENTA_ACTIVA");
        var candidato = (await new PasivosParaCorridaHandler(db, new Proveedores(sinCuenta)).Handle(new("MXN"), default)).Items.Single();
        candidato.CuentaValidada.Should().BeFalse();
    }

    [Fact]
    public async Task CA4_corrida_de_650000_con_limite_500000_no_se_autoriza_hasta_la_segunda_firma()
    {
        await using var db = Db();
        var (cuenta, p) = await Escenario(db, (Guid.NewGuid(), 650_000m));
        var r = await Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default);
        r = await Enviar(db, r);
        r.FirmasRequeridas.Should().Be(2);
        r.RolRequerido.Should().Be("gerente");

        r = await Autorizar(db, JefeTesoreria).Handle(new(r.Id, r.Version), default);
        r.Estado.Should().Be("EnAutorizacion");
        r.AutorizadaPor.Should().BeNull();

        r = await Autorizar(db, Gerente).Handle(new(r.Id, r.Version), default);
        r.Estado.Should().Be("Autorizada");
        r.Firmas.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("499999.99", 1)]   // T-1
    [InlineData("500000.00", 2)]   // T: al alcanzar el límite
    [InlineData("500000.01", 2)]   // T+1
    public async Task Umbral_inclusivo_al_enviar_a_autorizacion(string monto, int firmas)
    {
        await using var db = Db();
        var (cuenta, p) = await Escenario(db, (Guid.NewGuid(), decimal.Parse(monto, System.Globalization.CultureInfo.InvariantCulture)));
        var r = await Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default);

        r = await Enviar(db, r);

        r.FirmasRequeridas.Should().Be(firmas);
    }

    [Fact]
    public async Task El_parametro_cambia_el_requisito_sin_tocar_codigo()
    {
        await using var db = Db();
        await new GuardarPoliticaFirmasHandler(db, new EmpresaContext()).Handle(
            new("MXN", [new(1_000m, 3, null)]), default);
        var (cuenta, p) = await Escenario(db, (Guid.NewGuid(), 1_000m));
        var r = await Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default);

        r = await Enviar(db, r);

        (r.FirmasRequeridas, r.RolRequerido).Should().Be((3, null));
    }

    [Fact]
    public async Task CA5_quien_armo_la_corrida_no_puede_autorizarla()
    {
        await using var db = Db();
        var (cuenta, p) = await Escenario(db, (Guid.NewGuid(), 1_000m));
        var r = await Enviar(db, await Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default));

        var act = () => Autorizar(db, Armador).Handle(new(r.Id, r.Version), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_MISMO_USUARIO");
    }

    [Fact]
    public async Task Version_vieja_lanza_concurrencia()
    {
        await using var db = Db();
        var (cuenta, p) = await Escenario(db, (Guid.NewGuid(), 1_000m));
        var r = await Enviar(db, await Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default));

        var act = () => Autorizar(db, JefeTesoreria).Handle(new(r.Id, r.Version + 5), default);

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task CxP_retira_un_pasivo_y_sale_de_la_corrida_con_aviso()
    {
        await using var db = Db();
        var (cuenta, p) = await Escenario(db, (Guid.NewGuid(), 1_000m), (Guid.NewGuid(), 2_000m));
        var r = await Crear(db, Armador).Handle(new(cuenta.Id, p.Select(x => x.FacturaProveedorId).ToList()), default);
        r = await Enviar(db, r);

        await new RetirarPasivoDePagoHandler(db, new Clock()).Handle(new(Guid.NewGuid(),
            new(Empresa, Ahora.AddMinutes(1), p[0].FacturaProveedorId, p[0].ProveedorId, "MXN", "en revisión")), default);

        var c = await new ObtenerCorridaHandler(db, new Proveedores()).Handle(new(r.Id), default);
        c.Lineas.Should().ContainSingle(l => l.FacturaProveedorId == p[1].FacturaProveedorId);
        c.Estado.Should().Be("Borrador");
        c.Total.Should().Be(2_000m);
        c.Avisos.Should().Contain(p[0].FacturaProveedorId.ToString());
    }

    private sealed class EmpresaContext : ICurrentEmpresaContext
    { public Guid? Current => Empresa; public bool IsBypassed => false; public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } } }
    private sealed class User(Guid id) : ICurrentUserContext { public Guid? UserId => id; public string? UserName => "Prueba"; }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Ahora; }
    private sealed class Roles : IFirmanteRolesReadPort
    {
        public Task<IReadOnlyCollection<string>> CodigosRolAsync(Guid usuarioId, Guid empresaId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<string>>(usuarioId == Gerente ? ["gerente"] : ["jefe-tesoreria"]);
    }
    private sealed class Nombres(Guid usuario, string nombre) : ICorteNombresReadPort
    {
        public Task<IReadOnlyDictionary<Guid, string>> NombresCajerosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.Where(i => i == usuario).ToDictionary(i => i, _ => nombre));
        public Task<IReadOnlyDictionary<Guid, string>> NombresCajasAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
    private sealed class Proveedores(params Guid[] sinCuentaValidada) : IProveedorBancoReadPort
    {
        public HashSet<Guid> SinOpinion { get; init; } = [];
        public HashSet<Guid> Bloqueados { get; init; } = [];
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ProveedorBancoDto?>(Dto(id));
        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(ids.ToDictionary(i => i, Dto));
        private ProveedorBancoDto Dto(Guid id) => new(id, "P", $"Proveedor {id.ToString()[..4]}",
            [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", "012180001234567897", true, null, "MXN", "Titular", true,
                sinCuentaValidada.Contains(id) ? "PorValidar" : "Activa")], SinOpinionCumplimiento: SinOpinion.Contains(id),
            Bloqueado: Bloqueados.Contains(id), MotivoBloqueo: Bloqueados.Contains(id) ? "Fraude" : null);
    }

    // M-147 (Javier Canché, 13-nov): libros 400,000, portal 650,000, corrida 500,000 → pasa con el saldo del portal.
    [Fact]
    public async Task M147_con_saldo_del_portal_capturado_la_corrida_no_se_bloquea_por_el_saldo_en_libros()
    {
        await using var db = Db();
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        cuenta.RegistrarSaldoInicial(400_000m, new DateOnly(2000, 1, 1), "Saldo en libros menor que el del portal");
        var p = CorridaPagoDominioTests.Pasivo(Guid.NewGuid(), 500_000m, empresa: Empresa);
        db.CuentasBancarias.Add(cuenta); db.PasivosPendientesPago.Add(p); await db.SaveChangesAsync();
        var r = await Crear(db, Armador).Handle(new(cuenta.Id, [p.FacturaProveedorId]), default);
        var enviar = new EnviarCorridaAAutorizacionHandler(db, new Proveedores(), new Clock());

        var sinPortal = () => enviar.Handle(new(r.Id, r.Version), default);
        await sinPortal.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_SALDO_INSUFICIENTE");

        var hoy = DateOnly.FromDateTime(Ahora.UtcDateTime);
        r = await enviar.Handle(new(r.Id, r.Version, 650_000m, hoy, "Captura portal BBVA"), default);
        (r.Estado, r.SaldoPortal, r.FechaConsultaPortal, r.EvidenciaSaldoPortal).Should().Be(("EnAutorizacion", 650_000m, hoy, "Captura portal BBVA"));

        // La firma reusa el saldo del portal capturado al enviar (no vuelve al saldo en libros).
        r = await Autorizar(db, Gerente).Handle(new(r.Id, r.Version), default);
        r.FirmasRecibidas.Should().Be(1);
    }

    [Fact]
    public async Task M147_saldo_del_portal_menor_al_total_al_firmar_bloquea_con_codigo_y_no_firma()
    {
        await using var db = Db();
        var (cuenta, p) = await Escenario(db, (Guid.NewGuid(), 500_000m));
        var r = await Enviar(db, await Crear(db, Armador).Handle(new(cuenta.Id, [p[0].FacturaProveedorId]), default));

        var act = () => Autorizar(db, JefeTesoreria).Handle(new(r.Id, r.Version, 499_999.99m, DateOnly.FromDateTime(Ahora.UtcDateTime), "Portal"), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_SALDO_PORTAL_INSUFICIENTE");
        (await db.Set<CorridaPagoFirma>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public void M147_validador_exige_fecha_y_evidencia_con_el_saldo_y_rechaza_negativo_o_fecha_futura()
    {
        var v = new AutorizarCorridaValidator();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        v.Validate(new AutorizarCorridaCommand(Guid.NewGuid(), 1)).IsValid.Should().BeTrue();
        v.Validate(new AutorizarCorridaCommand(Guid.NewGuid(), 1, 10m, hoy, "Portal")).IsValid.Should().BeTrue();
        v.Validate(new AutorizarCorridaCommand(Guid.NewGuid(), 1, -1m, hoy, "Portal")).IsValid.Should().BeFalse();
        v.Validate(new AutorizarCorridaCommand(Guid.NewGuid(), 1, 10m, hoy.AddDays(2), "Portal")).IsValid.Should().BeFalse();
        v.Validate(new AutorizarCorridaCommand(Guid.NewGuid(), 1, 10m, null, "Portal")).IsValid.Should().BeFalse();
        v.Validate(new AutorizarCorridaCommand(Guid.NewGuid(), 1, 10m, hoy, null)).IsValid.Should().BeFalse();
        v.Validate(new AutorizarCorridaCommand(Guid.NewGuid(), 1, null, hoy, null)).IsValid.Should().BeFalse();
        new EnviarCorridaAAutorizacionValidator().Validate(new EnviarCorridaAAutorizacionCommand(Guid.NewGuid(), 1, -1m, hoy, "x")).IsValid.Should().BeFalse();
    }
}
