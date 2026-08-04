using Millet.Integraciones.Fiscal.Application.Configuracion;
using Millet.Integraciones.Fiscal.Application.Configuracion.GuardarConfiguracionPac;
using Millet.Integraciones.Fiscal.Application.IntegrationEvents;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.UnitTests.Application;

public sealed class GuardarConfiguracionPacHandlerTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Ahora = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    private static GuardarConfiguracionPacCommand NewCommand(string? apiKey = "fiscal-api-key-secret") =>
        new(
            EmpresaId: EmpresaId,
            Proveedor: ProveedorPac.FiscalApi,
            BaseUrl: "https://live.fiscalapi.com",
            ApiKey: apiKey,
            Activo: true);

    private static (GuardarConfiguracionPacHandler Handler,
                    Millet.Integraciones.Fiscal.Infrastructure.Persistence.IntegracionesFiscalDbContext Db,
                    InMemoryFiscalDb.CapturingPublisher Publisher,
                    FiscalSecretCipher Cipher,
                    InMemoryFiscalDb.TrackingResolver Resolver)
        Build(Guid? empresaActual = null)
    {
        var db = InMemoryFiscalDb.Create();
        var cipher = InMemoryFiscalDb.Cipher();
        var publisher = new InMemoryFiscalDb.CapturingPublisher();
        var resolver = new InMemoryFiscalDb.TrackingResolver();
        var empresa = new InMemoryFiscalDb.FakeEmpresaContext(empresaActual ?? EmpresaId);
        var clock = new InMemoryFiscalDb.FakeClock(Ahora);
        var handler = new GuardarConfiguracionPacHandler(db, cipher, publisher, resolver, empresa, clock);
        return (handler, db, publisher, cipher, resolver);
    }

    [Fact]
    public async Task Crear_cifra_apikey_publica_evento_y_marca_rotacion()
    {
        var (handler, db, publisher, cipher, _) = Build();

        var response = await handler.Handle(NewCommand(apiKey: "secret-key-123"), CancellationToken.None);

        response.EmpresaId.Should().Be(EmpresaId);
        response.ApiKey.Should().Be("••••");
        response.ApiKeyConfigured.Should().BeTrue();
        response.Activo.Should().BeTrue();

        var persisted = db.ConfiguracionesPac.Single();
        cipher.Decrypt(persisted.ApiKeyCifrado).Should().Be("secret-key-123");
        persisted.ApiKeyHash.Should().Be(FiscalSecretCipher.HashForChangeDetection("secret-key-123"));

        publisher.Published.Should().HaveCount(1);
        var evt = publisher.Published.Single() as IntegracionesFiscalConfiguracionActualizadaEvent;
        evt.Should().NotBeNull();
        evt!.Rotacion.Should().BeTrue();
        evt.EmpresaId.Should().Be(EmpresaId);
    }

    [Fact]
    public async Task Crear_sin_apikey_lanza_BusinessRule()
    {
        var (handler, _, _, _, _) = Build();

        var act = () => handler.Handle(NewCommand(apiKey: null), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<BusinessRuleException>()).Subject.First();
        ex.Code.Should().Be("CONFIG_PAC_APIKEY_REQUERIDA");
    }

    [Fact]
    public async Task EmpresaActual_distinta_del_command_lanza_CrossTenantViolation()
    {
        var otraEmpresa = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var (handler, _, _, _, _) = Build(empresaActual: otraEmpresa);

        var act = () => handler.Handle(NewCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<CrossTenantViolationException>();
    }

    [Fact]
    public async Task Update_sin_apikey_no_rota()
    {
        var (handler, db, publisher, cipher, _) = Build();
        await handler.Handle(NewCommand(apiKey: "original-key"), CancellationToken.None);

        var configOriginal = db.ConfiguracionesPac.Single();
        var hashOriginal = configOriginal.ApiKeyHash;
        var cifradoOriginal = configOriginal.ApiKeyCifrado;

        var responseUpdate = await handler.Handle(
            NewCommand(apiKey: null) with { BaseUrl = "https://live.fiscalapi.com" },
            CancellationToken.None);

        db.ConfiguracionesPac.Single().ApiKeyHash.Should().Be(hashOriginal);
        db.ConfiguracionesPac.Single().ApiKeyCifrado.Should().Equal(cifradoOriginal);
        publisher.Published.Count(p => p is IntegracionesFiscalConfiguracionActualizadaEvent e && e.Rotacion).Should().Be(1); // solo el original
    }

    [Fact]
    public async Task Update_con_apikey_distinta_rota_y_actualiza_timestamp()
    {
        var (handler, db, publisher, cipher, _) = Build();
        await handler.Handle(NewCommand(apiKey: "original-key"), CancellationToken.None);

        var responseUpdate = await handler.Handle(
            NewCommand(apiKey: "nueva-key-rotada"),
            CancellationToken.None);

        var persisted = db.ConfiguracionesPac.Single();
        cipher.Decrypt(persisted.ApiKeyCifrado).Should().Be("nueva-key-rotada");
        persisted.ApiKeyHash.Should().Be(FiscalSecretCipher.HashForChangeDetection("nueva-key-rotada"));
        publisher.Published.Count(p => p is IntegracionesFiscalConfiguracionActualizadaEvent e && e.Rotacion).Should().Be(2);
    }

    [Fact]
    public async Task Update_con_apikey_igual_es_idempotente()
    {
        var (handler, db, publisher, _, _) = Build();
        await handler.Handle(NewCommand(apiKey: "same-key"), CancellationToken.None);
        var cifradoInicial = db.ConfiguracionesPac.Single().ApiKeyCifrado;

        await handler.Handle(NewCommand(apiKey: "same-key"), CancellationToken.None);

        db.ConfiguracionesPac.Single().ApiKeyCifrado.Should().Equal(cifradoInicial); // no re-cifró
        publisher.Published.Count(p => p is IntegracionesFiscalConfiguracionActualizadaEvent e && e.Rotacion).Should().Be(1);
    }

    [Fact]
    public async Task Update_desactiva_si_Activo_es_false()
    {
        var (handler, db, _, _, _) = Build();
        await handler.Handle(NewCommand(), CancellationToken.None);

        await handler.Handle(NewCommand(apiKey: null) with { Activo = false }, CancellationToken.None);

        db.ConfiguracionesPac.Single().Activo.Should().BeFalse();
    }

    [Fact]
    public async Task Guardar_invalida_cache_del_resolver()
    {
        var (handler, _, _, _, resolver) = Build();

        await handler.Handle(NewCommand(), CancellationToken.None);

        resolver.InvalidacionesRecibidas.Should().ContainSingle()
            .Which.Should().Be((EmpresaId, ProveedorPac.FiscalApi));
    }

    // ───────────────────────── Identidades sandbox ─────────────────────────

    private static readonly IdentidadSandboxDto Eku =
        new("EKU9003173C9", "ESCUELA KEMPER URGATE", "601", "42501");

    private static readonly IdentidadSandboxDto Cacx =
        new("CACX7605101P8", "XOCHILT CASAS CHAVEZ", "612", "36257");

    private static GuardarConfiguracionPacCommand SandboxCommand(
        IdentidadSandboxDto? emisor, IdentidadSandboxDto? receptor, string? apiKey = "sk_test_key") =>
        NewCommand(apiKey) with
        {
            BaseUrl = "https://test.fiscalapi.com",
            EmisorSandbox = emisor,
            ReceptorSandbox = receptor,
        };

    [Fact]
    public async Task Guardar_identidades_sandbox_persiste_y_regresa_en_response()
    {
        var (handler, db, _, _, _) = Build();

        var response = await handler.Handle(SandboxCommand(Eku, Cacx), CancellationToken.None);

        response.EmisorSandbox!.Rfc.Should().Be("EKU9003173C9");
        response.ReceptorSandbox!.Rfc.Should().Be("CACX7605101P8");
        var persisted = db.ConfiguracionesPac.Single();
        persisted.EmisorSandbox!.CodigoPostal.Should().Be("42501");
        persisted.ReceptorSandbox!.RazonSocial.Should().Be("XOCHILT CASAS CHAVEZ");
    }

    [Fact]
    public async Task Guardar_identidades_con_base_url_live_lanza_BusinessRule()
    {
        var (handler, _, _, _, _) = Build();

        var act = () => handler.Handle(
            NewCommand() with { EmisorSandbox = Eku }, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<BusinessRuleException>()).Subject.First();
        ex.Code.Should().Be("CONFIG_PAC_IDENTIDAD_REQUIERE_SANDBOX");
    }

    [Fact]
    public async Task Update_reemplaza_y_limpia_identidades_sandbox()
    {
        var (handler, db, _, _, _) = Build();
        await handler.Handle(SandboxCommand(Eku, Eku), CancellationToken.None);

        // Reemplazo del owned type sobre entidad trackeada.
        await handler.Handle(SandboxCommand(Eku, Cacx, apiKey: null), CancellationToken.None);
        db.ConfiguracionesPac.Single().ReceptorSandbox!.Rfc.Should().Be("CACX7605101P8");

        // Upsert con null limpia.
        await handler.Handle(SandboxCommand(null, null, apiKey: null), CancellationToken.None);
        var persisted = db.ConfiguracionesPac.Single();
        persisted.EmisorSandbox.Should().BeNull();
        persisted.ReceptorSandbox.Should().BeNull();
    }

    [Fact]
    public async Task Update_a_live_limpia_identidades_previas()
    {
        var (handler, db, _, _, _) = Build();
        await handler.Handle(SandboxCommand(Eku, Eku), CancellationToken.None);

        await handler.Handle(NewCommand(apiKey: null), CancellationToken.None); // BaseUrl live, sin identidades

        var persisted = db.ConfiguracionesPac.Single();
        persisted.EmisorSandbox.Should().BeNull();
        persisted.ReceptorSandbox.Should().BeNull();
    }

    // ───────────────────────── CSD del emisor ─────────────────────────
    // Material sintético REAL (cert self-signed + llave PKCS#8 cifrada):
    // desde PR-C el handler valida el trío al guardar (CsdValidador) y un
    // fake de bytes arbitrarios sería rechazado. Vigencia amplia alrededor
    // del FakeClock (Ahora = 2026-05-25).

    private static readonly CsdDto CsdEku = CrearCsdDto("12345678a");

    private static CsdDto CrearCsdDto(string password)
    {
        var (cer, key, pass) = Infrastructure.CsdTestFactory.Crear(
            password, notBefore: Ahora.AddYears(-1), notAfter: Ahora.AddYears(4));
        return new CsdDto(cer, key, pass);
    }

    [Fact]
    public async Task Guardar_csd_cifra_las_tres_piezas_y_expone_configurado()
    {
        var (handler, db, publisher, cipher, _) = Build();

        var response = await handler.Handle(
            NewCommand() with { Csd = CsdEku }, CancellationToken.None);

        response.CsdConfigurado.Should().BeTrue();
        response.CsdActualizadoAt.Should().Be(Ahora);

        var persisted = db.ConfiguracionesPac.Single();
        cipher.Decrypt(persisted.CsdCertificadoCifrado!).Should().Be(CsdEku.CertificadoBase64);
        cipher.Decrypt(persisted.CsdLlavePrivadaCifrada!).Should().Be(CsdEku.LlavePrivadaBase64);
        cipher.Decrypt(persisted.CsdPasswordCifrado!).Should().Be("12345678a");

        var evt = publisher.Published.Single() as IntegracionesFiscalConfiguracionActualizadaEvent;
        evt!.Rotacion.Should().BeTrue();
    }

    [Fact]
    public async Task Guardar_csd_igual_es_idempotente_y_distinto_rota()
    {
        var (handler, db, _, _, _) = Build();
        await handler.Handle(NewCommand() with { Csd = CsdEku }, CancellationToken.None);
        var hashOriginal = db.ConfiguracionesPac.Single().CsdHash;

        // Mismo CSD: no re-cifra (hash idéntico).
        await handler.Handle(NewCommand(apiKey: null) with { Csd = CsdEku }, CancellationToken.None);
        db.ConfiguracionesPac.Single().CsdHash.Should().Be(hashOriginal);

        // CSD distinto (otro par cert/llave válido): rota.
        await handler.Handle(
            NewCommand(apiKey: null) with { Csd = CrearCsdDto("otro-pass") },
            CancellationToken.None);
        db.ConfiguracionesPac.Single().CsdHash.Should().NotBe(hashOriginal);
    }

    [Fact]
    public async Task Guardar_csd_con_password_incorrecta_es_rechazado_sin_persistir()
    {
        var (handler, db, _, _, _) = Build();
        await handler.Handle(NewCommand(), CancellationToken.None); // config base sin CSD

        var invalido = CsdEku with { Password = "password-equivocada" };
        var act = () => handler.Handle(
            NewCommand(apiKey: null) with { Csd = invalido }, CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("CONFIG_PAC_CSD_PASSWORD_INCORRECTA");
        db.ConfiguracionesPac.Single().CsdConfigurado.Should().BeFalse();
    }

    [Fact]
    public async Task Guardar_sin_csd_no_toca_el_persistido()
    {
        var (handler, db, _, _, _) = Build();
        await handler.Handle(NewCommand() with { Csd = CsdEku }, CancellationToken.None);

        await handler.Handle(NewCommand(apiKey: null), CancellationToken.None); // Csd null = no tocar

        db.ConfiguracionesPac.Single().CsdConfigurado.Should().BeTrue();
    }
}
