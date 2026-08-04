using Millet.Integraciones.Fiscal.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.UnitTests.Domain;

public sealed class ConfiguracionPacTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Ahora = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] CifradoSample = [0x01, 0x02, 0x03, 0x04];
    private const string HashSample = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private static ConfiguracionPac NewDefault() =>
        new(Guid.NewGuid(), EmpresaId, ProveedorPac.FiscalApi,
            "https://api.fiscalapi.com", CifradoSample, HashSample, Ahora);

    [Fact]
    public void Constructor_marca_activo_y_setea_ultima_rotacion()
    {
        var cfg = NewDefault();

        cfg.Activo.Should().BeTrue();
        cfg.UltimaRotacionAt.Should().Be(Ahora);
    }

    [Fact]
    public void Constructor_rechaza_empresa_vacia()
    {
        var act = () => new ConfiguracionPac(
            Guid.NewGuid(), Guid.Empty, ProveedorPac.FiscalApi,
            "https://api.fiscalapi.com", CifradoSample, HashSample, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONFIG_PAC_EMPRESA_INVALIDA");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("ftp://api.fiscalapi.com")]
    public void Constructor_rechaza_base_url_invalida(string baseUrl)
    {
        var act = () => new ConfiguracionPac(
            Guid.NewGuid(), EmpresaId, ProveedorPac.FiscalApi,
            baseUrl, CifradoSample, HashSample, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONFIG_PAC_BASE_URL_INVALIDA");
    }

    [Fact]
    public void Constructor_rechaza_apikey_cifrado_vacio()
    {
        var act = () => new ConfiguracionPac(
            Guid.NewGuid(), EmpresaId, ProveedorPac.FiscalApi,
            "https://api.fiscalapi.com", [], HashSample, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONFIG_PAC_APIKEY_INVALIDA");
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("not-64-chars-hex-string-but-long-enough-just-not-correct-length")]
    public void Constructor_rechaza_hash_no_sha256(string hash)
    {
        var act = () => new ConfiguracionPac(
            Guid.NewGuid(), EmpresaId, ProveedorPac.FiscalApi,
            "https://api.fiscalapi.com", CifradoSample, hash, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONFIG_PAC_APIKEY_HASH_INVALIDO");
    }

    [Fact]
    public void RotarApiKey_actualiza_cuando_hash_difiere()
    {
        var cfg = NewDefault();
        var nuevoCifrado = new byte[] { 0xAA, 0xBB };
        var nuevoHash = new string('f', 64);
        var despues = Ahora.AddDays(30);

        cfg.RotarApiKey(nuevoCifrado, nuevoHash, despues);

        cfg.ApiKeyCifrado.Should().Equal(nuevoCifrado);
        cfg.ApiKeyHash.Should().Be(nuevoHash);
        cfg.UltimaRotacionAt.Should().Be(despues);
    }

    [Fact]
    public void RotarApiKey_es_idempotente_cuando_hash_coincide()
    {
        var cfg = NewDefault();
        var despues = Ahora.AddDays(30);

        cfg.RotarApiKey([0x99], HashSample, despues);

        cfg.ApiKeyCifrado.Should().Equal(CifradoSample); // sin cambio
        cfg.UltimaRotacionAt.Should().Be(Ahora);          // sin cambio
    }

    [Fact]
    public void Desactivar_marca_inactivo_sin_borrar()
    {
        var cfg = NewDefault();

        cfg.Desactivar();

        cfg.Activo.Should().BeFalse();
    }

    [Fact]
    public void Activar_es_idempotente()
    {
        var cfg = NewDefault();
        cfg.Desactivar();

        cfg.Activar();
        cfg.Activar();

        cfg.Activo.Should().BeTrue();
    }

    [Fact]
    public void RegistrarTestConexion_actualiza_timestamp_y_flag()
    {
        var cfg = NewDefault();
        var t1 = Ahora.AddMinutes(5);

        cfg.RegistrarTestConexion(true, t1);

        cfg.UltimaTestConexionAt.Should().Be(t1);
        cfg.UltimaTestConexionExitosa.Should().BeTrue();
    }

    // ───────────────────────── Identidades sandbox ─────────────────────────

    private static ConfiguracionPac NewSandbox() =>
        new(Guid.NewGuid(), EmpresaId, ProveedorPac.FiscalApi,
            "https://test.fiscalapi.com", CifradoSample, HashSample, Ahora);

    private static IdentidadSandbox Eku() =>
        new("EKU9003173C9", "ESCUELA KEMPER URGATE", "601", "42501");

    [Fact]
    public void EsSandbox_solo_con_host_de_pruebas()
    {
        NewSandbox().EsSandbox.Should().BeTrue();
        NewDefault().EsSandbox.Should().BeFalse();
    }

    [Fact]
    public void ConfigurarIdentidadesSandbox_asigna_en_sandbox()
    {
        var cfg = NewSandbox();

        cfg.ConfigurarIdentidadesSandbox(Eku(), Eku());

        cfg.EmisorSandbox!.Rfc.Should().Be("EKU9003173C9");
        cfg.ReceptorSandbox!.CodigoPostal.Should().Be("42501");
    }

    [Fact]
    public void ConfigurarIdentidadesSandbox_rechaza_fuera_de_sandbox()
    {
        var cfg = NewDefault(); // BaseUrl productiva

        var act = () => cfg.ConfigurarIdentidadesSandbox(Eku(), null);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONFIG_PAC_IDENTIDAD_REQUIERE_SANDBOX");
    }

    [Fact]
    public void ConfigurarIdentidadesSandbox_null_limpia_sin_importar_url()
    {
        var cfg = NewDefault();

        var act = () => cfg.ConfigurarIdentidadesSandbox(null, null);

        act.Should().NotThrow();
        cfg.EmisorSandbox.Should().BeNull();
    }

    [Fact]
    public void ActualizarBaseUrl_a_live_limpia_identidades()
    {
        var cfg = NewSandbox();
        cfg.ConfigurarIdentidadesSandbox(Eku(), Eku());

        cfg.ActualizarBaseUrl("https://live.fiscalapi.com");

        cfg.EmisorSandbox.Should().BeNull();
        cfg.ReceptorSandbox.Should().BeNull();
    }

    [Theory]
    [InlineData("XX", "IDENTIDAD_SANDBOX_RFC_INVALIDO")]           // RFC corto
    [InlineData("EKU9003173C9X9", "IDENTIDAD_SANDBOX_RFC_INVALIDO")] // RFC largo
    public void IdentidadSandbox_rechaza_rfc_invalido(string rfc, string code)
    {
        var act = () => new IdentidadSandbox(rfc, "X", "601", "42501");

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be(code);
    }

    [Fact]
    public void IdentidadSandbox_rechaza_cp_invalido()
    {
        var act = () => new IdentidadSandbox("EKU9003173C9", "ESCUELA KEMPER URGATE", "601", "425");

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("IDENTIDAD_SANDBOX_CP_INVALIDO");
    }

    [Fact]
    public void IdentidadSandbox_normaliza_rfc_a_mayusculas()
    {
        new IdentidadSandbox("eku9003173c9", "ESCUELA KEMPER URGATE", "601", "42501")
            .Rfc.Should().Be("EKU9003173C9");
    }

    // ───────────────────────── CSD del emisor ─────────────────────────

    private const string OtroHash = "0000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void ConfigurarCsd_captura_las_tres_piezas_y_marca_configurado()
    {
        var cfg = NewDefault();
        cfg.CsdConfigurado.Should().BeFalse();

        cfg.ConfigurarCsd([0xA1], [0xB2], [0xC3], HashSample, Ahora);

        cfg.CsdConfigurado.Should().BeTrue();
        cfg.CsdHash.Should().Be(HashSample);
        cfg.CsdActualizadoAt.Should().Be(Ahora);
    }

    [Fact]
    public void ConfigurarCsd_es_idempotente_cuando_hash_coincide()
    {
        var cfg = NewDefault();
        cfg.ConfigurarCsd([0xA1], [0xB2], [0xC3], HashSample, Ahora);

        cfg.ConfigurarCsd([0xFF], [0xFF], [0xFF], HashSample, Ahora.AddDays(1));

        cfg.CsdCertificadoCifrado.Should().Equal([0xA1]); // no re-cifra
        cfg.CsdActualizadoAt.Should().Be(Ahora);
    }

    [Fact]
    public void ConfigurarCsd_rota_cuando_hash_difiere()
    {
        var cfg = NewDefault();
        cfg.ConfigurarCsd([0xA1], [0xB2], [0xC3], HashSample, Ahora);

        cfg.ConfigurarCsd([0xD4], [0xE5], [0xF6], OtroHash, Ahora.AddDays(1));

        cfg.CsdCertificadoCifrado.Should().Equal([0xD4]);
        cfg.CsdHash.Should().Be(OtroHash);
        cfg.CsdActualizadoAt.Should().Be(Ahora.AddDays(1));
    }

    [Fact]
    public void ConfigurarCsd_rechaza_piezas_incompletas()
    {
        var cfg = NewDefault();

        var act = () => cfg.ConfigurarCsd([0xA1], [], [0xC3], HashSample, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONFIG_PAC_CSD_INCOMPLETO");
    }

    [Fact]
    public void ConfigurarCsd_rechaza_hash_no_sha256()
    {
        var cfg = NewDefault();

        var act = () => cfg.ConfigurarCsd([0xA1], [0xB2], [0xC3], "corto", Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONFIG_PAC_CSD_HASH_INVALIDO");
    }

    [Fact]
    public void LimpiarCsd_borra_todo_y_es_idempotente()
    {
        var cfg = NewDefault();
        cfg.ConfigurarCsd([0xA1], [0xB2], [0xC3], HashSample, Ahora);

        cfg.LimpiarCsd();
        cfg.LimpiarCsd();

        cfg.CsdConfigurado.Should().BeFalse();
        cfg.CsdCertificadoCifrado.Should().BeNull();
        cfg.CsdLlavePrivadaCifrada.Should().BeNull();
        cfg.CsdPasswordCifrado.Should().BeNull();
        cfg.CsdHash.Should().BeNull();
        cfg.CsdActualizadoAt.Should().BeNull();
    }

    [Fact]
    public void ActualizarBaseUrl_a_live_conserva_el_csd()
    {
        // A diferencia de las identidades sandbox, el CSD NO se limpia al
        // salir de sandbox: en live se rota al CSD real (un CSD de prueba
        // en live falla visible en el PAC, nunca silencioso).
        var cfg = NewSandbox();
        cfg.ConfigurarCsd([0xA1], [0xB2], [0xC3], HashSample, Ahora);

        cfg.ActualizarBaseUrl("https://live.fiscalapi.com");

        cfg.CsdConfigurado.Should().BeTrue();
    }
}
