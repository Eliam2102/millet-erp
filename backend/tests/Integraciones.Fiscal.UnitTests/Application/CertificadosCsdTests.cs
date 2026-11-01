using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Fiscal.Application.Certificados;
using Millet.Integraciones.Fiscal.Application.Configuracion;
using Millet.Integraciones.Fiscal.Application.Configuracion.GuardarConfiguracionPac;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Certificados;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;
using Millet.Integraciones.Fiscal.UnitTests.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.UnitTests.Application;

/// <summary>
/// F6.9 (86e3ke0d3): historial de CSD, revocación, avisos a 30/15/5 días y
/// bloqueo del timbrado con un CSD vencido o revocado. Certificados sintéticos.
/// </summary>
public sealed class CertificadosCsdTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Ahora = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
    private const string SerieA = "30001000000500003416";
    private const string SerieB = "30001000000500003417";

    // ---- Lectura del certificado ----

    [Fact]
    public void Lee_el_numero_de_certificado_del_SAT_y_el_RFC_del_titular()
    {
        var (cer, key, pass) = CsdTestFactory.Crear(
            notBefore: Ahora.AddYears(-1), notAfter: Ahora.AddYears(3),
            subject: "CN=EMPRESA DEMO, OID.2.5.4.45=EKU9003173C9 / VADA800927HSRSRL05, OU=Sucursal de prueba",
            numeroSerie: SerieA);

        var vigencia = CsdValidador.Validar(cer, key, pass, Ahora);

        (vigencia.NumeroSerie, vigencia.Rfc).Should().Be((SerieA, "EKU9003173C9"));
        CsdValidador.LeerMetadatos(cer).NumeroSerie.Should().Be(SerieA);
    }

    // ---- Política de avisos ----

    [Theory]
    [InlineData(45, new int[0], null)]
    [InlineData(30, new int[0], 30)]
    [InlineData(10, new int[0], 15)]
    [InlineData(10, new[] { 30 }, 15)]
    [InlineData(10, new[] { 30, 15 }, null)]
    [InlineData(4, new[] { 30, 15 }, 5)]
    [InlineData(0, new[] { 30, 15, 5 }, 0)]
    [InlineData(-3, new[] { 30, 15, 5, 0 }, null)]
    public void Avisa_una_vez_el_umbral_mas_urgente_cruzado(int dias, int[] avisados, int? esperado)
    {
        PoliticaAlertasCsd.UmbralPendiente(dias, avisados).Should().Be(esperado);
    }

    // ---- Bloqueo del timbrado ----

    [Fact]
    public void Bloquea_el_timbrado_con_CSD_revocado_aunque_no_haya_vencido_y_con_CSD_vencido()
    {
        var vigente = new CsdResuelto("c", "k", "p", VenceEn: Ahora.AddYears(1));
        FiscalApiTimbradoAdapter.BloqueoCsd(vigente, Ahora).Should().BeNull();
        FiscalApiTimbradoAdapter.BloqueoCsd(vigente with { RevocadoEn = Ahora.AddDays(-1) }, Ahora)!.Value.Codigo
            .Should().Be("CSD_REVOCADO");
        FiscalApiTimbradoAdapter.BloqueoCsd(vigente with { VenceEn = Ahora }, Ahora)!.Value.Codigo
            .Should().Be("CSD_VENCIDO");
        // Sin dato de vencimiento (CSD anterior a los metadatos) no se bloquea.
        FiscalApiTimbradoAdapter.BloqueoCsd(new CsdResuelto("c", "k", "p"), Ahora).Should().BeNull();
    }

    // ---- Historial al capturar ----

    [Fact]
    public async Task Capturar_un_CSD_nuevo_lo_registra_y_el_anterior_queda_sustituido()
    {
        var f = Fixture.Crear();
        await f.GuardarCsdAsync(SerieA);
        await f.GuardarCsdAsync(SerieB);

        var historial = await f.HistorialAsync();
        historial.Select(c => (c.NumeroSerie, c.Estado)).Should().BeEquivalentTo(
            [(SerieB, "Vigente"), (SerieA, "Sustituido")]);
        historial.Single(c => c.NumeroSerie == SerieA).FinalizadoPor.Should().Be("Admin fiscal");
    }

    [Fact]
    public async Task Revocar_bloquea_el_CSD_invalida_la_cache_y_no_se_vuelve_a_aceptar()
    {
        var f = Fixture.Crear();
        await f.GuardarCsdAsync(SerieA);

        var sinMotivo = () => f.Revocar("");
        await sinMotivo.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CSD_MOTIVO_REVOCACION_REQUERIDO");

        var revocado = await f.Revocar("Llave comprometida");

        revocado.Estado.Should().Be("Revocado");
        revocado.MotivoRevocacion.Should().Be("Llave comprometida");
        var config = f.Db.ConfiguracionesPac.Single();
        config.CsdRevocadoEn.Should().Be(Ahora);
        config.ObtenerEstadoCsd(Ahora).Should().Be(EstadoCsd.Revocado);
        f.Resolver.InvalidacionesRecibidas.Should().Contain((EmpresaId, ProveedorPac.FiscalApi));

        var otraVez = () => f.GuardarCsdAsync(SerieA, password: "otra-clave1");
        await otraVez.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CONFIG_PAC_CSD_REVOCADO");
    }

    // ---- Avisos ----

    [Fact]
    public async Task A_10_dias_avisa_el_umbral_de_15_una_sola_vez_y_el_dia_del_vencimiento_avisa_de_nuevo()
    {
        var f = Fixture.Crear();
        await f.GuardarCsdAsync(SerieA, venceEn: Ahora.AddDays(10).AddHours(1));

        (await f.EvaluarAsync()).Should().Be(1);
        (await f.EvaluarAsync()).Should().Be(0);

        f.Aviso.Enviados.Should().ContainSingle();
        var (aviso, destinatarios) = f.Aviso.Enviados[0];
        (aviso.UmbralDias, aviso.DiasRestantes, aviso.NumeroSerie).Should().Be((15, 10, SerieA));
        destinatarios.Should().ContainSingle().Which.Email.Should().Be("contador@demo.local");
        f.Destinatarios.RolesPedidos.Should().BeEquivalentTo(["contador-general", "super-admin"]);

        f.Clock.UtcNow = Ahora.AddDays(10).AddHours(2);
        (await f.EvaluarAsync()).Should().Be(1);
        f.Aviso.Enviados[^1].Aviso.UmbralDias.Should().Be(0);
        (await f.HistorialAsync()).Single().AlertasEnviadas.Should().Equal(15, 0);
    }

    [Fact]
    public async Task Un_CSD_capturado_antes_sin_historial_se_registra_al_evaluar()
    {
        var f = Fixture.Crear();
        await f.GuardarCsdAsync(SerieA, venceEn: Ahora.AddDays(20));
        f.Db.CertificadosCsd.RemoveRange(f.Db.CertificadosCsd);
        await f.Db.SaveChangesAsync();

        (await f.EvaluarAsync()).Should().Be(1);

        var registrado = f.Db.CertificadosCsd.Single();
        (registrado.NumeroSerie, registrado.Estado, registrado.RegistradoPor).Should().Be((SerieA, EstadoCertificadoCsd.Vigente, "sistema"));
        f.Aviso.Enviados.Single().Aviso.UmbralDias.Should().Be(30);
    }

    [Fact]
    public async Task Estado_para_el_banner_indica_dias_restantes_y_si_bloquea()
    {
        var f = Fixture.Crear();
        var sinCsd = await new EstadoCsdEmpresaHandler(f.Db, f.Empresa, f.Clock).Handle(new EstadoCsdEmpresaQuery(), CancellationToken.None);
        sinCsd.Estado.Should().Be("NoConfigurado");

        await f.GuardarCsdAsync(SerieA, venceEn: Ahora.AddDays(12).AddHours(1));
        var proximo = await new EstadoCsdEmpresaHandler(f.Db, f.Empresa, f.Clock).Handle(new EstadoCsdEmpresaQuery(), CancellationToken.None);
        (proximo.Estado, proximo.DiasRestantes, proximo.NumeroSerie, proximo.BloqueaTimbrado)
            .Should().Be(("ProximoAVencer", 12, SerieA, false));

        await f.Revocar("Revocado por el SAT");
        var revocado = await new EstadoCsdEmpresaHandler(f.Db, f.Empresa, f.Clock).Handle(new EstadoCsdEmpresaQuery(), CancellationToken.None);
        (revocado.Estado, revocado.BloqueaTimbrado).Should().Be(("Revocado", true));
    }

    // ---- Infraestructura de prueba ----

    private sealed class Probe : IPacCandidatoProbe
    {
        public Task<PingResultDto> ProbarAsync(string url, string key, CancellationToken ct) =>
            Task.FromResult(new PingResultDto(true, 200, "DEMO", 1, Ahora));
    }

    private sealed class AvisoCapturado : IAvisoCsdPort
    {
        public List<(AvisoCsd Aviso, IReadOnlyList<DestinatarioAvisoCsd> Destinatarios)> Enviados { get; } = [];
        public Task EnviarAsync(AvisoCsd aviso, IReadOnlyList<DestinatarioAvisoCsd> destinatarios, CancellationToken cancellationToken)
        {
            Enviados.Add((aviso, destinatarios));
            return Task.CompletedTask;
        }
    }

    private sealed class DestinatariosFijos : IDestinatariosAvisoCsdPort
    {
        public IReadOnlyList<string> RolesPedidos { get; private set; } = [];
        public Task<IReadOnlyList<DestinatarioAvisoCsd>> ObtenerAsync(Guid empresaId, IReadOnlyList<string> codigosRol, CancellationToken cancellationToken)
        {
            RolesPedidos = codigosRol;
            return Task.FromResult<IReadOnlyList<DestinatarioAvisoCsd>>([new DestinatarioAvisoCsd("Contador demo", "contador@demo.local")]);
        }
    }

    private sealed class Usuario(string nombre) : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName { get; } = nombre;
    }

    private sealed record Fixture(
        Millet.Integraciones.Fiscal.Infrastructure.Persistence.IntegracionesFiscalDbContext Db,
        FiscalSecretCipher Cipher,
        InMemoryFiscalDb.TrackingResolver Resolver,
        InMemoryFiscalDb.FakeEmpresaContext Empresa,
        InMemoryFiscalDb.FakeClock Clock,
        AvisoCapturado Aviso,
        DestinatariosFijos Destinatarios)
    {
        public static Fixture Crear() => new(
            InMemoryFiscalDb.Create(), InMemoryFiscalDb.Cipher(), new InMemoryFiscalDb.TrackingResolver(),
            new InMemoryFiscalDb.FakeEmpresaContext(EmpresaId), new InMemoryFiscalDb.FakeClock(Ahora),
            new AvisoCapturado(), new DestinatariosFijos());

        public async Task GuardarCsdAsync(string serie, DateTimeOffset? venceEn = null, string password = "12345678a")
        {
            var (cer, key, pass) = CsdTestFactory.Crear(password, Ahora.AddYears(-1), venceEn ?? Ahora.AddYears(3), numeroSerie: serie);
            var handler = new GuardarConfiguracionPacHandler(Db, Cipher, new InMemoryFiscalDb.CapturingPublisher(), Resolver,
                Empresa, Clock, new Probe(), new Usuario("Admin fiscal"));
            var existente = Db.ConfiguracionesPac.SingleOrDefault();
            await handler.Handle(new GuardarConfiguracionPacCommand(
                EmpresaId, ProveedorPac.FiscalApi, "https://test.fiscalapi.com", existente is null ? "DEMO-key" : null, true,
                VersionEsperada: existente?.Version, Csd: new CsdDto(cer, key, pass)), CancellationToken.None);
        }

        public Task<CertificadoCsdItem> Revocar(string motivo) =>
            new RevocarCsdHandler(Db, Resolver, Cipher, Empresa, new Usuario("Admin fiscal"), Clock)
                .Handle(new RevocarCsdCommand(EmpresaId, ProveedorPac.FiscalApi, motivo), CancellationToken.None);

        public Task<int> EvaluarAsync() =>
            new EvaluarAlertasCsdHandler(Db, Cipher, Aviso, Destinatarios, Options.Create(new AlertasCsdOptions()), Clock,
                NullLogger<EvaluarAlertasCsdHandler>.Instance).Handle(new EvaluarAlertasCsdCommand(), CancellationToken.None);

        public Task<IReadOnlyList<CertificadoCsdItem>> HistorialAsync() =>
            new ListarCertificadosCsdHandler(Db).Handle(new ListarCertificadosCsdQuery(EmpresaId), CancellationToken.None);
    }
}
