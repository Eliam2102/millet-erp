using Millet.Integraciones.Fiscal.Application.Configuracion.GuardarConfiguracionPac;
using Millet.Integraciones.Fiscal.Application.Configuracion.TestConexionPac;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.Integraciones.Fiscal.UnitTests.Application;

/// <summary>
/// M-232 (86e3zr7n2): la configuración del timbrador se activa solo con una prueba exitosa del candidato
/// exacto, guarda la prueba usada y abre una incidencia con el mensaje del timbrador cuando falla.
/// </summary>
public sealed class ConexionPacIncidenciasTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Admin = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 17, 0, 0, TimeSpan.Zero);

    private sealed class Probe : IPacCandidatoProbe
    {
        public PingResultDto Resultado { get; set; } = new(true, 200, "OK", 5, Ahora.AddMinutes(-5));
        public Task<PingResultDto> ProbarAsync(string url, string key, CancellationToken ct) => Task.FromResult(Resultado);
    }

    private sealed class Incidencias : IIncidenciasPort
    {
        public List<AbrirIncidenciaSolicitud> Abiertas { get; } = [];
        public List<(string Paso, Guid DocumentoId)> Resueltas { get; } = [];

        public Task<IncidenciaAbierta> AbrirAsync(AbrirIncidenciaSolicitud solicitud, CancellationToken cancellationToken)
        {
            Abiertas.Add(solicitud);
            return Task.FromResult(new IncidenciaAbierta(Guid.NewGuid(), Abiertas.Count, false));
        }

        public Task<int> ResolverPorDocumentoAsync(string paso, Guid documentoId, string? detalleClave, string resolucion, CancellationToken cancellationToken)
        {
            Resueltas.Add((paso, documentoId));
            return Task.FromResult(1);
        }

        public Task<bool> HayPendientesAsync(Guid documentoId, string? paso, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class Usuario : ICurrentUserContext
    {
        public Guid? UserId => Admin;
        public string? UserName => "Administrador DEMO";
    }

    private static GuardarConfiguracionPacCommand Activar(string? apiKey = "sk_test_demo", int? version = null) =>
        new(EmpresaId, ProveedorPac.FiscalApi, "https://test.fiscalapi.com", apiKey, Activo: true, VersionEsperada: version);

    private static (GuardarConfiguracionPacHandler Guardar, Millet.Integraciones.Fiscal.Infrastructure.Persistence.IntegracionesFiscalDbContext Db,
        FiscalSecretCipher Cipher, Probe Probe, Incidencias Incidencias) Build()
    {
        var db = InMemoryFiscalDb.Create();
        var cipher = InMemoryFiscalDb.Cipher();
        var probe = new Probe();
        var incidencias = new Incidencias();
        var handler = new GuardarConfiguracionPacHandler(db, cipher, new InMemoryFiscalDb.CapturingPublisher(),
            new InMemoryFiscalDb.TrackingResolver(), new InMemoryFiscalDb.FakeEmpresaContext(EmpresaId),
            new InMemoryFiscalDb.FakeClock(Ahora), probe, new Usuario(), incidencias);
        return (handler, db, cipher, probe, incidencias);
    }

    [Fact]
    public async Task Criterio1_activar_sin_conexion_valida_se_rechaza_con_Pruebe_la_conexion_y_sigue_inactiva()
    {
        var (guardar, db, _, probe, _) = Build();
        await guardar.Handle(Activar() with { Activo = false }, default);
        probe.Resultado = new(false, 401, "Invalid API key", 5, Ahora);

        var act = () => guardar.Handle(Activar(apiKey: null, version: db.ConfiguracionesPac.Single().Version), default);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().StartWith("Pruebe la conexión antes de activar");
        db.ConfiguracionesPac.Single().Activo.Should().BeFalse();
    }

    [Fact]
    public async Task Criterio2_con_prueba_exitosa_queda_activa_y_guarda_la_fecha_de_la_prueba_usada()
    {
        var (guardar, db, _, probe, incidencias) = Build();

        var r = await guardar.Handle(Activar(), default);

        r.Activo.Should().BeTrue();
        var prueba = db.PruebasConexionPac.Single();
        (prueba.RealizadaEn, prueba.Exitosa, prueba.Origen).Should().Be((probe.Resultado.ConsultadoEn, true, OrigenPruebaConexionPac.Guardado));
        prueba.Mensaje.Should().Be("Conexión exitosa con FiscalAPI.");
        incidencias.Resueltas.Should().ContainSingle().Which.Should().Be((PasosIncidencia.FisPacConexionRechazada, db.ConfiguracionesPac.Single().Id));
    }

    [Fact]
    public async Task Criterio3_credenciales_rechazadas_al_guardar_abren_incidencia_con_el_mensaje_y_no_cambian_la_config_activa()
    {
        var (guardar, db, cipher, probe, incidencias) = Build();
        await guardar.Handle(Activar(), default);
        var antes = db.ConfiguracionesPac.Single();
        var (hashAntes, versionAntes) = (antes.ApiKeyHash, antes.Version);
        probe.Resultado = new(false, 401, "Invalid API key: sk_test_nueva", 5, Ahora);

        var act = () => guardar.Handle(Activar(apiKey: "sk_test_nueva", version: versionAntes), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CONFIG_PAC_CONEXION_RECHAZADA");
        var despues = db.ConfiguracionesPac.Single();
        (despues.Activo, despues.ApiKeyHash, despues.Version).Should().Be((true, hashAntes, versionAntes));
        cipher.Decrypt(despues.ApiKeyCifrado).Should().Be("sk_test_demo");
        var incidencia = incidencias.Abiertas.Should().ContainSingle().Subject;
        incidencia.Paso.Should().Be(PasosIncidencia.FisPacConexionRechazada);
        incidencia.DocumentoId.Should().Be(despues.Id);
        incidencia.ResponsableUsuarioId.Should().Be(Admin);
        incidencia.Detalle.Should().Contain("HTTP 401").And.Contain("Invalid API key");
        incidencia.PasoReanudacion.Should().Be(PasosIncidencia.FisPacConfiguracion);
    }

    [Fact]
    public async Task Criterio3_probar_la_config_guardada_con_credenciales_rechazadas_abre_incidencia_y_registra_la_falla()
    {
        var (guardar, db, cipher, probe, _) = Build();
        await guardar.Handle(Activar(), default);
        var incidencias = new Incidencias();
        var sdk = new SdkPing(new PingResultDto(false, 401, "Invalid API key", 5, Ahora));
        var test = new TestConexionPacHandler(sdk, db, cipher, probe, incidencias, new Usuario());

        var r = await test.Handle(new TestConexionPacCommand(EmpresaId, ProveedorPac.FiscalApi, null, null, null), default);

        r.Exitosa.Should().BeFalse();
        r.Mensaje.Should().NotContain("Invalid API key"); // el texto crudo solo va a la incidencia
        var config = db.ConfiguracionesPac.Single();
        config.Activo.Should().BeTrue();
        db.PruebasConexionPac.Where(p => p.Origen == OrigenPruebaConexionPac.Prueba).Should().ContainSingle().Which.Exitosa.Should().BeFalse();
        incidencias.Abiertas.Should().ContainSingle().Which.Detalle.Should().Contain("Invalid API key");
    }

    [Fact]
    public async Task Probar_un_candidato_distinto_no_registra_la_prueba_ni_abre_incidencia()
    {
        var (guardar, db, cipher, probe, _) = Build();
        await guardar.Handle(Activar(), default);
        var antes = db.PruebasConexionPac.Count();
        var incidencias = new Incidencias();
        probe.Resultado = new(false, 401, "Invalid API key", 5, Ahora.AddMinutes(1));
        var test = new TestConexionPacHandler(new SdkPing(probe.Resultado), db, cipher, probe, incidencias, new Usuario());

        await test.Handle(new TestConexionPacCommand(EmpresaId, ProveedorPac.FiscalApi, "https://test.fiscalapi.com", "sk_test_otra", 5), default);

        db.PruebasConexionPac.Count().Should().Be(antes);
        incidencias.Abiertas.Should().BeEmpty();
    }

    [Theory]
    [InlineData(422)]
    [InlineData(501)]
    public async Task Sin_credenciales_o_integracion_apagada_no_es_falla_del_timbrador_y_no_abre_incidencia(int status)
    {
        var (guardar, db, cipher, probe, _) = Build();
        await guardar.Handle(Activar(), default);
        var incidencias = new Incidencias();
        var test = new TestConexionPacHandler(new SdkPing(new PingResultDto(false, status, "x", 1, Ahora)), db, cipher, probe, incidencias, new Usuario());

        await test.Handle(new TestConexionPacCommand(EmpresaId, ProveedorPac.FiscalApi, null, null, null), default);

        incidencias.Abiertas.Should().BeEmpty();
    }

    /// <summary>Solo responde el ping de la configuración guardada.</summary>
    private sealed class SdkPing(PingResultDto resultado) : IFiscalApiSdkClient
    {
        public Task<PingResultDto> PingAsync(Guid empresaId, CancellationToken cancellationToken) => Task.FromResult(resultado);
        public Task<PersonExterno> AsegurarPersonAsync(Guid empresaId, string rfc, string legalName, string zipCode,
            string satTaxRegimeCode, string? satCfdiUseCode, string email, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SincronizarPersonAsync(Guid empresaId, string personIdExterno, string legalName, string zipCode,
            string? satCfdiUseCode, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TaxFilesSubidos> SubirTaxFilesAsync(Guid empresaId, string personIdExterno, string rfc, byte[] cerBytes,
            byte[] keyBytes, string password, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<DownloadRuleExternaDto> AsegurarDownloadRuleAsync(Guid empresaId, string personIdExterno, SatQueryType satQueryType,
            DownloadType downloadType, SatInvoiceStatusFilter satInvoiceStatus, string descripcion, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<SolicitudDescargaExternaDto> CrearSolicitudAsync(Guid empresaId, string ruleIdExterno, DateTimeOffset startDate,
            DateTimeOffset endDate, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SolicitudDescargaExternaDto> ConsultarSolicitudAsync(Guid empresaId, string requestIdExterno,
            CancellationToken cancellationToken) => throw new NotImplementedException();
        public IAsyncEnumerable<MetaItemDto> ListarMetaItemsAsync(Guid empresaId, string requestIdExterno,
            CancellationToken cancellationToken) => throw new NotImplementedException();
        public IAsyncEnumerable<XmlCfdiItemDto> ListarXmlsAsync(Guid empresaId, string requestIdExterno,
            CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<EstatusUuidDto> ConsultarEstatusUuidAsync(Guid empresaId, string uuidCfdi, string rfcEmisor, string rfcReceptor,
            decimal total, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
